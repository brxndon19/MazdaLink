using System.Text.Json;

namespace MazdaLink.Core;

/// <summary>Offline backend contract. The separate native original-only qualification uses its own fixed wire script and gate.</summary>
public interface IOriginalRestoreDevice
{
    void ColdBind(string preservedBootSha256);
    byte[] ReadImage();
    void EraseEb14();
    void ProgramNextOriginalUnit(int index, byte[] data);
    byte[] ReadUnit(int index);
}

public sealed record RestoreJournalEntry(int Schema, string Session, int Sequence, string PreviousHash,
    string OriginalSha256, string Stage, int Unit);

public interface IRestoreJournal : IDisposable
{
    void Commit(string stage, int unit = -1);
}

/// <summary>Create-new, bounded, flushed intent log. It contains no ROM bytes or identity secrets.</summary>
public sealed class RestoreJournal : IRestoreJournal
{
    private readonly FileStream stream;
    private readonly string originalHash, session = Guid.NewGuid().ToString("N");
    private string previous = new('0',64);
    private int sequence;
    private bool failed;
    public RestoreJournal(string path, OriginalBlockRestorePlan plan)
    {
        originalHash = plan.OriginalSha256;
        stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    }
    public string OriginalSha256=>originalHash;
    public void Commit(string stage, int unit = -1)
    {
        if (failed || sequence >= 2060 || !Allowed(stage,unit))
            throw new IOException("Restore journal is closed, exhausted or invalid.");
        try
        {
            var entry = new RestoreJournalEntry(1,session,++sequence,previous,originalHash,stage,unit);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(entry);
            stream.Write(bytes);stream.WriteByte(10);stream.Flush(flushToDisk:true);
            previous = RomStorage.Hash(bytes);
        }
        catch { failed=true;throw; }
    }
    internal static bool Allowed(string stage,int unit) =>
        stage is "unit-intent" or "unit-verified" ? unit is >=0 and <1024 :
        unit==-1 && stage is "started" or "bound" or "erase-intent" or "erase-verified" or "full-readback-1" or "complete";
    public void Dispose() { failed=true;stream.Dispose(); }
}

/// <summary>One-shot unchanged-original backend. A new instance always binds and re-erases;
/// it cannot resume a program index or infer success from an interrupted journal.</summary>
public sealed class OriginalRestoreTransaction(OriginalBlockRestorePlan plan, IOriginalRestoreDevice device, IRestoreJournal journal)
{
    private bool used;
    public void Run(CancellationToken cancel=default)
    {
        if (used) throw new InvalidOperationException("A restore session is single-use. Fresh binding and full erase are required.");
        used=true;
        void Record(string stage,int unit=-1) { cancel.ThrowIfCancellationRequested();journal.Commit(stage,unit);cancel.ThrowIfCancellationRequested(); }
        Record("started");
        device.ColdBind(plan.PreservedBootSha256);
        plan.RequirePreservedReadback(device.ReadImage());
        Record("bound");
        Record("erase-intent"); // Durable before the first destructive request.
        device.EraseEb14();
        byte[] blank=device.ReadImage();plan.RequirePreservedReadback(blank);
        plan.RequireErasedBlock(blank.AsSpan(OriginalBlockRestorePlan.Start,OriginalBlockRestorePlan.Length).ToArray());
        Record("erase-verified");
        for(int unit=0;unit<OriginalBlockRestorePlan.Units;unit++)
        {
            byte[] data=plan.Data(unit);
            Record("unit-intent",unit);
            device.ProgramNextOriginalUnit(unit,data);
            if(!plan.Data(unit).AsSpan().SequenceEqual(device.ReadUnit(unit)))
                throw new InvalidDataException("Original restore unit readback failed. No command was resent.");
            Record("unit-verified",unit);
        }
        plan.RequireFullReadback(device.ReadImage());Record("full-readback-1");
        plan.RequireFullReadback(device.ReadImage());Record("complete");
    }
}
