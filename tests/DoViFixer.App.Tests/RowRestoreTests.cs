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
        Assert.AreEqual(new string('A', 64), await runtime.ReadBaseLayerHashAsync(runtime.Identify(input), default), "A verified restore records the file's base-layer hash.");
        Assert.IsFalse(row.CanRestore, "A restored row must not offer restoration again.");
        Assert.IsFalse(model.RestoreRowCommand.CanExecute(row));
        Assert.AreEqual(archive, row.RestoreArchive, "The restored row keeps its archive so no other row can claim it.");

        await model.ScanCommand.ExecuteAsync();
        Assert.AreEqual(MediaRowState.Scanned, row.State);
        Assert.IsTrue(row.CanRestore, "Rescanning returns the row to a restorable state.");
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
    public async Task CachedHashChecksSameNameFirstThenRenamedArchivesAndExcludesMatches()
    {
        using var runtime = new TestRuntime();
        runtime.Archives.UnionWith([@"C:\Media\P81.dovi", @"C:\Media\A-invalid.dovi", @"C:\Media\B-legacy.dovi", @"C:\Media\Z-renamed.dovi"]);
        runtime.ArchiveBaseLayerHashes[@"C:\Media\P81.dovi"] = new string('B', 64);
        runtime.ArchiveBaseLayerHashes[@"C:\Media\A-invalid.dovi"] = "invalid";
        runtime.ArchiveBaseLayerHashes[@"C:\Media\B-legacy.dovi"] = null;
        await CacheBaseLayerHashAsync(runtime, @"C:\Media\P81.mkv", @"C:\Media\P81-copy.mkv");
        var model = runtime.Container.GetRequiredService<MediaViewModel>();

        await model.AddAsync([@"C:\Media\P81.mkv", @"C:\Media\P81-copy.mkv"]);

        Assert.AreEqual(@"C:\Media\P81.dovi", runtime.ManifestReads[0]);
        Assert.AreEqual(@"C:\Media\Z-renamed.dovi", model.Files[0].RestoreArchive, "A same-name archive with a different base-layer hash must be passed over.");
        Assert.IsFalse(model.Files[1].CanRestore, "An archive already paired in this scan must not be assigned again.");
        Assert.AreEqual(1, runtime.ManifestReads.Count(path => path == @"C:\Media\Z-renamed.dovi"));

        runtime.ManifestReads.Clear();
        await model.ScanCommand.ExecuteAsync();
        Assert.AreEqual(@"C:\Media\Z-renamed.dovi", model.Files[0].RestoreArchive, "Rescan must release and rebuild existing pairings.");
        Assert.AreEqual(1, runtime.ManifestReads.Count(path => path == @"C:\Media\Z-renamed.dovi"));
    }

    [TestMethod]
    public async Task UncachedPairingOffersOnlyAUsableSameNameArchive()
    {
        using var runtime = new TestRuntime();
        runtime.Archives.UnionWith([@"C:\Media\P81.dovi", @"C:\Media\A-other.dovi"]);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();

        await model.AddAsync([@"C:\Media\P81.mkv"]);

        var row = model.Files.Single();
        Assert.AreEqual(@"C:\Media\P81.dovi", row.RestoreArchive);
        CollectionAssert.AreEqual(new[] { @"C:\Media\P81.dovi" }, runtime.ManifestReads, "Without a cached hash, differently named archives are not read.");
        foreach (string? unusable in new[] { null, "invalid" })
        {
            runtime.ArchiveBaseLayerHashes[@"C:\Media\P81.dovi"] = unusable;
            await model.ScanCommand.ExecuteAsync();
            Assert.IsFalse(row.CanRestore, "A same-name archive without a readable manifest must not be offered.");
        }

        runtime.Archives.Clear();
        runtime.ArchiveBaseLayerHashes.Clear();
        runtime.Archives.Add(@"C:\Media\Renamed.dovi");
        await model.ScanCommand.ExecuteAsync();
        Assert.IsFalse(row.CanRestore);
    }

    [TestMethod]
    public async Task CachedHashMatchesRenamedArchiveAndHonorsCacheSetting()
    {
        using var runtime = new TestRuntime();
        runtime.Archives.Add(@"C:\Media\Renamed.dovi");
        await CacheBaseLayerHashAsync(runtime, @"C:\Media\P81.mkv");
        var model = runtime.Container.GetRequiredService<MediaViewModel>();

        await model.AddAsync([@"C:\Media\P81.mkv"]);
        Assert.AreEqual(@"C:\Media\Renamed.dovi", model.Files.Single().RestoreArchive);

        runtime.ArchiveBaseLayerHashes[@"C:\Media\Renamed.dovi"] = new string('B', 64);
        await model.ScanCommand.ExecuteAsync();
        Assert.IsFalse(model.Files.Single().CanRestore, "A renamed archive must match the cached base-layer hash.");

        runtime.ArchiveBaseLayerHashes.Clear();
        await runtime.UpdateAsync(settings => settings with { UseCachedResults = false }, default);
        await model.ScanCommand.ExecuteAsync();
        Assert.IsFalse(model.Files.Single().CanRestore, "Without cached results only same-name archives are offered.");
    }

    [TestMethod]
    public async Task PairingNeedsNoMediaToolsOrSetupPrompt()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\P81.mkv", @"C:\Media\P81-copy.mkv"]);
        runtime.Archives.UnionWith([@"C:\Media\P81.dovi", @"C:\Media\P81-copy.dovi"]);
        runtime.Ready = false;

        await model.ScanCommand.ExecuteAsync();

        Assert.IsEmpty(runtime.Reviews);
        Assert.AreEqual(@"C:\Media\P81.dovi", model.Files[0].RestoreArchive);
        Assert.AreEqual(@"C:\Media\P81-copy.dovi", model.Files[1].RestoreArchive);
    }

    [TestMethod]
    public async Task RowsACancelledScanNeverReachedKeepTheirRestoreArchive()
    {
        using var runtime = new TestRuntime();
        runtime.Archives.UnionWith([@"C:\Media\P81.dovi", @"C:\Media\P81-copy.dovi"]);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\P81.mkv", @"C:\Media\P81-copy.mkv"]);
        Assert.IsTrue(model.Files.All(row => row.CanRestore));
        await runtime.UpdateAsync(settings => settings with { UseCachedResults = false }, default);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Profile 8.1 inspection never reaches RPU analysis, so interrupt the scan while it probes the first row.
        runtime.DuringProbe = async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };

        var scanning = model.ScanCommand.ExecuteAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        model.CancelCommand.Execute();
        await scanning.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsFalse(model.Files[0].CanRestore, "The interrupted row lost its analysis and must be rescanned.");
        Assert.AreEqual(MediaRowState.Cancelled, model.Files[1].State);
        Assert.AreEqual(@"C:\Media\P81-copy.dovi", model.Files[1].RestoreArchive);
        Assert.IsTrue(model.Files[1].CanRestore);
        Assert.IsTrue(model.RestoreRowCommand.CanExecute(model.Files[1]));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedRestoreOfMismatchedSameNameArchiveStopsOfferingIt(bool renamedMatchExists)
    {
        string inputHash = new string('C', 64);
        using var runtime = new TestRuntime { Approval = true, RestoreBaseLayerSha256 = inputHash };
        runtime.Archives.Add(@"C:\Media\P81.dovi");
        if (renamedMatchExists)
        {
            runtime.Archives.Add(@"C:\Media\Renamed.dovi");
            runtime.ArchiveBaseLayerHashes[@"C:\Media\Renamed.dovi"] = inputHash;
        }

        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\P81.mkv"]);
        var row = model.Files.Single();
        Assert.AreEqual(@"C:\Media\P81.dovi", row.RestoreArchive, "Without a cached hash the same-name archive is offered.");

        await model.RestoreRowCommand.ExecuteAsync(row);

        Assert.AreEqual(MediaRowState.Failed, row.State);
        StringAssert.Contains(row.Result!.Message, "SHA-256 mismatch");
        Assert.AreEqual(inputHash, await runtime.ReadBaseLayerHashAsync(runtime.Identify(@"C:\Media\P81.mkv"), default));
        Assert.AreEqual(renamedMatchExists ? @"C:\Media\Renamed.dovi" : null, row.RestoreArchive, "The learned hash must replace the mismatched pairing.");
        Assert.AreEqual(renamedMatchExists, model.RestoreRowCommand.CanExecute(row));

        await model.ScanCommand.ExecuteAsync();
        Assert.AreEqual(renamedMatchExists ? @"C:\Media\Renamed.dovi" : null, row.RestoreArchive, "Rescanning must not offer the mismatched archive again.");
    }

    [TestMethod]
    public async Task PairingFailureKeepsCompletedAnalysisAndExplainsWhy()
    {
        using var runtime = new TestRuntime
        {
            ArchiveDiscoveryFailure = new UnauthorizedAccessException(@"Access to C:\Media is denied.")
        };
        var model = runtime.Container.GetRequiredService<MediaViewModel>();

        await model.AddAsync([@"C:\Media\P81.mkv"]);

        var row = model.Files.Single();
        Assert.AreEqual(MediaRowState.Scanned, row.State);
        Assert.IsNotNull(row.Analysis);
        Assert.IsNull(row.AnalysisError);
        Assert.IsFalse(row.CanRestore);
        StringAssert.Contains(row.Notice, @"Access to C:\Media is denied.");
    }

    private static async Task CacheBaseLayerHashAsync(TestRuntime runtime, params string[] inputs)
    {
        // Conversion caches the published output's base-layer hash; seed it the same way.
        foreach (string input in inputs)
        {
            await runtime.WriteBaseLayerHashAsync(runtime.Identify(input), new string('A', 64), default);
        }
    }
}
