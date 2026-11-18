namespace MazdaLink.Core;

public static class BenchColdStart
{
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
        if (wait is not null) wait(60000, cancel);
        else BenchEntryWindow.Wait(TimeSpan.FromSeconds(60), Thread.Sleep, cancel);
        cancel.ThrowIfCancellationRequested();
        record("cold-power-off-host-wait-completed");
        output.WriteLine("Keep the ECU OFF while the adapter opens.\n" +
            "Type START, then reconnect both ECU positive feeds promptly when ENTRY WINDOW appears.\n" +
            "Reconnect within that 20-second window and leave the ECU powered:");
        if (input.ReadLine() != "START") throw new InvalidDataException("Entry window cancelled before bench traffic.");
        cancel.ThrowIfCancellationRequested();
    }
}
