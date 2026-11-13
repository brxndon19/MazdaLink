using System.Reflection;
using SpeedLink.Core;
using SpeedLink.J2534;

// MazdaLink Reader: a read-only command-line ROM reader for Mazda ECUs over a J2534 cable.
// Every request goes through DiagnosticProtocol.CheckReadOnly inside J2534ReadTransport, so
// erase, write and programming requests are blocked before they reach the cable.

const string Usage = """
MazdaLink Reader (read-only)

Usage:
  MazdaLinkReader probe   [options]   Identify the ECU, unlock reading and read two 16-byte samples.
  MazdaLinkReader scan    [options]   Try a set of small reads to work out which sizes and addresses the ECU accepts.
  MazdaLinkReader read    [options]   Read the whole ROM twice and save it only if both reads match.
  MazdaLinkReader drivers [options]   List the installed J2534 drivers for the selected adapter.

Options:
  --adapter mongoose|tactrix   Cable to use (default: mongoose, the Mongoose-Plus ISO/CAN3).
  --driver <name or dll>       Pick one driver when more than one is installed.
  --gen2                       Use the Gen 2 read protocol when the calibration is not recognised.
  --block <bytes>              Read in smaller blocks (for read). Use the size scan recommends.
  --out <folder>               Where to save ROMs and logs (default: a "MazdaLink" folder in Documents).

Before probe or read: ignition ON, engine OFF, headlights, fan and accessories OFF,
and a healthy battery or charger connected.
""";

bool interactive = args.Length == 0 && !Console.IsInputRedirected && !Console.IsOutputRedirected;
if (interactive)
    args = AskForCommand();
int code = await RunAsync(args);
if (interactive)
{
    Console.WriteLine();
    Console.WriteLine("Press Enter to close.");
    Console.ReadLine();
}
return code;

