using DoViFixer.App.Presentation.Application;
using DoViFixer.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class RowRestoreTests
{
    [TestMethod]
    [DataRow("P81.mkv", "P81.dovi")]
    [DataRow("P81 - DV P8.1.mkv", "P81.dovi")]
    [DataRow("P81 - DV P8.1.mkv", "P81 - DV P8.1.dovi")]
    public async Task RestoreUsesMatchingArchiveAndOnlyClickedRow(string name, string archiveName)
    {
        using var runtime = new TestRuntime { Approval = true };
        string input = Path.Combine(@"C:\Media", name);
        string archive = Path.Combine(@"C:\Media", archiveName);
        runtime.Archives.Add(archive);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([input, @"C:\Media\Mountain.mkv"]);
        var row = model.Files[0];
        model.Focused = model.Files[1];
        Assert.IsTrue(row.CanRestore);
        Assert.IsFalse(row.IsSelected);
        Assert.IsTrue(model.RestoreRowCommand.CanExecute(row));
        Assert.IsFalse(model.RestoreRowCommand.CanExecute(new MediaRow(input) { Analysis = row.Analysis, RestoreArchive = archive }));
        runtime.DuringRestore = _ =>
        {
            Assert.IsTrue(row.IsActive);
            Assert.AreSame(row, model.BatchProgress.CurrentJob);
            Assert.IsFalse(model.RestoreRowCommand.CanExecute(row));
            Assert.IsFalse(model.ConvertRowDv81Command.CanExecute(model.Files[1]));
            return Task.CompletedTask;
        };

        await model.RestoreRowCommand.ExecuteAsync(row);

        CollectionAssert.AreEqual(new[] { input }, runtime.RestoredInputs);
        Assert.AreEqual(archive, runtime.ReadArchive);
        Assert.AreEqual(false, runtime.AllowLegacyArchive);
        Assert.HasCount(1, runtime.Reviews);
        StringAssert.Contains(runtime.Reviews[0], archive);
        Assert.AreEqual(MediaRowState.Restored, row.State);
        Assert.IsTrue(row.CanOpenResult);
        Assert.AreEqual(Path.ChangeExtension(input, ".restored.mkv"), row.Result!.Output);
        Assert.IsNull(model.Files[1].Result);
        Assert.AreSame(model.Files[1], model.Focused);
        Assert.IsFalse(row.IsSelected);
        Assert.IsTrue(model.Files[1].IsSelected);
        Assert.AreEqual(1, model.BatchProgress.Total);
    }

    [TestMethod]
    public async Task RestoreRequiresProfile81AndMatchingSiblingArchiveAndRefreshesOnRescan()
    {
        using var runtime = new TestRuntime();
        runtime.Archives.UnionWith([@"C:\Media\Mountain.dovi", @"C:\Media\Sdr.dovi", @"C:\Other\P81.dovi", @"C:\Media\Unrelated.dovi"]);
        foreach (string archive in runtime.Archives)
        {
            runtime.ArchiveBaseLayerHashes[archive] = new string('B', 64);
        }
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Sdr.mkv", @"C:\Media\P81.mkv"]);
        Assert.IsTrue(model.Files.All(row => !row.CanRestore && !model.RestoreRowCommand.CanExecute(row)));
        var row = model.Files[2];
        runtime.Archives.Add(@"C:\Media\P81.dovi");
        runtime.ArchiveBaseLayerHashes[@"C:\Media\P81.dovi"] = new string('A', 64);
        await model.ScanCommand.ExecuteAsync();
        Assert.IsTrue(row.CanRestore);
        runtime.Archives.Clear();

        await model.RestoreRowCommand.ExecuteAsync(row);

        Assert.IsFalse(row.CanRestore, "An archive removed after scanning must be detected before restoration.");
        Assert.IsEmpty(runtime.Reviews);
        Assert.IsEmpty(runtime.RestoredInputs);
        Assert.AreEqual(ViewStatus.Error, model.Status);
        Assert.IsFalse(new MediaRow(row.Path) { RestoreArchive = @"C:\Media\P81.dovi" }.CanRestore);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RestoreHonorsReviewAndArchiveValidation(bool approve)
    {
        using var runtime = new TestRuntime { Approval = approve, FailArchiveValidation = true };
        runtime.Archives.Add(@"C:\Media\P81.dovi");
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\P81.mkv"]);
        var row = model.Files.Single();

        await model.RestoreRowCommand.ExecuteAsync(row);

        Assert.IsEmpty(runtime.RestoredInputs);
        Assert.IsFalse(row.CanOpenResult);
        if (approve)
        {
            Assert.AreEqual(MediaRowState.Failed, row.State);
            StringAssert.Contains(row.Result!.Message, "SHA-256");
        }
        else
        {
            Assert.AreEqual(ViewStatus.RestoreNotApproved, model.Status);
            Assert.IsNull(row.Result);
        }
    }

    [TestMethod]
    public async Task RestoreCanBeCancelledFromTheRow()
    {
        using var runtime = new TestRuntime { Approval = true };
        runtime.Archives.Add(@"C:\Media\P81.dovi");
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\P81.mkv"]);
        var row = model.Files.Single();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DuringRestore = async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };
        var restoring = model.RestoreRowCommand.ExecuteAsync(row);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            model.CancelFileCommand.Execute(row);
            await restoring.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(MediaRowState.Cancelled, row.State);
            Assert.AreEqual("Restoration cancelled", row.Status);
            Assert.IsFalse(row.CanOpenResult);
            Assert.IsTrue(model.IsIdle);
            Assert.IsFalse(row.IsActive);
        }
        finally
        {
            model.CancelCommand.Execute();
            await restoring.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [TestMethod]
    public async Task PairingChecksSameNameFirstThenRenamedArchivesAndExcludesMatches()
    {
        using var runtime = new TestRuntime();
        runtime.Archives.UnionWith([@"C:\Media\P81.dovi", @"C:\Media\A-invalid.dovi", @"C:\Media\B-legacy.dovi", @"C:\Media\Z-renamed.dovi"]);
        runtime.ArchiveBaseLayerHashes[@"C:\Media\P81.dovi"] = new string('B', 64);
        runtime.ArchiveBaseLayerHashes[@"C:\Media\A-invalid.dovi"] = "invalid";
        runtime.ArchiveBaseLayerHashes[@"C:\Media\B-legacy.dovi"] = null;
        var model = runtime.Container.GetRequiredService<MediaViewModel>();

        await model.AddAsync([@"C:\Media\P81.mkv", @"C:\Media\P81-copy.mkv"]);

        Assert.AreEqual(@"C:\Media\P81.dovi", runtime.ManifestReads[0]);
        Assert.AreEqual(@"C:\Media\Z-renamed.dovi", model.Files[0].RestoreArchive);
        Assert.IsFalse(model.Files[1].CanRestore, "An archive already paired in this scan must not be assigned again.");
        Assert.AreEqual(1, runtime.ManifestReads.Count(path => path == @"C:\Media\Z-renamed.dovi"));
        CollectionAssert.AreEqual(new[] { @"C:\Media\P81.mkv", @"C:\Media\P81-copy.mkv" }, runtime.HashedInputs);

        runtime.ManifestReads.Clear();
        await model.ScanCommand.ExecuteAsync();
        Assert.AreEqual(@"C:\Media\Z-renamed.dovi", model.Files[0].RestoreArchive, "Rescan must release and rebuild existing pairings.");
        Assert.AreEqual(1, runtime.ManifestReads.Count(path => path == @"C:\Media\Z-renamed.dovi"));
    }

    [TestMethod]
    public async Task PairingPrefersMatchingNameAndSkipsHashingWithoutUsableManifests()
    {
        using var runtime = new TestRuntime();
        runtime.Archives.UnionWith([@"C:\Media\P81.dovi", @"C:\Media\A-other.dovi"]);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\P81.mkv"]);
        CollectionAssert.AreEqual(new[] { @"C:\Media\P81.dovi" }, runtime.ManifestReads);
        Assert.HasCount(1, runtime.HashedInputs);

        runtime.Archives.Clear();
        runtime.HashedInputs.Clear();
        await model.ScanCommand.ExecuteAsync();
        Assert.IsEmpty(runtime.HashedInputs);
        Assert.IsFalse(model.Files[0].CanRestore);
        runtime.Archives.Add(@"C:\Media\legacy.dovi");
        runtime.ArchiveBaseLayerHashes[@"C:\Media\legacy.dovi"] = null;
        await model.ScanCommand.ExecuteAsync();
        Assert.IsEmpty(runtime.HashedInputs);
        Assert.IsFalse(model.Files[0].CanRestore);
    }

    [TestMethod]
    public async Task CachedHashMatchesRenamedArchiveWithoutToolsAndHonorsCacheSetting()
    {
        using var runtime = new TestRuntime();
        runtime.Archives.Add(@"C:\Media\P81.dovi");
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\P81.mkv"]);
        Assert.HasCount(1, runtime.HashedInputs);
        runtime.Archives.Clear();
        runtime.Archives.Add(@"C:\Media\Renamed.dovi");
        runtime.Ready = false;

        await model.ScanCommand.ExecuteAsync();

        Assert.AreEqual(@"C:\Media\Renamed.dovi", model.Files.Single().RestoreArchive);
        Assert.HasCount(1, runtime.HashedInputs);
        Assert.IsEmpty(runtime.Reviews);
        runtime.Ready = true;
        await runtime.UpdateAsync(settings => settings with { UseCachedResults = false }, default);
        await model.ScanCommand.ExecuteAsync();
        Assert.HasCount(2, runtime.HashedInputs);
    }

    [TestMethod]
    public async Task UncachedPairingOffersMissingToolSetupWithoutLosingCachedAnalysis()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\P81.mkv"]);
        runtime.Archives.Add(@"C:\Media\P81.dovi");
        runtime.Ready = false;

        await model.ScanCommand.ExecuteAsync();

        Assert.HasCount(1, runtime.Reviews);
        Assert.IsEmpty(runtime.HashedInputs);
        Assert.IsNotNull(model.Files.Single().Analysis);
        Assert.IsFalse(model.Files.Single().CanRestore);
        StringAssert.Contains(model.Files.Single().Notice, "Configure tools");
    }

    [TestMethod]
    public async Task PairingCanBeCancelledWithoutAssigningAnArchive()
    {
        using var runtime = new TestRuntime();
        runtime.Archives.Add(@"C:\Media\P81.dovi");
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DuringHash = async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };
        var adding = model.AddAsync([@"C:\Media\P81.mkv"]);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            model.CancelFileCommand.Execute(model.Files.Single());
            await adding.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(model.Files.Single().CanRestore);
            Assert.IsTrue(model.IsIdle);
        }
        finally
        {
            model.CancelCommand.Execute();
            await adding.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
