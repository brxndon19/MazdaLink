using SpeedLink.Core;

namespace SpeedLink.Tests;

public class ReadProgressTests
{
    private sealed class Capture(Action<ReadProgress>? observe = null) : IProgress<ReadProgress>
    {
        public List<ReadProgress> Events { get; } = [];
        public void Report(ReadProgress value) { Events.Add(value); observe?.Invoke(value); }
    }

    [Fact]
    public void test_progress_tracks_both_passes_and_completes_after_sealing()
    {
        using var temp = new TempFolder();
        using var peer = new TestEcu(Fixture.Rom);
        var store = Fixture.Store();
        var progress = new Capture(value =>
        {
            if (value.Stage == BackupReadStage.Complete)
            {
                var entry = Assert.Single(store.Scan(temp.Path));
                Assert.True(entry.Complete);
                Assert.Equal(Fixture.Rom, store.Load(Path.Combine(Path.GetDirectoryName(entry.Path)!, "rom.bin")).Data);
            }
        });
        Fixture.Reader().ReadBackup(peer, ReadWorkflow.Identify(peer), temp.Path, progress);
        Assert.Equal(BackupReadStage.Preparing, progress.Events[0].Stage);
        Assert.Equal(BackupReadStage.Verifying, progress.Events[^2].Stage);
        Assert.Equal(BackupReadStage.Complete, progress.Events[^1].Stage);
        Assert.Single(progress.Events, p => p.Stage == BackupReadStage.Complete);
        Assert.True(progress.Events.Zip(progress.Events.Skip(1)).All(pair => pair.First.Fraction <= pair.Second.Fraction));
        foreach (int pass in new[] { 1, 2 })
        {
            var events = progress.Events.Where(p => p.Stage == BackupReadStage.Reading && p.Pass == pass).ToArray();
            Assert.Equal(0, events[0].BytesInPass);
            Assert.Equal(RomInspector.RomSize, events[^1].BytesInPass);
            Assert.Equal(pass / 2.0, events[^1].Fraction);
            Assert.Equal(265, events.Count(p => p.BytesInPass > 0));
        }
        Assert.Equal(530, peer.Requests.Count(r => r[0] == 0x23));
    }

    [Fact]
    public void test_cancelled_inflight_block_retains_latest_durable_progress_before_display_delivery()
    {
        using var temp = new TempFolder();
        using var cancel = new CancellationTokenSource();
        int reads = 0;
        using var peer = new TestEcu(Fixture.Rom) { ModifyResponse = (request, response) =>
        {
            if (request[0] == 0x23 && ++reads == 2) cancel.Cancel();
            return response;
        }};
        var queued = new Capture();
        var progress = new ReadProgressCapture(queued);
        Assert.Throws<OperationCanceledException>(() => Fixture.Reader().ReadBackup(peer, ReadWorkflow.Identify(peer), temp.Path, progress, cancel.Token));
        string capture = Assert.Single(Directory.GetFiles(temp.Path, "pass1.read", SearchOption.AllDirectories));
        Assert.Equal(7936, new FileInfo(capture).Length);
        Assert.Equal(new FileInfo(capture).Length, progress.Latest!.BytesInPass);
        Assert.Equal(1, progress.Latest.Pass);
        Assert.Equal(2, reads);
        Assert.DoesNotContain(queued.Events, p => p.Stage is BackupReadStage.Verifying or BackupReadStage.Complete);
        Assert.Empty(Directory.GetFiles(temp.Path, "rom.bin", SearchOption.AllDirectories));
    }

    [Fact]
    public void test_failed_block_does_not_advance_or_complete_progress()
    {
        using var temp = new TempFolder();
        int reads = 0;
        using var peer = new TestEcu(Fixture.Rom) { ModifyResponse = (request, response) => request[0] == 0x23 && ++reads == 2 ? response[..^1] : response };
        var progress = new Capture();
        Assert.Throws<TransportException>(() => Fixture.Reader().ReadBackup(peer, ReadWorkflow.Identify(peer), temp.Path, progress));
        Assert.Equal(3968, progress.Events[^1].BytesInPass);
        Assert.DoesNotContain(progress.Events, p => p.Stage is BackupReadStage.Verifying or BackupReadStage.Complete);
        Assert.Equal(2, reads);
    }

    [Theory]
    [InlineData("mismatch")]
    [InlineData("rom-save")]
    [InlineData("journal-complete")]
    public void test_mismatch_or_storage_failure_never_reports_backup_complete(string failure)
    {
        using var temp = new TempFolder();
        int reads = 0;
        using var peer = new TestEcu(Fixture.Rom) { ModifyResponse = (request, response) =>
        {
            if (request[0] == 0x23 && ++reads == 266 && failure == "mismatch") response[^1] ^= 1;
            return response;
        }};
        var files = new TestFileSystem { Fault = failure == "mismatch" ? null : failure == "rom-save" ? "open" : "delete", Predicate = TestFileSystem.FileIs(failure == "journal-complete" ? "journal.jsonl.incomplete" : "rom.bin") };
        var progress = new Capture();
        var error = Record.Exception(() => Fixture.Reader(files).ReadBackup(peer, ReadWorkflow.Identify(peer), temp.Path, progress));
        if (failure == "mismatch") Assert.IsType<TransportException>(error); else Assert.IsType<UnauthorizedAccessException>(error);
        Assert.Equal(BackupReadStage.Verifying, progress.Events[^1].Stage);
        Assert.DoesNotContain(progress.Events, p => p.Stage == BackupReadStage.Complete);
        Assert.False(Fixture.Store(files).Scan(temp.Path).Single().Complete);
    }

    [Fact]
    public void test_stop_after_last_block_prevents_sealing_and_complete_progress()
    {
        using var temp = new TempFolder();
        using var peer = new TestEcu(Fixture.Rom);
        using var cancel = new CancellationTokenSource();
        var progress = new Capture(value => { if (value.Stage == BackupReadStage.Reading && value.Fraction == 1) cancel.Cancel(); });
        Assert.Throws<OperationCanceledException>(() => Fixture.Reader().ReadBackup(peer, ReadWorkflow.Identify(peer), temp.Path, progress, cancel.Token));
        Assert.Equal(1, progress.Events[^1].Fraction);
        Assert.DoesNotContain(progress.Events, p => p.Stage is BackupReadStage.Verifying or BackupReadStage.Complete);
        Assert.Empty(Directory.GetFiles(temp.Path, "rom.bin", SearchOption.AllDirectories));
        Assert.Equal(530, peer.Requests.Count(r => r[0] == 0x23));
    }
}
