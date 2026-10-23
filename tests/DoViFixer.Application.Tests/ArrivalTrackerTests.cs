using DoViFixer.Application.WatchFolder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Application.Tests;

/// <summary>A file in the watch folder is acted on only once it has finished arriving.</summary>
[TestClass]
public sealed class ArrivalTrackerTests
{
    private const string Movie = @"C:\Downloads\Movie.mkv";
    private static readonly DateTimeOffset Start = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AFileArrivesOnceItsSizeHasNotChangedForTheSettleTime()
    {
        var tracker = new ArrivalTracker(TimeSpan.FromSeconds(30));
        tracker.Notice(Movie, Start);
        var file = new ArrivingFile(true, 1000, false);

        Assert.IsEmpty(tracker.Poll(Start.AddSeconds(1), _ => file), "The first check only records the size.");
        Assert.IsEmpty(tracker.Poll(Start.AddSeconds(30), _ => file));
        CollectionAssert.AreEqual(new[] { Movie }, tracker.Poll(Start.AddSeconds(31), _ => file).ToArray());
        Assert.IsTrue(tracker.IsEmpty, "An arrived file is reported once.");
    }

    [TestMethod]
    public void GrowingRestartsTheWait()
    {
        var tracker = new ArrivalTracker(TimeSpan.FromSeconds(30));
        tracker.Notice(Movie, Start);
        tracker.Poll(Start.AddSeconds(1), _ => new(true, 1000, false));

        Assert.IsEmpty(tracker.Poll(Start.AddSeconds(25), _ => new(true, 2000, false)));
        Assert.IsEmpty(tracker.Poll(Start.AddSeconds(54), _ => new(true, 2000, false)));
        Assert.HasCount(1, tracker.Poll(Start.AddSeconds(55), _ => new(true, 2000, false)));
    }

    [TestMethod]
    public void AFileAnotherAppIsWritingWaitsUntilItIsClosed()
    {
        // Explorer sets a copy's final size first, so only the open file shows the copy is still running.
        var tracker = new ArrivalTracker(TimeSpan.FromSeconds(5));
        tracker.Notice(Movie, Start);
        tracker.Poll(Start.AddSeconds(1), _ => new(true, 1000, true));

        Assert.IsEmpty(tracker.Poll(Start.AddMinutes(10), _ => new(true, 1000, true)));
        Assert.HasCount(1, tracker.Poll(Start.AddMinutes(10).AddSeconds(1), _ => new(true, 1000, false)));
    }

    [TestMethod]
    public void AFileThatGoesAwayIsForgotten()
    {
        var tracker = new ArrivalTracker(TimeSpan.FromSeconds(5));
        tracker.Notice(Movie, Start);

        Assert.IsEmpty(tracker.Poll(Start.AddSeconds(10), _ => new(false, 0, false)));
        Assert.IsTrue(tracker.IsEmpty);
    }

    [TestMethod]
    public void AWriteNoticedAgainRestartsTheWaitAndAShorterSettleTimeApplies()
    {
        var tracker = new ArrivalTracker(TimeSpan.FromSeconds(30));
        tracker.Notice(Movie, Start);
        tracker.Poll(Start.AddSeconds(1), _ => new(true, 1000, false));
        tracker.Notice(Movie, Start.AddSeconds(20));
        tracker.Poll(Start.AddSeconds(21), _ => new(true, 1000, false));

        tracker.SettleTime = TimeSpan.FromSeconds(5);
        Assert.IsEmpty(tracker.Poll(Start.AddSeconds(25), _ => new(true, 1000, false)));
        Assert.HasCount(1, tracker.Poll(Start.AddSeconds(26), _ => new(true, 1000, false)));
    }
}
