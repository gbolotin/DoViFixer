# Validation warning integration

WpfFoundation retains `WfValidationWarningTemplate`, the right-side warning marker.
DoViFixer now uses `WfValidationOutlineTextBoxStyle` and `WfValidationOutlineComboBoxStyle` instead.
These styles reuse Microsoft's native Fluent `DefaultTextBoxInvalidationStyle` resource.
It draws a rounded error outline while preserving the focus underline and clear button.
No additional marker space is reserved. Errors appear in tooltips and UI Automation item status.
The Gallery's Edit note dialog demonstrates the marker on Title and the outline on Distance.

Numeric edits remain as text in `SettingsViewModel` until they can be parsed.
`INotifyDataErrorInfo` reports malformed values, numeric limits, and the required output folder.
Invalid preferences cannot be saved. Reset, correction, and Discard clear the appropriate errors.
Filesystem checks and unassigned service failures retain their existing footer handling.

## Release order

This integration requires WpfFoundation **1.1.0-preview.10** from the sibling checkout.
Build `DoViFixer.Local.sln` while the package is unpublished.
Do not ship this integration with the currently pinned **1.1.0-preview.8** packages;
they do not contain the new resource keys.

1. Publish the WpfFoundation change through its normal CI release.
2. Update both `WpfFoundation` and `WpfFoundation.Notifications` pins to `1.1.0-preview.10` in `Directory.Packages.props`.
3. Build and test with `UseLocalWpfFoundation=false` before releasing DoViFixer **0.1.22**.

Publication, pushes, and the post-publication package-pin update were outside this implementation request.

## Verification on 2026-10-17

- Local solution and WpfFoundation Release builds passed with zero warnings and errors.
- DoViFixer app suite: 204 passed.
- WpfFoundation suite: 129 passed, one skipped.
- XAML design checks and Git whitespace checks passed in both repositories.
- Tests cover numeric bounds, malformed input, finite percentages, save/close protection, correction, reset, and discard.
- WPF integration checks cover native conversion errors, multiple messages, generic FrameworkElements, accessibility, placement, resizing, reattachment, and section navigation.
- Windows UI Automation exercised the running app with an isolated data directory.
- Light, Dark, and the Windows Aquatic contrast theme were inspected. Windows contrast settings were restored to None.

Screenshots are under `artifacts/validation-warning/`: `analysis-warning-dark.png`,
`analysis-warning-light-final.png`, and `analysis-warning-high-contrast.png`.

## Native outline follow-up

The new styles reuse the installed .NET 10 Fluent template rather than copying Microsoft source.
Both warning-marker regression tests still pass.
Outline tests verify the native template, critical brush, rounded corners, tooltip restoration,
multiple errors, UI Automation item status, and removal after correction.
Reference: [Microsoft Fluent TextBox](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Fluent/Styles/TextBox.xaml).

The native outline screenshots are under `artifacts/validation-outline/`: `outline-dark.png`,
`outline-light.png`, and `outline-high-contrast.png`. All three themes were checked in the running app.
