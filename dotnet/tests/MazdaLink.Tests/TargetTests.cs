using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using MazdaLink.Core;
using MazdaLink.J2534;

namespace MazdaLink.Tests;

public sealed class TargetTests
{
    private static byte[] Image()
    {
        byte[] rom = new byte[TargetPolicy.RomSize];
        new Random(87).NextBytes(rom);
        "ZY87EC0W"u8.CopyTo(rom.AsSpan(0xC0000));
        Encoding.ASCII.GetBytes(TargetPolicy.SoftwareId).CopyTo(rom, Zy87Integrity.IdentityOffset);
        for (int i = 0; i < Zy87Integrity.Ranges.Length; i++)
        {
            int entry = Zy87Integrity.TableAddress + 12 * i;
            BinaryPrimitives.WriteUInt32BigEndian(rom.AsSpan(entry), (uint)Zy87Integrity.Ranges[i].Start);
            BinaryPrimitives.WriteUInt32BigEndian(rom.AsSpan(entry + 4), (uint)Zy87Integrity.Ranges[i].End);
        }
        BinaryPrimitives.WriteUInt32BigEndian(rom.AsSpan(Zy87Integrity.TableAddress + 19 * 12), uint.MaxValue);
        BinaryPrimitives.WriteUInt32BigEndian(rom.AsSpan(0xFFFFC), 0x7100);
        return Zy87Integrity.Correct(rom);
    }
    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MazdaLink.Tests", Guid.NewGuid().ToString("N"));
        public Folder() => Directory.CreateDirectory(Path);
        public void Dispose() { foreach (string p in Directory.GetFiles(Path,"*",SearchOption.AllDirectories)) File.SetAttributes(p, FileAttributes.Normal); Directory.Delete(Path, true); }
    }
    private sealed class Peer(byte[] image) : IReadTransport
    {
        public string Identity { get; set; } = TargetPolicy.SoftwareId;
        public List<byte[]> Requests { get; } = [];
        public Action<byte[]>? OnRequest { get; set; }
        public int Rpm { get; set; }
        public bool Mismatch { get; set; }
        public bool Short { get; set; }
        public int IdentityChangesAfter { get; set; } = int.MaxValue;
        private int identities, bytesRead;
        public byte[] Exchange(byte[] r)
        {
            DiagnosticProtocol.CheckReadOnly(r);
            Requests.Add(r.ToArray()); OnRequest?.Invoke(r);
            return r[0] switch
            {
                9 => [0x49, 4, 1, .. Encoding.ASCII.GetBytes(++identities > IdentityChangesAfter ? "L38KEH000" : Identity)],
                1 => [0x41, 0x0C, (byte)(Rpm >> 8), (byte)Rpm],
                0x10 => [0x50, 3, 0, 50, 1, 244],
                0x27 when r[1] == 3 => [0x67, 3, 0x1C, 0x2C, 0x75],
                0x27 => Convert.ToHexString(r) == "2704F9624C" ? [0x67, 4] : throw new TransportException("Wrong security key."),
                0x23 => Read(r),
                _ => throw new TransportException("Unsupported request.")
            };
        }
        private byte[] Read(byte[] r)
        {
            int a = BinaryPrimitives.ReadInt32BigEndian(r.AsSpan(2)), n = BinaryPrimitives.ReadUInt16BigEndian(r.AsSpan(6));
            byte[] reply = [0x63, .. image.AsSpan(a, n).ToArray()];
            if (n == 512) { bytesRead += n; if (Mismatch && bytesRead > TargetPolicy.RomSize) reply[1] ^= 1; if (Short) return reply[..^1]; }
            return reply;
        }
        public void Dispose() { }
    }
    [Theory]
    [InlineData("ZY87EC000")][InlineData("ZY87EC000ZYV9999")][InlineData("L38KEH000")][InlineData("")][InlineData("ZY87EC000ZYV5050X")]
    public void OtherOrIncompleteIdentitiesStopBeforeUnlockOrMemoryRead(string identity)
    {
        using var folder = new Folder(); using var peer = new Peer(Image()) { Identity = identity };
        if (identity.Length == 0) Assert.Throws<TransportException>(() => new ReadWorkflow().Capture(peer, folder.Path));
        else Assert.Throws<InvalidDataException>(() => new ReadWorkflow().Capture(peer, folder.Path));
        Assert.Single(peer.Requests); Assert.Equal(new byte[] { 9,4 }, peer.Requests[0]);
        Assert.Empty(Directory.GetFiles(folder.Path,"*",SearchOption.AllDirectories));
    }
    [Fact]
    public void IdentityChangingBeforeUnlockIsRejected()
    {
        using var folder = new Folder(); using var peer = new Peer(Image()) { IdentityChangesAfter = 1 };
        Assert.Throws<InvalidDataException>(() => new ReadWorkflow().Capture(peer, folder.Path));
        Assert.DoesNotContain(peer.Requests, r => r[0] is 0x10 or 0x27 or 0x23);
    }
    [Fact]
    public void EngineRunningStopsBeforeSession()
    {
        using var folder = new Folder(); using var peer = new Peer(Image()) { Rpm = 4000 };
        Assert.Throws<TransportException>(() => new ReadWorkflow().Capture(peer, folder.Path));
        Assert.DoesNotContain(peer.Requests, r => r[0] is 0x10 or 0x27 or 0x23);
    }
    [Fact]
    public void MemoryIdentityMismatchStopsBeforeFullRead()
    {
        var rom = Image(); rom[Zy87Integrity.IdentityOffset+15] ^= 1;
        using var folder = new Folder(); using var peer = new Peer(rom);
        Assert.Throws<InvalidDataException>(() => new ReadWorkflow().Capture(peer, folder.Path));
        Assert.Single(peer.Requests,r=>r[0]==0x23);
    }
    [Fact]
    public void ReadsBothPassesAt512AndExportsExactBytesWithNoOverwrite()
    {
        var rom = Image(); rom[0xC1000] ^= 1; // Raw backups must retain even invalid checksum bytes.
        using var folder = new Folder(); using var peer = new Peer(rom);
        string original = new ReadWorkflow().Capture(peer, folder.Path);
        Assert.Equal(rom, RomStorage.LoadBackup(original));
        var reads = peer.Requests.Where(r=>r[0]==0x23).ToArray(); Assert.Equal(4097, reads.Length);
        Assert.All(reads.Skip(1), r=>Assert.Equal(512, BinaryPrimitives.ReadUInt16BigEndian(r.AsSpan(6))));
        Assert.Equal("2324000FFE000200", Convert.ToHexString(reads[^1]));
        Assert.Single(peer.Requests,r=>r[0]==0x27&&r[1]==4);
        string export=System.IO.Path.Combine(folder.Path,"export.rom"); RomStorage.Export(original,export);
        Assert.Equal(rom,File.ReadAllBytes(export)); Assert.Throws<IOException>(()=>RomStorage.Export(original,export));
        Assert.Equal(rom,File.ReadAllBytes(export));
    }
    [Theory][InlineData("mismatch")][InlineData("short")][InlineData("cancel")]
    public void FailedReadsNeverSealAVerifiedBackup(string fault)
    {
        using var folder = new Folder(); using var peer = new Peer(Image()) { Mismatch=fault=="mismatch", Short=fault=="short" };
        using var cancellation=new CancellationTokenSource();
        Assert.ThrowsAny<Exception>(()=>new ReadWorkflow().Capture(peer,folder.Path,(_,_)=>{if(fault=="cancel")cancellation.Cancel();},cancellation.Token));
        Assert.Empty(Directory.GetFiles(folder.Path,"backup.json",SearchOption.AllDirectories));
    }
    [Theory][InlineData("pass1.read")][InlineData("pass2.read")][InlineData("original.rom")][InlineData("journal.json")]
    public void TamperedBackupsCannotBeExported(string name)
    {
        using var folder=new Folder(); using var peer=new Peer(Image()); string original=new ReadWorkflow().Capture(peer,folder.Path);
        string changed=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(original)!,name); File.SetAttributes(changed,FileAttributes.Normal);File.WriteAllBytes(changed,[]);
        Assert.ThrowsAny<Exception>(()=>RomStorage.Export(original,System.IO.Path.Combine(folder.Path,"bad.rom")));
        Assert.False(File.Exists(System.IO.Path.Combine(folder.Path,"bad.rom")));
    }
    [Theory][InlineData(0x10)][InlineData(0x50000)][InlineData(0xFF740)][InlineData(0xC009F)]
    public void PreparationRejectsChangesOutsideCalibrationOrToSoftwareIdentity(int address)
    {
        using var folder=new Folder(); using var peer=new Peer(Image()); string original=new ReadWorkflow().Capture(peer,folder.Path);
        byte[] edited=File.ReadAllBytes(original);edited[address]^=1;string path=System.IO.Path.Combine(folder.Path,"edit.rom");File.WriteAllBytes(path,edited);
        Assert.Throws<InvalidDataException>(()=>RomEditor.SaveNew(original,path,System.IO.Path.Combine(folder.Path,"prepared.rom")));
    }
    [Fact]
    public void EveryPrepareCorrectsAllRangesVerifiesDiskAndPreservesInputs()
    {
        using var folder=new Folder(); using var peer=new Peer(Image()); string original=new ReadWorkflow().Capture(peer,folder.Path);
        byte[] edited=File.ReadAllBytes(original);edited[0xC1000]^=1;
        // Corrupt multiple correction words; export must recalculate every established word.
        edited[Zy87Integrity.CorrectionAddress(0)]^=1;edited[Zy87Integrity.CorrectionAddress(18)]^=1;
        string input=System.IO.Path.Combine(folder.Path,"edit.rom"), output=System.IO.Path.Combine(folder.Path,"prepared.rom");File.WriteAllBytes(input,edited);
        RomEditor.SaveNew(original,input,output);
        byte[] prepared=File.ReadAllBytes(output);Assert.All(Zy87Integrity.Check(prepared), x=>Assert.True(x.Valid));
        foreach(var range in Zy87Integrity.Check(prepared))
        {
            uint sum=range.Stored; for(int p=range.Start;p<range.End;p+=4)sum=unchecked(sum+BinaryPrimitives.ReadUInt32BigEndian(prepared.AsSpan(p)));
            Assert.Equal(0x5AA5A55Au,sum);
        }
        Assert.Equal(edited,File.ReadAllBytes(input));Assert.Equal(Image(),RomStorage.LoadBackup(original));
        Assert.Throws<IOException>(()=>RomEditor.SaveNew(original,input,output));
        RomEditor.SaveNew(original,original,System.IO.Path.Combine(folder.Path,"unedited.rom"));
        Assert.Equal(Image(),File.ReadAllBytes(System.IO.Path.Combine(folder.Path,"unedited.rom")));
    }
    [Theory][InlineData("1002")][InlineData("1087")][InlineData("2701")][InlineData("3101FF00")][InlineData("3400440000000000100000")][InlineData("3601AA")][InlineData("1101")][InlineData("2324000000000201")][InlineData("23000000000010")][InlineData("220001")]
    public void StrictWireGateBlocksProgrammingAndOtherProtocols(string hex)=>Assert.Throws<TransportException>(()=>DiagnosticProtocol.CheckReadOnly(Convert.FromHexString(hex)));
    [Fact]
    public void MenuAndCommandsCannotOpenHardwareForFlashingOrOverrides()
    {
        using var folder=new Folder();int opens=0;var output=new StringWriter();
        var terminal=new Terminal(()=>{opens++;throw new Exception("Must not connect");},new StringReader("6\n7\n0\n"),output,folder.Path);
        Assert.Equal(0,terminal.Run([]));Assert.Equal(0,opens);Assert.Contains("Flashing is unavailable",output.ToString());
        Assert.Equal(1,terminal.Run(["read","--gen2"]));Assert.Equal(1,terminal.Run(["flash","anything.rom"]));Assert.Equal(0,opens);
        Assert.DoesNotContain("SpeedLink",Terminal.Help);Assert.Contains(TargetPolicy.SoftwareId,Terminal.Help);
        Assert.DoesNotContain(typeof(ReadWorkflow).Assembly.GetReferencedAssemblies(),a=>a.Name!.StartsWith("SpeedLink"));
    }
    [Fact]
    public void CancellationDuringSessionPreventsTheNextSecurityRequest()
    {
        using var folder=new Folder();using var cancel=new CancellationTokenSource();using var peer=new Peer(Image());
        peer.OnRequest=r=>{if(r[0]==0x10)cancel.Cancel();};
        Assert.Throws<OperationCanceledException>(()=>new ReadWorkflow().Capture(peer,folder.Path,cancel:cancel.Token));
        Assert.DoesNotContain(peer.Requests,r=>r[0] is 0x27 or 0x23);
    }
    [Fact]
    public void OnlyRegisteredMongooseIsoCanDriversMatch()
    {
        Assert.True(J2534Drivers.AcceptsRegistration(J2534AdapterKind.MongoosePlusIsoCan3,"Mongoose-Plus ISO/CAN3","DrewTech",true));
        Assert.False(J2534Drivers.AcceptsRegistration(J2534AdapterKind.MongoosePlusIsoCan3,"Openport 2.0","Tactrix",true));
        Assert.False(J2534Drivers.AcceptsRegistration(J2534AdapterKind.MongoosePlusIsoCan3,"Mongoose-Plus ISO/CAN3","DrewTech",false));
        Assert.DoesNotContain(typeof(J2534ReadTransport).Assembly.GetReferencedAssemblies(),a=>a.Name!.StartsWith("SpeedLink"));
    }
    private sealed class Channel(string identity):IJ2534Channel
    {
        public string? CleanupWarning=>null; public int Writes; private bool read;
        public J2534Version Open()=>new("test","test","04.04"); public void Connect(uint b,uint q,uint r){} public void ClearReceive(){read=false;}
        public void Write(byte[] r){Writes++;} public J2534Frame? Read(int timeout){if(read)return null;read=true;return new(0x7E8,0,[0x49,4,1,..Encoding.ASCII.GetBytes(identity)]);} public void Dispose(){}
    }
    [Fact]
    public void TransportRequiresExactIdentityBeforeSecurityEvenForDirectCalls()
    {
        var channel=new Channel("L38KEH000");using var link=new J2534ReadTransport(channel);link.Initialize();
        Assert.Throws<TransportException>(()=>link.Exchange([0x27,3]));Assert.Equal(0,channel.Writes);
        Assert.Throws<InvalidDataException>(()=>link.Exchange([9,4]));Assert.Equal(1,channel.Writes);
        Assert.Throws<TransportException>(()=>link.Exchange([0x10,3]));Assert.Equal(1,channel.Writes);
    }
}
