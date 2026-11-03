using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SpeedLink.Core;

public sealed record LoggerReading(string Channel, double? Value, DateTimeOffset? Timestamp, string Status);
public sealed record LoggerExchange(string Query, DateTimeOffset Timestamp, double DurationMs, string? ResponseHex, string? Error, string? RequestHex = null);
public sealed record LoggerFrame(long Scan, DateTimeOffset Timestamp, TimeSpan Elapsed,
    IReadOnlyDictionary<string, LoggerReading> Readings, IReadOnlyList<LoggerExchange> Exchanges);

/// <summary>Sequential stock-ROM identifier polling. No security access, session switch, writes or request retries.</summary>
public sealed class LiveLogger
{
    private readonly LoggerDefinition definition;
    private readonly LoggerCalibration calibration;
    private readonly IReadTransport transport;
    private readonly LoggerChannel[] selected, needed;
    private sealed record PollRequest(string Key, byte[] Request, LoggerChannel[][] Groups, int ResponseLength);
    private readonly PollRequest[] directPlan, groupedPlan;
    private bool standardBatchesUnavailable;
    private PollRequest[] Plan => standardBatchesUnavailable ? directPlan : groupedPlan;
    private readonly Dictionary<string, string> rejected = new(StringComparer.Ordinal);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DateTimeOffset origin = DateTimeOffset.UtcNow;
    private long scan;
    public static readonly TimeSpan MaximumSampleAge = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaximumInputSkew = TimeSpan.FromMilliseconds(500);
    public int QueryCount => Plan.Length;
    public int UngroupedQueryCount => directPlan.Length;
    public LoggerFrame? LastFrame { get; private set; }
    public int ActiveQueryCount => Plan.Count(group => !rejected.ContainsKey(group.Key));
    private DateTimeOffset Now => origin + clock.Elapsed; // UTC anchor plus monotonic elapsed time.
    public DateTimeOffset CurrentTime => Now;

