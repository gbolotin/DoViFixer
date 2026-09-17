# Select Files Based on Scan Result

When files are added to the list and scanned (either automatically upon adding or via manual scan/inspection), DoViFixer should update each file's selection state (`IsSelected`) depending on its scan result. Conversion candidates (Profile 7 MEL and optionally Simple FEL) are selected, while non-convertible files (non-Profile 7, already converted Profile 8.1, Profile 5, SDR/HDR10, scan failures, or incomplete scans) are automatically deselected.

## User Review Required

> [!IMPORTANT]
> **Candidate Selection Criteria**:
> By default, we recommend selecting **Profile 7 MEL** (lossless) and **Profile 7 Simple FEL** (standard candidate in DoViFixer GUI), while keeping **Profile 7 Complex FEL** deselected because converting Complex FEL permanently discards enhancement-layer video picture data. Users can still manually check Complex FEL files if they wish.
>
> Please confirm if this default behavior aligns with your expectations, or if you prefer a different selection rule (e.g. MEL only, or all Profile 7 including Complex FEL).

> [!NOTE]
> **Unscanned Files Initial State**:
> When files are first added to the list, they remain initially selected until scanning completes. This ensures that if automatic scanning is turned off in Settings, the "Scan" button remains enabled so the user can scan them. Once each file's scan finishes, its selection is updated to reflect its scan result.

---

## Open Questions

> [!IMPORTANT]
> 1. **Which scan results should be auto-selected by default?**
>    - **Option A (Recommended)**: Select Profile 7 MEL and Simple FEL. Deselect Complex FEL, Incomplete/Unknown scans, Analysis Failed, and non-Profile 7 files (Profile 8.1, Profile 5, SDR, HDR10).
>    - **Option B**: Select only Profile 7 MEL (pure lossless conversion only).
>    - **Option C**: Select all Profile 7 files (MEL, Simple FEL, and Complex FEL).
>
> 2. **Should this be configurable in Settings?**
>    - Should we add an "Auto-select conversion candidates after scan" toggle in `SettingsView` (under "Scanning and analysis cache")? We recommend adding this setting (enabled by default) so users who prefer manual selection can turn it off.
>
> 3. **Should re-inspection or retrying analysis also update selection?**
>    - When a user clicks "Inspect" on an incomplete scan row or "Retry" on a failed row, if the inspection completes successfully as a conversion candidate (e.g. MEL), should the row automatically become selected? We recommend **yes**, keeping selection state synchronized with the latest scan/inspection verdict.

---

## Proposed Changes

 Group files by component and order logically.

### DoViFixer.Domain

Define pure candidate selection policy in the Domain layer according to architecture rules.

#### [MODIFY] [ConversionPolicy.cs](file:///c:/Users/gbolotin/OneDrive/source/repos/DoViFixer/src/DoViFixer.Domain/Conversion/ConversionPolicy.cs)
- Add domain policy method `IsConversionCandidate(MediaAnalysis? analysis, bool includeSimpleFel = true, bool includeComplexFel = false)`.
- Returns `true` only for HEVC Dolby Vision Profile 7 inputs with `AnalysisVerdict.Mel` or `AnalysisVerdict.SimpleFel` (when `includeSimpleFel` is true) or `AnalysisVerdict.ComplexFel` (when `includeComplexFel` is true).
- Returns `false` for `AnalysisVerdict.NotApplicable` (e.g. Profile 8.1, Profile 5, SDR, HDR10), `AnalysisVerdict.AnalysisFailed`, `AnalysisVerdict.Unknown`, `AnalysisVerdict.FelUnclassified`, and non-HEVC/non-Profile 7 files.

---

### DoViFixer.Application

Add setting for candidate auto-selection preference.

#### [MODIFY] [UserSettings.cs](file:///c:/Users/gbolotin/OneDrive/source/repos/DoViFixer/src/DoViFixer.Application/Settings/UserSettings.cs)
- Add property `public bool AutoSelectCandidatesAfterScan { get; init; } = true;`.

#### [MODIFY] [SettingsService.cs](file:///c:/Users/gbolotin/OneDrive/source/repos/DoViFixer/src/DoViFixer.Application/Settings/SettingsService.cs)
- Support updating and persisting `AutoSelectCandidatesAfterScan` in `SetPreferencesAsync`.

---

### DoViFixer.App

Update WPF presentation and ViewModels to apply selection based on scan results.

