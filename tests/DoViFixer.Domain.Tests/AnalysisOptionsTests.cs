using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Domain.Tests;

[TestClass]
public sealed class AnalysisOptionsTests
{
    [TestMethod]
    public void PercentileExcludesRarePeaksAndMaximumPreservesThem()
    {
        var counts = new long[4096];
        Assert.IsNull(PeakBrightness.SelectPq(counts, 100));
        counts[2000] = 999;
        counts[4000] = 1;
        Assert.AreEqual(2000, PeakBrightness.SelectPq(counts, 99.9));
        Assert.AreEqual(4000, PeakBrightness.SelectPq(counts, 100));
        counts[2000] = 1;
        Assert.AreEqual(4000, PeakBrightness.SelectPq(counts, 99.9), "Small sets still include a rare peak.");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PeakBrightness.SelectPq(counts, double.NaN));
    }

    [TestMethod]
    public void CoverageRoundsUpAndNeverAllowsZeroSuccessfulClips()
    {
        var media = new MediaInfo(new("movie.mkv", 1, DateTime.UnixEpoch), DolbyVisionProfile.Profile7, "HEVC", 0, 1920, 1080, 24, 24, 1, 0, 1000, [], 0, 0, null, "{}");
        var options = new AnalysisOptions(MinimumScanCoveragePercent: 50);
        Assert.AreEqual(4, options.RequiredScanSamples(7));
        var evidence = new RpuEvidence(AnalysisMethod.SampledRpu, EnhancementLayer.Mel, 24, null, 4, 7, Options: options);
        Assert.AreEqual(AnalysisVerdict.Mel, MediaClassifier.Classify(media, evidence).Verdict);
        Assert.AreEqual(AnalysisVerdict.Unknown, MediaClassifier.Classify(media, evidence with { SuccessfulSamples = 3 }).Verdict);
        Assert.AreEqual(AnalysisVerdict.Unknown, MediaClassifier.Classify(media, evidence with { Method = AnalysisMethod.FullRpu }).Verdict);
        Assert.AreEqual(1, new AnalysisOptions(MinimumScanCoveragePercent: 1).RequiredScanSamples(1));
    }

    [TestMethod]
    public void InvalidOptionsAreRejectedAndEndpointsAreAllowed()
    {
        new AnalysisOptions(ScanSampleCount: 1, ScanSampleSeconds: 0.1, MinimumScanCoveragePercent: 1, InspectionPeakPercentile: 1).ValidateSampling();
        new AnalysisOptions(ScanSampleCount: 100, ScanSampleSeconds: 60).ValidateSampling();
        foreach (var invalid in new[]
        {
            new AnalysisOptions(ScanSampleCount: 0), new AnalysisOptions(ScanSampleCount: 101),
            new AnalysisOptions(ScanSampleSeconds: 0.09), new AnalysisOptions(ScanSampleSeconds: 61),
            new AnalysisOptions(ScanSampleSeconds: double.NaN), new AnalysisOptions(MinimumScanCoveragePercent: 0),
            new AnalysisOptions(MinimumScanCoveragePercent: 101), new AnalysisOptions(InspectionPeakPercentile: double.PositiveInfinity),
            new AnalysisOptions(InspectionPeakPercentile: 0), new AnalysisOptions(InspectionPeakPercentile: 101)
        })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(invalid.ValidateSampling);
        }
    }
}
