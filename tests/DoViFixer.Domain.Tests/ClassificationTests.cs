using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Conversion;
using DoViFixer.Domain.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Domain.Tests;

[TestClass]
public sealed class ClassificationTests
{
    private static MediaInfo Media(double? maxCll = 1000) => new(new("movie.mkv", 1000, DateTime.UnixEpoch), DolbyVisionProfile.Profile7,
        "HEVC", 0, 1920, 1080, 24, 24, 1, 0, maxCll, new[] { new MediaTrack(0, "video", "HEVC", "und", "", true, false, "1") }, 0, 0, "", "{}");

    [TestMethod]
    [DataRow(0L, AnalysisVerdict.SimpleFel)]
    [DataRow(1L, AnalysisVerdict.ComplexFel)]
    public void DeepInspectionUsesFrameComparisonWithoutMaxCll(long expanded, AnalysisVerdict expected)
    {
        var evidence = new RpuEvidence(AnalysisMethod.DeepInspection, EnhancementLayer.Fel, 24, 1000, 1, 1,
            Brightness: new(24, expanded, 1000, 100, 12));
        Assert.AreEqual(expected, MediaClassifier.Classify(Media(null), evidence).Verdict);
        Assert.AreEqual(AnalysisVerdict.Unknown, MediaClassifier.Classify(Media(), evidence with { Brightness = null }).Verdict);
        Assert.AreEqual(AnalysisVerdict.Unknown, MediaClassifier.Classify(Media(), evidence with { Frames = 25 }).Verdict);
    }

    [TestMethod]
    public void MissingMaxCllIsUnknown()
    {
        var analysis = MediaClassifier.Classify(Media(null), new(AnalysisMethod.FullRpu, EnhancementLayer.Fel, 24, 1000, 1, 1));
        Assert.AreEqual(AnalysisVerdict.Unknown, analysis.Verdict);
        Assert.IsFalse(ConversionPolicy.Evaluate(analysis, true, true).Allowed);
    }

    [TestMethod]
    public void FailureIsDistinctFromComplexFel()
    {
        var analysis = MediaClassifier.Classify(Media(), new(AnalysisMethod.FullRpu, EnhancementLayer.Fel, 0, null, 0, 1, "bad data"));
        Assert.AreEqual(AnalysisVerdict.AnalysisFailed, analysis.Verdict);
        Assert.IsFalse(ConversionPolicy.Evaluate(analysis, true, true).Allowed);
    }

    [TestMethod]
    [DataRow(1000d, AnalysisVerdict.SimpleFel, false, false, false)]
    [DataRow(1000d, AnalysisVerdict.SimpleFel, true, false, true)]
    [DataRow(1100d, AnalysisVerdict.ComplexFel, true, false, false)]
    [DataRow(1100d, AnalysisVerdict.ComplexFel, false, true, true)]
    public void FelRequiresItsSpecificChoice(double peak, AnalysisVerdict verdict, bool simple, bool force, bool allowed)
    {
        var analysis = MediaClassifier.Classify(Media(), new(AnalysisMethod.FullRpu, EnhancementLayer.Fel, 24, peak, 1, 1));
        Assert.AreEqual(verdict, analysis.Verdict);
        Assert.AreEqual(allowed, ConversionPolicy.Evaluate(analysis, simple, force).Allowed);
    }

    [TestMethod]
    public void PartialSampleCoverageIsUnknownEvenForMel()
    {
        var analysis = MediaClassifier.Classify(Media(), new(AnalysisMethod.SampledRpu, EnhancementLayer.Mel, 24, null, 9, 10));
        Assert.AreEqual(AnalysisVerdict.Unknown, analysis.Verdict);
    }

    [TestMethod]
    public void MelDoesNotInventPeakMetadata()
    {
        var analysis = MediaClassifier.Classify(Media(null), new(AnalysisMethod.FullRpu, EnhancementLayer.Mel, 24, null, 1, 1));
        Assert.AreEqual(AnalysisVerdict.Mel, analysis.Verdict);
        Assert.IsNull(analysis.Media.MaxCll);
        Assert.IsTrue(ConversionPolicy.Evaluate(analysis, false, false).Allowed);
    }

    [TestMethod]
    public void PqEndpointsAndStorageOverflowAreChecked()
    {
        Assert.AreEqual(0, MediaClassifier.PqToNits(0), 0.001);
        Assert.AreEqual(10000, MediaClassifier.PqToNits(4095), 0.001);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => MediaClassifier.PqToNits(4096));
        Assert.ThrowsExactly<OverflowException>(() => ConversionPolicy.RequiredScratchBytes(long.MaxValue));
    }
}
