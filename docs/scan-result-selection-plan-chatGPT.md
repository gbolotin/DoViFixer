# Automatic file selection based on scan results

Status: Proposed; implementation has not started.

## Goal

After a newly added file completes its initial scan, update its checkbox based on the scan result. Currently, added files start checked and remain checked regardless of the result.

## Proposed selection policy

The result-to-selection mapping below is a proposal, not a confirmed user requirement.

| Scan result | Automatically selected |
|---|---|
| Profile 7 MEL | Yes |
| Profile 7 Simple FEL | Yes; existing conversion warnings still apply |
| Complex FEL or unclassified FEL | No; the user can select manually |
| Other profiles or not applicable | No |
| Unknown, incomplete, failed, cancelled, or skipped | No |

Automatic selection requires a supported Profile 7 HEVC input. Selection does not authorize conversion or bypass existing conversion validation, warnings, or approval.

## Implementation plan

1. Add a small, testable rule for automatic selection, keeping media eligibility policy separate from UI state coordination.
2. Apply the rule when each newly added file finishes its initial scan, including cached results. Update the selected count and the All/None checkbox immediately.
3. Preserve existing files and their manual selections when adding more files. Keep row focus independent from checkbox selection.
4. When automatic scanning is disabled, retain the current checked state so users can run Scan. Apply the rule after that first scan. Later rescans preserve manual choices.
5. Ensure automatic deselection cannot trigger the existing pending-file skip behavior or retain an outdated conversion plan. Handle failed, cancelled, and skipped rows consistently, including rows that never start scanning.
6. Add focused tests for the selection rule and its integration with the Media workspace, then run the WPF tests, solution build, and whitespace checks.

## Expected code areas

- `src/DoViFixer.App/ViewModels/MediaViewModel.cs`: initial scan tracking, per-file completion handling, selection updates, and plan invalidation.
- `src/DoViFixer.App/ViewModels/MediaRow.cs`: explicit initial-scan selection state if needed.
- Domain policy: a focused selection recommendation rule that uses existing typed analysis results and eligibility rules without weakening conversion requirements.
- `tests/DoViFixer.App.Tests/ViewModelTests.cs`: workflow and selection regression coverage.
- Domain tests: result-to-selection mapping and unsupported-input coverage.

## Validation

- Verify each result category in the proposed policy.
- Verify identical selection behavior for fresh and cached scan results.
- Verify mixed batches update each completed file independently.
- Verify failed, cancelled, and skipped files finish unchecked without accidentally skipping other work.
- Verify adding files does not rescan or change the selection of existing rows.
- Verify automatic-scanning-disabled behavior and preservation of manual choices on later rescans.
- Verify selection summaries, All/None state, independent row focus, and conversion-plan invalidation.
- Run relevant WPF and domain tests, build the solution, and run `git diff --check`.

## Scope

This feature changes initial checkbox defaults in the WPF Media workspace. It does not start conversion, change conversion approval, or introduce row multiselect.
