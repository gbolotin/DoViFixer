using System.Globalization;

namespace DoViFixer.App.ViewModels;

public sealed partial class SettingsViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScanCoverageSummary))]
    public partial int ScanSampleCount { get; set; } = 10;
    partial void OnScanSampleCountChanged(int value) => NumericPreferenceChanged(nameof(ScanSampleCountText));

    [ObservableProperty]
    public partial string ScanSampleCountText { get; set; } = "10";
    partial void OnScanSampleCountTextChanged(string value) => UpdateNumericPreference(() =>
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out int number))
        {
            ScanSampleCount = number;
        }
    });

    [RelayCommand]
    private void ResetScanSampleCount() => ScanSampleCountText = "10";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScanCoverageSummary))]
    public partial double ScanSampleSeconds { get; set; } = 1;
    partial void OnScanSampleSecondsChanged(double value) => NumericPreferenceChanged(nameof(ScanSampleSecondsText));

    [ObservableProperty]
    public partial string ScanSampleSecondsText { get; set; } = "1";
    partial void OnScanSampleSecondsTextChanged(string value) => UpdateNumericPreference(() =>
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double number))
        {
            ScanSampleSeconds = number;
        }
    });

    [RelayCommand]
    private void ResetScanSampleSeconds() => ScanSampleSecondsText = "1";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScanCoverageSummary))]
    public partial int MinimumScanCoveragePercent { get; set; } = 100;
    partial void OnMinimumScanCoveragePercentChanged(int value) => NumericPreferenceChanged(nameof(MinimumScanCoveragePercentText));

    [ObservableProperty]
    public partial string MinimumScanCoveragePercentText { get; set; } = "100";
    partial void OnMinimumScanCoveragePercentTextChanged(string value) => UpdateNumericPreference(() =>
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out int number))
        {
            MinimumScanCoveragePercent = number;
        }
    });

    [RelayCommand]
    private void ResetMinimumScanCoveragePercent() => MinimumScanCoveragePercentText = "100";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScanCoverageSummary))]
    public partial double InspectionPeakPercentile { get; set; } = 100;
    partial void OnInspectionPeakPercentileChanged(double value) => NumericPreferenceChanged(nameof(InspectionPeakPercentileText));

    [ObservableProperty]
    public partial string InspectionPeakPercentileText { get; set; } = "100";
    partial void OnInspectionPeakPercentileTextChanged(string value) => UpdateNumericPreference(() =>
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double number))
        {
            InspectionPeakPercentile = number;
        }
    });

    [RelayCommand]
    private void ResetInspectionPeakPercentile() => InspectionPeakPercentileText = "100";

    public string ScanCoverageSummary => $"At least {Math.Max(1, (int)Math.Ceiling(ScanSampleCount * (MinimumScanCoveragePercent / 100d)))} of {ScanSampleCount} clips must succeed. A lower requirement can miss brightness expansion.";
}