    public LiveLogger(LoggerDefinition definition, LoggerCalibration calibration, IReadTransport transport,
        EcuIdentity identity, IEnumerable<string> selection)
    {
        if (transport.ReadAttempted) throw new TransportException("Reconnect before logging after a ROM read.");
        if (LoggerDefinition.ForCalibration(identity.Calibration) is null || !definition.MatchesCalibrationId(identity.Calibration))
            throw new TransportException("Live ECU calibration does not match the selected logger profile. ROM reading is still available.");
        this.definition = definition; this.calibration = calibration; this.transport = transport;
        var all = definition.Channels.ToDictionary(c => c.Id, StringComparer.Ordinal);
        string[] ids = selection.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0 || ids.Any(id => !all.ContainsKey(id))) throw new ArgumentException("Select known logger parameters.");
        selected = ids.Select(id => all[id]).ToArray();
        var dependencies = new HashSet<string>(StringComparer.Ordinal);
        void Include(string id) { if (!dependencies.Add(id)) return; foreach (string input in all[id].Inputs) Include(input); }
        foreach (string id in ids) Include(id);
        needed = all.Values.Where(c => dependencies.Contains(c.Id)).ToArray();
        // Place each calculated channel's inputs together before unrelated reads.
        var order = new List<string>();
        void Schedule(LoggerChannel channel)
        {
            foreach (string input in channel.Inputs) Schedule(all[input]);
            if (channel.Query is not null && !order.Contains(channel.Query, StringComparer.Ordinal)) order.Add(channel.Query);
        }
        foreach (var channel in selected.Where(c => c.Query is null)) Schedule(channel);
        foreach (var channel in selected) Schedule(channel);
        directPlan = order.Select(query => needed.Where(c => c.Query == query).ToArray())
            .Select(group => new PollRequest(group[0].Query!, definition.RequestFor(group[0].Id), [group], definition.ResponseLengthFor(group[0].Id))).ToArray();
        var aliases = Mode01LoggerProtocol.HasVerifiedAliases(definition) ? directPlan.Where(p => Mode01LoggerProtocol.IsAlias(p.Request, p.ResponseLength)).ToArray() : [];
        if (aliases.Length >= 2)
        {
            byte[] batch = [1, .. aliases.Select(p => p.Request[2])];
            var combined = new PollRequest("01/" + Convert.ToHexString(batch.AsSpan(1)), batch, aliases.SelectMany(p => p.Groups).ToArray(), Mode01LoggerProtocol.ResponseLength(batch));
            groupedPlan = directPlan.SelectMany(p => ReferenceEquals(p, aliases[0]) ? new[] { combined } : aliases.Contains(p) ? [] : new[] { p }).ToArray();
        }
        else groupedPlan = directPlan;
    }

    public LoggerFrame ReadFrame(CancellationToken cancellation = default, Action<LoggerFrame>? progress = null)
    {
        cancellation.ThrowIfCancellationRequested();
        long number = ++scan;
        LastFrame = null;
        var readings = needed.ToDictionary(c => c.Id, c => new LoggerReading(c.Id, null, null, "Waiting"), StringComparer.Ordinal);
        var samples = new Dictionary<string, LoggerSample>(StringComparer.Ordinal);
        var exchanges = new List<LoggerExchange>();
        LoggerFrame Snapshot()
        {
            // Capture once: preemption between separate clock reads must not make
            // the recorded UTC and elapsed clocks describe different instants.
            var elapsed = clock.Elapsed;
            var now = origin + elapsed;
            var result = new Dictionary<string, LoggerReading>(StringComparer.Ordinal);
            foreach (var channel in selected)
            {
                var reading = readings[channel.Id];
                if (reading.Timestamp is not null && now - reading.Timestamp > MaximumSampleAge)
                    reading = reading with { Value = null, Status = "Stale" };
                result.Add(channel.Id, reading);
            }
            return LastFrame = new(number, now, elapsed, new ReadOnlyDictionary<string, LoggerReading>(result), exchanges.ToArray());
        }
        foreach (var poll in Plan)
        {
            cancellation.ThrowIfCancellationRequested();
            string query = poll.Key;
            var allChannels = poll.Groups.SelectMany(g => g).ToArray();
            if (rejected.TryGetValue(query, out string? prior))
            {
                foreach (var channel in allChannels) readings[channel.Id] = new(channel.Id, null, null, prior);
                continue;
            }
            var started = Now;
            var timer = Stopwatch.StartNew();
            string? capturedResponse = null;
            try
            {
                byte[] response = transport.ExchangeLogger(poll.Request, poll.ResponseLength);
                capturedResponse = Convert.ToHexString(response);
                var timestamp = Now;
                var standard = poll.Request[0] == 1 ? Mode01LoggerProtocol.Split(poll.Request, response) : null;
                foreach (var group in poll.Groups)
                {
                    byte[] own = standard is not null ? [0x62, 0, definition.RequestFor(group[0].Id)[2], .. standard[definition.RequestFor(group[0].Id)[2]]] : response;
                    foreach (var channel in group)
                    {
                        double value = definition.Decode(channel.Id, own, calibration);
                        readings[channel.Id] = new(channel.Id, value, timestamp, "OK");
                        samples[channel.Id] = new(value, timestamp);
                    }
                }
                exchanges.Add(new(query, started, timer.Elapsed.TotalMilliseconds, Convert.ToHexString(response), null, Convert.ToHexString(poll.Request)));
            }
            catch (NegativeResponseException error)
            {
                string status = $"Unavailable · NRC {error.Code:X2}";
                if (poll.Request[0] == 1 && error.Code is 0x11 or 0x12 or 0x31)
                {
                    standardBatchesUnavailable = true;
                    transport.Diagnostics?.Record("logger.standard_batch_unavailable", new { code = $"{error.Code:X2}", individualQueriesFromNextScan = true });
                }
                else if (error.Code is 0x11 or 0x12 or 0x31) rejected[query] = status;
                foreach (var channel in allChannels) readings[channel.Id] = new(channel.Id, null, null, status);
                exchanges.Add(new(query, started, timer.Elapsed.TotalMilliseconds, null, $"7F{poll.Request[0]:X2}{error.Code:X2}", Convert.ToHexString(poll.Request)));
            }
            catch (Exception error)
            {
                foreach (var channel in allChannels) readings[channel.Id] = new(channel.Id, null, null, "Connection stopped");
                exchanges.Add(new(query, started, timer.Elapsed.TotalMilliseconds, capturedResponse, error.Message, Convert.ToHexString(poll.Request)));
                Snapshot();
                throw;
            }
            // A malformed response or transport error ends the session. Never retry an uncertain exchange.
            foreach (var channel in needed.Where(c => c.Query is null))
            {
                try
                {
                    double value = definition.Calculate(channel.Id, samples, Now, MaximumSampleAge, MaximumInputSkew);
                    var timestamp = channel.Inputs.Max(id => samples[id].Timestamp);
                    readings[channel.Id] = new(channel.Id, value, timestamp, "OK");
                    samples[channel.Id] = new(value, timestamp);
                }
                catch (TransportException) { readings[channel.Id] = new(channel.Id, null, null, "Inputs unavailable or too far apart"); }
            }
            var partial = Snapshot();
            progress?.Invoke(partial);
        }
        cancellation.ThrowIfCancellationRequested();
        return Snapshot();
    }
}