static async Task<int> RunAsync(string[] args)
{
    string? command = null;
    int? block = null;
    string adapterName = "mongoose", driverName = "", outputFolder = "";
    bool gen2 = false;
    for (int index = 0; index < args.Length; index++)
    {
        string arg = args[index];
        string Next() => index + 1 < args.Length ? args[++index] : throw new ArgumentException($"{arg} needs a value.");
        try
        {
            switch (arg)
            {
                case "--adapter": adapterName = Next().ToLowerInvariant(); break;
                case "--driver": driverName = Next(); break;
                case "--out": outputFolder = Next(); break;
                case "--gen2": gen2 = true; break;
                case "--block":
                    block = int.TryParse(Next(), out int size) && size is >= 1 and <= 3968 ? size : throw new ArgumentException("--block needs a size from 1 to 3968 bytes.");
                    break;
                case "-h" or "--help" or "help": Console.WriteLine(Usage); return 0;
                default:
                    if (command is null && !arg.StartsWith('-')) command = arg.ToLowerInvariant();
                    else throw new ArgumentException($"Unknown option: {arg}");
                    break;
            }
        }
        catch (ArgumentException error)
        {
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine(Usage);
            return 2;
        }
    }
    if (command is not ("probe" or "scan" or "read" or "drivers"))
    {
        Console.WriteLine(Usage);
        return command is null ? 0 : 2;
    }

    J2534AdapterKind kind;
    switch (adapterName)
    {
        case "mongoose": kind = J2534AdapterKind.MongoosePlusIsoCan3; break;
        case "tactrix" or "openport": kind = J2534AdapterKind.Tactrix; break;
        default: Console.Error.WriteLine("Choose --adapter mongoose or --adapter tactrix."); return 2;
    }
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("MazdaLink Reader needs Windows and the cable's 32-bit J2534 driver.");
        return 2;
    }

    var drivers = J2534Drivers.Discover(kind);
    if (command == "drivers")
    {
        if (drivers.Count == 0) Console.WriteLine("No matching J2534 drivers are installed.");
        foreach (var item in drivers) Console.WriteLine($"{item.Name}  ({Path.GetFileName(item.LibraryPath)})");
        return 0;
    }
    if (drivers.Count == 0)
    {
        Console.Error.WriteLine(kind == J2534AdapterKind.MongoosePlusIsoCan3
            ? "No Mongoose-Plus ISO/CAN3 driver found. Install the manufacturer's driver with 32-bit J2534-1 04.04 / ISO15765 support, then try again."
            : "No Tactrix Openport driver found. Install the Tactrix drivers, then try again.");
        return 2;
    }
    var matching = driverName.Length == 0 ? drivers : drivers.Where(item =>
        item.Name.Equals(driverName, StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileName(item.LibraryPath).Equals(driverName, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matching.Count != 1)
    {
        Console.Error.WriteLine(matching.Count == 0 ? $"No installed driver matches \"{driverName}\"." : "More than one driver is installed. Choose one with --driver:");
        foreach (var item in drivers) Console.Error.WriteLine($"  {item.Name}  ({Path.GetFileName(item.LibraryPath)})");
        return 2;
    }
    var driver = matching[0];

    string root = Path.GetFullPath(outputFolder.Length > 0 ? outputFolder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MazdaLink"));
    Directory.CreateDirectory(root);
    string build = (Assembly.GetEntryAssembly() ?? typeof(ReadProbe).Assembly).GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    var family = gen2 ? ReadFamily.Gen2 : ReadFamily.Gen1;

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cancellation.Cancel();
        Console.WriteLine();
        Console.WriteLine("Stopping after the current request…");
    };

    Console.WriteLine($"MazdaLink Reader {build.Split('+')[0]} · read-only");
    Console.WriteLine($"Cable driver: {driver.Name}");
    var session = new DiagnosticSession(Path.Combine(root, "logs"), build.Split('+')[0], build);
    int exit = 1;
    IReadTransport? link = null;
    J2534ReadTransport? passThru = null;
    try
    {
        session.Record("reader.started", new { command, adapter = kind.ToString(), family = family.ToString() });
        passThru = new J2534ReadTransport(new NativeJ2534Channel(driver, session), diagnostics: session);
        passThru.Initialize();
        link = new DiagnosticTransport(passThru, session);
        Console.WriteLine($"Connected: {passThru.Adapter}");

        if (command == "scan")
        {
            var scan = ReadScan.Run(link, family, cancellation.Token);
            PrintIdentity(scan.Identity);
            Console.WriteLine($"Read protocol: {scan.Family} · security key accepted");
            Console.WriteLine();
            Console.WriteLine("  Address     Bytes   Result");
            foreach (var attempt in scan.Attempts)
                Console.WriteLine($"  0x{attempt.Address:X6}  {attempt.Count,6}   {attempt.Outcome}");
            Console.WriteLine();
            var refused = scan.Attempts.Where(a => a.Address != 0 && !a.Accepted).ToList();
            string gen2Flag = gen2 ? " --gen2" : "";
            if (scan.LargestBlockAtStart is int largest && refused.Count == 0)
            {
                Console.WriteLine($"SCAN PASSED. The ECU accepts reads of up to {largest} bytes, across the whole 1 MiB.");
                Console.WriteLine($"Turn the ignition off and on again, then run: MazdaLinkReader read{gen2Flag}" + (largest < 1023 ? $" --block {largest}" : ""));
                exit = 0;
            }
            else if (scan.AnyAccepted)
            {
                Console.WriteLine("SCAN PARTLY PASSED. The ECU accepted some reads but refused others:");
                foreach (var attempt in scan.Attempts.Where(a => !a.Accepted))
                    Console.WriteLine($"  0x{attempt.Address:X6} ({attempt.Count} bytes): {attempt.Outcome}");
                Console.WriteLine("The memory layout may differ from 1 MiB. Don't run read yet; please share the log folder below.");
            }
            else
            {
                Console.WriteLine("SCAN FAILED. The ECU unlocked but refused every read size and address tried.");
                Console.WriteLine("It probably needs a different request format. Nothing was changed; please share the log folder below.");
            }
        }
        else if (command == "probe")
        {
            var result = ReadProbe.Run(link, family, cancellation.Token);
            PrintIdentity(result.Identity);
            Console.WriteLine($"Read protocol: {result.Family}{(result.SecurityKeyRequired ? " · security key accepted" : " · no security key needed")}");
            Console.WriteLine("Start of ROM: read OK");
            if (result.Passed)
            {
                Console.WriteLine("End of ROM (1 MiB): read OK");
                Console.WriteLine();
                Console.WriteLine("PROBE PASSED. Turn the ignition off and on again, then run: MazdaLinkReader read" + (gen2 ? " --gen2" : ""));
                exit = 0;
            }
            else
            {
                Console.WriteLine($"End of ROM (1 MiB): rejected ({result.UpperReadError})");
                Console.WriteLine();
                Console.WriteLine("PROBE FAILED. The ECU unlocked but refused to read the end of a 1 MiB ROM.");
                Console.WriteLine("This ECU may have a smaller ROM or a different memory map. Nothing was changed on the ECU.");
                Console.WriteLine("Please share the log folder below so the read can be adapted.");
            }
        }
        else
        {
            var identity = ReadWorkflow.Identify(link);
            PrintIdentity(identity);
            Console.WriteLine("Reading the ROM twice. This takes several minutes; keep the ignition on and don't touch the car.");
            var vault = Path.Combine(root, "backups");
            Directory.CreateDirectory(vault);
            int lastPercent = -1;
            var progress = new ConsoleProgress(value =>
            {
                int percent = (int)Math.Floor(value.Fraction * 100);
                if (percent == lastPercent && value.Stage == BackupReadStage.Reading) return;
                lastPercent = percent;
                Console.Write($"\r{value.Message,-48} {percent,3}%");
            });
            var backup = new ReadWorkflow().ReadBackup(link, identity, vault, progress, cancellation.Token, family, block);
            Console.WriteLine();
            var inspection = backup.Inspection;
            string calibration = string.Concat((inspection.Calibration ?? identity.Calibration ?? "unknown").Where(char.IsLetterOrDigit));
            string copy = Path.Combine(root, $"{calibration}_{DateTime.Now:yyyyMMdd-HHmmss}.bin");
            using (var source = File.OpenRead(backup.Path))
            using (var target = new FileStream(copy, FileMode.CreateNew, FileAccess.Write))
                source.CopyTo(target);
            if (RomInspector.Hash(File.ReadAllBytes(copy)) != inspection.Sha256)
                throw new InvalidDataException("The saved copy does not match the verified backup.");
            Console.WriteLine();
            Console.WriteLine("READ COMPLETE · both reads matched");
            Console.WriteLine($"ROM to edit:      {copy}");
            Console.WriteLine($"Original backup:  {backup.Path} (read-only, keep it safe)");
            Console.WriteLine($"SHA-256:          {inspection.Sha256}");
            if (inspection.ProfileRecognized)
            {
                Console.WriteLine($"Calibration:      {inspection.Calibration} ({inspection.Model}) · bundled definition available");
                Console.WriteLine($"Checksum:         {(inspection.ChecksumValid == true ? "valid" : inspection.ChecksumValid == false ? "INVALID, do not flash this ROM" : "not checked")}");
            }
            else
                Console.WriteLine($"Calibration:      {identity.Calibration ?? "unavailable"} · no bundled definition yet. The ROM is saved; a definition is needed before editing.");
            exit = 0;
        }
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine();
        Console.WriteLine("Stopped. No verified ROM was saved from this run.");
    }
    catch (NegativeResponseException error) when (error.Service == 0x27)
    {
        Console.WriteLine();
        Console.Error.WriteLine($"The ECU refused the security key ({error.Message}).");
        Console.Error.WriteLine("Nothing was changed. Turn the ignition off and wait at least 10 minutes before trying again, or the ECU may lock access.");
        Console.Error.WriteLine(gen2 ? "Try again without --gen2 if this is a Gen 1 ECU." : "If this is a newer ECU, you can try again with --gen2.");
    }
    catch (Exception error) when (error is TransportException or InvalidDataException or IOException)
    {
        Console.WriteLine();
        Console.Error.WriteLine("Stopped: " + error.Message);
        Console.Error.WriteLine("Nothing was written to the ECU.");
    }
    finally
    {
        if (link is not null) link.Dispose();
        else passThru?.Dispose();
        if (passThru?.CloseWarning is { } warning) Console.Error.WriteLine("Cable cleanup: " + warning);
        session.Record("reader.finished", new { command, exit });
        await session.DisposeAsync();
        Console.WriteLine($"Log: {session.Folder}");
    }
    return exit;
}

static string[] AskForCommand()
{
    Console.WriteLine("""
    MazdaLink Reader (read-only)

    Before you start: cable plugged in, ignition ON, engine OFF, lights and fan OFF.

    What would you like to do?
      1  Probe: quick check that this ECU can be read
      2  Scan:  work out which read sizes and addresses this ECU accepts
      3  Read:  read the whole ROM and save it
      4  List the installed cable drivers
    Or type a full command, for example: scan --gen2
    """);
    Console.Write("> ");
    string choice = (Console.ReadLine() ?? "").Trim();
    string? command = choice switch { "1" => "probe", "2" => "scan", "3" => "read", "4" => "drivers", _ => null };
    if (command is null)
        return choice.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var result = new List<string> { command };
    if (command != "drivers")
    {
        Console.Write("Use the Gen 2 read protocol? (yes for the Mazda 2 ZY87 ECU) [y/N] ");
        if ((Console.ReadLine() ?? "").Trim().StartsWith('y'))
            result.Add("--gen2");
    }
    if (command == "read")
    {
        Console.Write("Block size from the scan (press Enter for the default): ");
        string size = (Console.ReadLine() ?? "").Trim();
        if (size.Length > 0)
            result.AddRange(["--block", size]);
    }
    Console.WriteLine();
    return [.. result];
}

static void PrintIdentity(EcuIdentity identity)
{
    Console.WriteLine($"Calibration: {identity.Calibration ?? "unavailable (" + identity.CalibrationStatus + ")"}");
    string? id = identity.VehicleIdentifier;
    string masked = id is null ? "unavailable" : id.Length <= 4 ? new string('*', id.Length) : new string('*', id.Length - 4) + id[^4..];
    Console.WriteLine($"{identity.VehicleIdentifierLabel}: {masked}");
}

sealed class ConsoleProgress(Action<ReadProgress> report) : IProgress<ReadProgress>
{
    public void Report(ReadProgress value) => report(value);
}
