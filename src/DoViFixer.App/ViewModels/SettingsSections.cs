namespace DoViFixer.App.ViewModels;

/// <summary>
/// A page of the settings frame. Every section shows part of the same <see cref="SettingsViewModel"/>; each has its own
/// type only so an implicit DataTemplate can pick its view.
/// </summary>
public abstract class SettingsSection(SettingsViewModel settings) : INavigationPage
{
    public SettingsViewModel Settings { get; } = settings;

    public abstract string NavigationName { get; }

    public abstract string? NavigationIcon { get; }
}

public sealed class GeneralSettingsSection(SettingsViewModel settings) : SettingsSection(settings)
{
    public override string NavigationName => "General";
    public override string? NavigationIcon => "\uE713";
}

public sealed class PresetsSettingsSection(SettingsViewModel settings) : SettingsSection(settings)
{
    public override string NavigationName => "Presets";
    public override string? NavigationIcon => "\uE8FD";
}

public sealed class ConversionSettingsSection(SettingsViewModel settings) : SettingsSection(settings)
{
    public override string NavigationName => "Conversion";
    public override string? NavigationIcon => "\uE8AB";
}

public sealed class AnalysisSettingsSection(SettingsViewModel settings) : SettingsSection(settings)
{
    public override string NavigationName => "Analysis";
    public override string? NavigationIcon => "\uE9D9";
}

public sealed class SafetySettingsSection(SettingsViewModel settings) : SettingsSection(settings)
{
    public override string NavigationName => "Safety";
    public override string? NavigationIcon => "\uEA18";
}

public sealed class WatchFolderSettingsSection(SettingsViewModel settings) : SettingsSection(settings)
{
    public override string NavigationName => "Watch folder";
    public override string? NavigationIcon => "\uE890";
}

public sealed class PerformanceSettingsSection(SettingsViewModel settings) : SettingsSection(settings)
{
    public override string NavigationName => "Performance";
    // The settings view shows WpfFoundation's vector gauge icon instead of a font glyph.
    public override string? NavigationIcon => null;
}

public sealed class NativeToolsSettingsSection(SettingsViewModel settings) : SettingsSection(settings)
{
    public override string NavigationName => "Native tools";
    public override string? NavigationIcon => "\uE90F";
}

public sealed class AboutSettingsSection(SettingsViewModel settings) : SettingsSection(settings)
{
    public override string NavigationName => "About";
    public override string? NavigationIcon => "\uE946";
}
