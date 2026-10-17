using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace DoViFixer.App.ViewModels;

public sealed partial class SettingsViewModel : INotifyDataErrorInfo
{
    private readonly Dictionary<string, string> validationErrors = [];
    private bool updatingNumericText;

    public bool HasErrors => validationErrors.Count != 0;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    // There are no entity-level errors. Returning field errors for null would also
    // adorn Content/DataContext bindings to this ViewModel, outlining the whole page.
    public IEnumerable GetErrors(string? propertyName) => propertyName is not null && validationErrors.TryGetValue(propertyName, out string? error)
        ? new[] { error } : Array.Empty<string>();

    // Keep incomplete edits in the ViewModel: a failed WPF conversion would otherwise leave
    // the last valid number available to autosave and lose the edit when a view is recreated.
    [ObservableProperty]
    public partial string BrightnessMarginText { get; set; } = "50";

    partial void OnBrightnessMarginTextChanged(string value) => UpdateNumericPreference(() =>
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out int number))
        {
            BrightnessMarginNits = number;
        }
    });

    [ObservableProperty]
    public partial string AssumedPeakBrightnessText { get; set; } = "1000";

    partial void OnAssumedPeakBrightnessTextChanged(string value) => UpdateNumericPreference(() =>
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out int number))
        {
            AssumedPeakBrightnessNits = number;
        }
    });

    [ObservableProperty]
    public partial string FreeSpaceMarginText { get; set; } = "20";

    partial void OnFreeSpaceMarginTextChanged(string value) => UpdateNumericPreference(() =>
    {
        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out double number))
        {
            FreeSpaceMarginPercent = number;
        }
    });

    private void UpdateNumericPreference(Action update)
    {
        if (updatingNumericText)
        {
            return;
        }

        updatingNumericText = true;
        try
        {
            update();
        }
        finally
        {
            updatingNumericText = false;
        }
        TriggerAutoSave();
    }

    private void NumericPreferenceChanged(string propertyName)
    {
        if (!updatingNumericText)
        {
            SynchronizeNumericText(propertyName);
            TriggerAutoSave();
        }
    }

    private void SynchronizeNumericText(string? propertyName = null)
    {
        if (updatingNumericText)
        {
            return;
        }

        updatingNumericText = true;
        try
        {
            if (propertyName is null or nameof(BrightnessMarginText))
            {
                BrightnessMarginText = BrightnessMarginNits.ToString(CultureInfo.CurrentCulture);
            }
            if (propertyName is null or nameof(AssumedPeakBrightnessText))
            {
                AssumedPeakBrightnessText = AssumedPeakBrightnessNits.ToString(CultureInfo.CurrentCulture);
            }
            if (propertyName is null or nameof(FreeSpaceMarginText))
            {
                FreeSpaceMarginText = FreeSpaceMarginPercent.ToString(CultureInfo.CurrentCulture);
            }
        }
        finally
        {
            updatingNumericText = false;
        }
    }

    private void ValidatePreferences()
    {
        SetValidationError(nameof(BrightnessMarginText),
            int.TryParse(BrightnessMarginText, NumberStyles.Integer, CultureInfo.CurrentCulture, out int margin) && margin is >= 0 and <= 1000
                ? null : "Enter a whole number from 0 to 1000 nits.");
        SetValidationError(nameof(AssumedPeakBrightnessText),
            int.TryParse(AssumedPeakBrightnessText, NumberStyles.Integer, CultureInfo.CurrentCulture, out int peak) && peak is >= 100 and <= 10000
                ? null : "Enter a whole number from 100 to 10000 nits.");
        SetValidationError(nameof(FreeSpaceMarginText),
            double.TryParse(FreeSpaceMarginText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out double space) && double.IsFinite(space) && space is >= 0 and <= 200
                ? null : "Enter a number from 0 to 200 percent.");
        SetValidationError(nameof(Destination), DestinationError);
    }

    private void SetValidationError(string propertyName, string? error)
    {
        if (validationErrors.GetValueOrDefault(propertyName) == error)
        {
            return;
        }

        if (error is null)
        {
            validationErrors.Remove(propertyName);
        }
        else
        {
            validationErrors[propertyName] = error;
        }
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        OnPropertyChanged(nameof(HasErrors));
    }
}
