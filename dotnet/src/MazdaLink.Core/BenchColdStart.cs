namespace MazdaLink.Core;

public static class BenchColdStart
{
    private static void Wait(int milliseconds, CancellationToken cancel, Action<int, CancellationToken>? wait)
    {
        cancel.ThrowIfCancellationRequested();
        if (wait is not null) wait(milliseconds, cancel);
        else BenchEntryWindow.Wait(TimeSpan.FromMilliseconds(milliseconds), Thread.Sleep, cancel);
        cancel.ThrowIfCancellationRequested();
    }
    public static void Prepare(TextReader input, TextWriter output, Action<string> record,
        CancellationToken cancel = default, Action<int, CancellationToken>? wait = null)
    {
        output.WriteLine("Disconnect ONLY both spare ECU positive supply feeds now.\n" +
            "Keep Mongoose OBD 12 V, USB, grounds and CAN connected.\n" +
            "Type POWER OFF once the ECU feeds are disconnected:");
        if (input.ReadLine() != "POWER OFF") throw new InvalidDataException("ECU power-off setup cancelled before bench traffic.");
        cancel.ThrowIfCancellationRequested();
        record("operator-confirmed-ecu-power-off-cold-wait-started");
        output.WriteLine("Waiting 60 seconds BEFORE the entry timer. Keep the ECU feeds disconnected.\n" +
            "This follows the successful spare test; the host timer does not measure internal ECU voltage.");
        Wait(60000, cancel, wait);
        record("cold-power-off-host-wait-completed");
        output.WriteLine("Keep the ECU OFF while the adapter opens.\n" +
            "Type START, then reconnect both ECU positive feeds promptly when ENTRY WINDOW appears.\n" +
            "Reconnect within that 20-second window and leave the ECU powered:");
        if (input.ReadLine() != "START") throw new InvalidDataException("Entry window cancelled before bench traffic.");
        cancel.ThrowIfCancellationRequested();
    }
    public static void PrepareNormalRestart(TextReader input, TextWriter output, Action<string> record,
        CancellationToken cancel = default, Action<int, CancellationToken>? wait = null)
    {
        output.WriteLine("NORMAL RESTART: disconnect ONLY both ECU positive feeds now.\n" +
            "Keep Mongoose OBD 12 V, USB, grounds and CAN connected.\n" +
            "Type POWER OFF once the ECU feeds are disconnected:");
        if (input.ReadLine() != "POWER OFF") throw new InvalidDataException("Normal restart power-off was not confirmed.");
        record("normal-restart-operator-power-off-confirmed");
        output.WriteLine("Waiting 60 seconds with ECU OFF. No monitor-entry traffic is sent during restart.");
        Wait(60000, cancel, wait);
        record("normal-restart-cold-host-wait-completed");
        output.WriteLine("Reconnect BOTH ECU positive feeds and leave them on.\nType POWER ON after reconnecting:");
        if (input.ReadLine() != "POWER ON") throw new InvalidDataException("Normal restart power-on was not confirmed.");
        record("normal-restart-operator-power-on-confirmed");
        output.WriteLine("Waiting 10 seconds for normal startup before the single diagnostic check...");
        Wait(10000, cancel, wait);
        record("normal-restart-startup-host-wait-completed");
    }
}