#### [MODIFY] [MediaViewModel.cs](file:///c:/Users/gbolotin/OneDrive/source/repos/DoViFixer/src/DoViFixer.App/ViewModels/MediaViewModel.cs)
- In `AnalyzeRowsAsync`, when each row finishes analysis (reported via `InlineProgress<FileResult>` or batch completion):
  - If `AutoSelectCandidatesAfterScan` is enabled (from user settings):
    - When analysis completes successfully (`OperationStatus.Completed`): evaluate `ConversionPolicy.IsConversionCandidate(row.Analysis)` and set `row.IsSelected` accordingly.
    - When analysis fails (`OperationStatus.Failed`): set `row.IsSelected = false`.
    - If cancelled: keep existing state.
- Ensure that updating `row.IsSelected` programmatically does not trigger `Skip(row)` (since the row has already finished active/pending status, `row.IsPending` is false).
- Update selection summaries and commands (`RaisePropertyChanged(nameof(SelectionSummary))`, `RaisePropertyChanged(nameof(AllFilesSelected))`, `CommandsChanged()`).

#### [MODIFY] [SettingsViewModel.cs](file:///c:/Users/gbolotin/OneDrive/source/repos/DoViFixer/src/DoViFixer.App/ViewModels/SettingsViewModel.cs)
- Add `AutoSelectCandidatesAfterScan` property, binding load/save commands.

#### [MODIFY] [SettingsView.xaml](file:///c:/Users/gbolotin/OneDrive/source/repos/DoViFixer/src/DoViFixer.App/Views/SettingsView.xaml)
- Add a checkbox in the "Scanning and analysis cache" section:
  - `<CheckBox Content="Auto-select conversion candidates after scan" IsChecked="{Binding AutoSelectCandidatesAfterScan}" />`

---

### Tests

#### [MODIFY] [ClassificationTests.cs](file:///c:/Users/gbolotin/OneDrive/source/repos/DoViFixer/tests/DoViFixer.Domain.Tests/ClassificationTests.cs)
- Add unit tests verifying `ConversionPolicy.IsConversionCandidate` for:
  - Profile 7 MEL (true)
  - Profile 7 Simple FEL (true when included, false when excluded)
  - Profile 7 Complex FEL (false by default, true when forced)
  - Profile 7 Unknown / Incomplete (false)
  - Profile 8.1, Profile 5, No DV / SDR (false)
  - Failed analysis / null (false)

#### [MODIFY] [TestRuntime.cs](file:///c:/Users/gbolotin/OneDrive/source/repos/DoViFixer/tests/DoViFixer.App.Tests/TestRuntime.cs)
- Support probing different profiles (e.g. detecting Profile 8.1 or SDR based on file name) so multi-file mixed-candidate scenarios can be tested.

#### [MODIFY] [ViewModelTests.cs](file:///c:/Users/gbolotin/OneDrive/source/repos/DoViFixer/tests/DoViFixer.App.Tests/ViewModelTests.cs)
- Add test: `ScanSelectsCandidatesAndDeselectsNonCandidates`:
  - Adds a batch of mixed files (MEL, Simple FEL, Complex FEL, Profile 8.1, SDR).
  - Verifies that after scan completes, MEL and Simple FEL are selected (`IsSelected == true`), while Complex FEL, Profile 8.1, and SDR are deselected (`IsSelected == false`).
- Add test: `InspectIncompleteSelectsRowWhenPromotedToCandidate`:
  - Verifies that an unselected incomplete row becomes selected once inspected into MEL.
- Add test: `DisablingAutoSelectPreservesManualSelection`:
  - Verifies that when `AutoSelectCandidatesAfterScan` is false, scanning does not alter the user's manual selection.

---

## Verification Plan

### Automated Tests
Run unit and integration tests across Domain and App test suites:
```powershell
dotnet test tests/DoViFixer.Domain.Tests/DoViFixer.Domain.Tests.csproj
dotnet test tests/DoViFixer.App.Tests/DoViFixer.App.Tests.csproj
```

### Manual Verification
1. Launch the WPF application (`DoViFixer.App`).
2. Add a folder containing diverse video files (e.g., Profile 7 MEL, Profile 8.1, SDR).
3. Observe automatic scanning: as files are analyzed, only valid Profile 7 conversion candidates stay/become checked `[x]`, while non-convertible files become unchecked `[ ]`.
4. Check Settings: verify "Auto-select conversion candidates after scan" toggle can be disabled and saved.
