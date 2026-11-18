using MazdaLink.Core;
using MazdaLink.J2534;
using System.Reflection;

string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MazdaLink");
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
try
{
    if (args.SequenceEqual(new[] { "license" }))
    {
        foreach (string resource in new[] { "MazdaLink.ATTRIBUTIONS", "MazdaLink.LICENSE", "MazdaLink.Runtime.LICENSE", "MazdaLink.Runtime.NOTICES" })
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream); Console.WriteLine(reader.ReadToEnd());
        }
        return 0;
    }
    string selectedDriver = "";
    J2534Driver? connectionDriver = null;
    // Only an installed registered Mongoose driver can be selected, never an arbitrary DLL.
    if (args.Length >= 2 && args[^2] == "--driver") { selectedDriver = args[^1]; args = args[..^2]; }
    J2534Driver SelectDriver()
    {
        if (connectionDriver is not null) return connectionDriver;
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4) throw new TransportException("Use MazdaLink.exe on Windows with the manufacturer's 32-bit J2534 04.04 driver.");
        var drivers = J2534Drivers.Discover().Where(d => selectedDriver.Length == 0 || d.Name.Equals(selectedDriver, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (drivers.Length == 0) throw new TransportException("No matching registered Mongoose-Plus ISO/CAN3 32-bit J2534 driver. Install the manufacturer's driver. Use drivers to list registrations.");
        J2534Driver driver;
        if (drivers.Length == 1) driver = drivers[0];
        else if (args.Length != 0) throw new TransportException("Multiple drivers are installed. Use --driver followed by the exact registered name.");
        else
        {
            for (int i = 0; i < drivers.Length; i++) Console.WriteLine($"{i + 1}  {drivers[i].Name}");
            Console.Write("Choose driver number: ");
            if (!int.TryParse(Console.ReadLine(), out int choice) || choice < 1 || choice > drivers.Length) throw new TransportException("Driver selection cancelled.");
            driver = drivers[choice - 1];
        }
        return connectionDriver = driver;
    }
    IReadTransport Connect()
    {
        var transport = new J2534ReadTransport(new NativeJ2534Channel(SelectDriver()));
        try { transport.Initialize(); return transport; } catch { transport.Dispose(); throw; }
    }
    if (args.SequenceEqual(new[] { "drivers" }))
    {
        var drivers = J2534Drivers.Discover();
        if (drivers.Count == 0) Console.WriteLine("No registered Mongoose-Plus ISO/CAN3 32-bit J2534 drivers found.");
        foreach (var driver in drivers) Console.WriteLine(driver.Name);
        return 0;
    }
    Console.WriteLine($"MazdaLink {Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]}");
    return new Terminal(Connect, Console.In, Console.Out, root,
        () => new NativeBenchMonitorChannel(SelectDriver()),
        () => new NativeRamSelfTestChannel(SelectDriver())).Run(args, cancel.Token);
}
catch (Exception error)
{
    // Unexpected failures end the process. No memory dumps, firmware, seeds/keys or stack traces are recorded.
    Console.Error.WriteLine("MazdaLink stopped after an unexpected error: " + error.GetType().Name);
    try
    {
        string folder = Path.Combine(root, "errors"); Directory.CreateDirectory(folder);
        foreach (string file in Directory.GetFiles(folder, "*.txt").OrderByDescending(File.GetLastWriteTimeUtc).Skip(4)) File.Delete(file);
        File.WriteAllText(Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-ffff") + ".txt"), DateTime.UtcNow.ToString("O") + "\n" + error.GetType().Name + "\nNo memory or ROM payload captured.\n");
    }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
    return 1;
}