/// <summary>A new directory per recording. Wide CSV with per-channel times and statuses, raw replies and a session manifest.</summary>
public sealed class LoggerRecording : IDisposable
{
    private readonly StreamWriter csv, wire;
    private readonly LoggerDefinition definition;
    private readonly string[] selection;
    private readonly IReadOnlyDictionary<string, string> units;
    private readonly IReadOnlyDictionary<string, double>? displayParameters;
    private readonly string manifestPath;
    private readonly Dictionary<string, object?> metadata;
    private bool finished;
    public string Folder { get; }
    public long Rows { get; private set; }
    public LoggerRecording(string parent, LoggerDefinition definition, LoggerCalibration calibration, EcuIdentity identity,
        IEnumerable<string> selection, IReadOnlyDictionary<string, string> units, IReadOnlyDictionary<string, double>? displayParameters,
        string appVersion, double requestedScansPerSecond)
    {
        this.definition = definition; this.selection = selection.ToArray(); this.units = new Dictionary<string, string>(units);
        this.displayParameters = displayParameters is null ? null : new Dictionary<string, double>(displayParameters);
        var channels = definition.Channels.ToDictionary(c => c.Id);
        if (this.selection.Length == 0 || this.selection.Distinct().Count() != this.selection.Length) throw new ArgumentException("Invalid recording selection.");
        foreach (string id in this.selection) definition.ConvertDisplay(id, 0, units[id], this.displayParameters);
        Folder = Path.Combine(parent, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        manifestPath = Path.Combine(Folder, "session.json");
        metadata = new()
        {
            ["formatVersion"] = 1, ["appVersion"] = appVersion, ["definition"] = definition.Id,
            ["definitionVersion"] = definition.DefinitionVersion, ["identity"] = identity.ToManifest(),
            ["calibrationRomSha256"] = calibration.SourceRomSha256, ["mapCalibration"] = calibration.Values,
            ["displayParameters"] = this.displayParameters, ["units"] = this.units, ["selection"] = this.selection,
            ["startedUtc"] = DateTimeOffset.UtcNow, ["status"] = "Recording or interrupted",
            ["requestedScansPerSecond"] = requestedScansPerSecond, ["pollingMode"] = requestedScansPerSecond == 0 ? "maximum" : "limited", ["ratesAreMeasuredAtRuntime"] = true,
            ["maximumAgeMs"] = LiveLogger.MaximumSampleAge.TotalMilliseconds,
            ["maximumCalculatedInputSkewMs"] = LiveLogger.MaximumInputSkew.TotalMilliseconds,
            ["atomicEcuSnapshot"] = false, ["channelValidation"] = "ROM/offline checked; real-ECU validation pending"
        };
        using (var manifest = NewWriter(manifestPath)) manifest.Write(JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        StreamWriter? output = null, raw = null;
        try
        {
            output = NewWriter(Path.Combine(Folder, "log.csv")); raw = NewWriter(Path.Combine(Folder, "responses.jsonl"));
            var header = new List<string> { "Timestamp UTC", "Elapsed (s)", "Scan" };
            foreach (string id in this.selection) header.AddRange([$"{channels[id].Name} ({units[id]})", id + " sample UTC", id + " status"]);
            output.WriteLine(string.Join(',', header.Select(Escape))); output.Flush(); csv = output; wire = raw;
        }
        catch { output?.Dispose(); raw?.Dispose(); throw; }
    }
    private static StreamWriter NewWriter(string path) => new(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
    private static string Escape(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    public void Append(LoggerFrame frame)
    {
        ObjectDisposedException.ThrowIf(finished, this);
        var row = new List<string> { frame.Timestamp.ToString("O", CultureInfo.InvariantCulture), frame.Elapsed.TotalSeconds.ToString("R", CultureInfo.InvariantCulture), frame.Scan.ToString(CultureInfo.InvariantCulture) };
        foreach (string id in selection)
        {
            var reading = frame.Readings[id];
            row.Add(reading.Value is double value ? definition.ConvertDisplay(id, value, units[id], displayParameters).ToString("R", CultureInfo.InvariantCulture) : "");
            row.Add(reading.Timestamp?.ToString("O", CultureInfo.InvariantCulture) ?? ""); row.Add(reading.Status);
        }
        foreach (var exchange in frame.Exchanges) wire.WriteLine(JsonSerializer.Serialize(new { frame.Scan, exchange.Query, exchange.Timestamp, exchange.DurationMs, requestHex = exchange.RequestHex ?? "22" + exchange.Query, exchange.ResponseHex, exchange.Error }));
        wire.Flush(); csv.WriteLine(string.Join(',', row.Select(Escape))); csv.Flush(); Rows++;
    }
    public void Complete(string status)
    {
        if (finished) return;
        finished = true;
        try { csv.Flush(); wire.Flush(); }
        finally { try { csv.Dispose(); } finally { wire.Dispose(); } }
        metadata["status"] = status; metadata["endedUtc"] = DateTimeOffset.UtcNow; metadata["rows"] = Rows;
        string pending = manifestPath + ".pending";
        File.WriteAllText(pending, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(pending, manifestPath, true);
    }
    public void Dispose()
    {
        if (finished) return;
        try { Complete("Stopped or interrupted"); }
        finally { csv.Dispose(); wire.Dispose(); finished = true; }
    }
}
