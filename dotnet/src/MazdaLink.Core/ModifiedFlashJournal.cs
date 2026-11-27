using System.Text.Json;

namespace MazdaLink.Core;

/// <summary>Separate schema binding both images, exact block order and live intermediate hashes.
/// Flushes every destructive intent before execution; no ROM payload is logged.</summary>
public sealed class ModifiedFlashJournal:IRestoreJournal
{
    private readonly FileStream stream;
    private readonly string transaction,original,intended;
    private string previous=new('0',64);
    private int sequence,block;
    private bool failed,firstComplete;
    private ModifiedFlashStep? current;
    public ModifiedFlashJournal(string path,ModifiedFlashSequence plan)
    {
        transaction=plan.Id;original=plan.OriginalSha256;intended=plan.IntendedSha256;
        stream=new(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
    }
    public void BindStep(RestoreQualificationPlan plan)
    {
        var step=plan.Modified;
        if(failed||step is null||step.TransactionId!=transaction||step.OriginalSha256!=original||step.IntendedSha256!=intended||
            (block==0?step.Block!=14:block!=14||!firstComplete||step.Block!=15))
        {failed=true;throw new InvalidDataException("Modified journal image, transaction or block-order mismatch.");}
        current=step;block=step.Block;
    }
    public void Commit(string stage,int unit=-1)
    {
        if(failed||current is null||sequence>=4120||!RestoreJournal.Allowed(stage,unit))throw new IOException("Modified journal is unbound, invalid or exhausted.");
        try
        {
            if(stage is "bound" or "erase-intent" && current.BeforeSha256 is null)throw new IOException("The complete live snapshot must be bound before erase intent.");
            byte[] line=JsonSerializer.SerializeToUtf8Bytes(new {Schema=1,Transaction=transaction,Sequence=++sequence,PreviousHash=previous,
                OriginalSha256=original,IntendedSha256=intended,Block=block,Recovery=current.Recovery,
                BeforeSha256=current.BeforeSha256,AfterSha256=current.ExpectedAfterSha256,Stage=stage,Unit=unit});
            stream.Write(line);stream.WriteByte(10);stream.Flush(flushToDisk:true);previous=RomStorage.Hash(line);
            if(stage=="complete"&&block==14)firstComplete=true;
        }
        catch{failed=true;throw;}
    }
    public void Dispose(){failed=true;stream.Dispose();}
}
public interface IModifiedFlashChannel:IOriginalRestoreChannel
{
    RestoreQualificationPlan AdvanceToEb15();
}
