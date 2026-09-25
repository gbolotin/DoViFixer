# WPF phase 4

Implemented 2026-09-11 against `docs/mockups/option-2-workflow-v3` for Media and `option-2-workflow-v2` for the other views. The older standalone `mockup/` project remains a prototype.

## Composition and presentation

`DoViFixer.App` is a separate .NET 10 Windows executable using standard WPF startup and Microsoft.Extensions.DependencyInjection. AppComposition adds shared and presentation services to one IServiceCollection. App builds the validated root provider, resolves the shell, and disposes the provider on exit. Composition tests check service resolution, transient and singleton behavior, factory dependencies, disposal and absence of registration-time filesystem activity. Local ObservableObject and RelayCommand helpers implement INotifyPropertyChanged and ICommand without an MVVM framework.

The shell retains three workspace views in a ContentControl, each with an injected transient ViewModel. Navigation is disabled during operations. File dialogs, approval dialogs and Explorer actions belong to presentation. Shared services receive typed requests and progress, never UI controls. Common.Wpf was not introduced because there is no second WPF consumer.

Media selection and focus are separate. Scan uses sampled inspection; standard/deep inspection uses the existing inspection service. Full analysis runs automatically during conversion planning. Changing an option or selection discards the prepared plan; rebuilding the review enables the single execution approval button. Outputs, archives, retention choices, exclusions and FEL warnings come from the prepared plans. Native processing revalidates identities, storage and output collisions through the existing services.

`ControlledBatchService` and per-operation `BatchControl` implement sequential, synchronized pending selection and current-file cancellation. A skipped item cannot start. Cancelling an active file waits for its workflow to return after recovery, then continues. Batch cancellation stops subsequent work. The Console's existing batch cancellation behavior is unchanged. Queued UI progress cannot overwrite a subsequent operation's final state. Progress without a native percentage remains indeterminate.

The right panel keeps source analysis and the last conversion outcome, including real output, verification and original-retention messages. Analysis failure remains distinct from Unknown and Complex FEL. Scan/inspection do not erase earlier conversion outcomes. Classification text accompanies every color.

The Media table combines **Status / Action** in one column. New rows show Not scanned with an empty Profile / Type. Successful scan or inspection clears the row status; Profile / Type and the evidence in file details describe the result. Queued and active rows offer Skip and Cancel. Failed/cancelled analysis names the operation and offers Retry for that row using the same analysis method. Conversion results use Converted, Converted with warnings, or an operation-specific failure/cancellation label. Published output offers Open folder for the clicked row, independently of focus. Retrying analysis never executes conversion.

Backup and restore present exact plans before execution. Cleanup lists exact discovered `.dovi` and `.bak.dovi_convert` files and requires a plan-specific `APPROVE` code. Tool setup displays package/version, source, destination, scope, elevation, digest and unavailable routes, then independently validates installed tools before resuming the requested workflow. No tools are installed at startup.

Preferences are persisted in one settings update after path validation. Existing console settings and tool paths remain compatible. Clearing the temporary directory restores the system default. Serilog is owned by the application service provider, uses the shared history/audit conventions and does not replace the static logger. App logs use a separate filename prefix in the shared Logs directory.

Newly added files appear before automatic sampled scanning begins. Only newly added rows enter the sequential queue, with Queued/Scanning/result states, pending-item skipping, per-file cancellation and whole-batch cancellation. Valid cached analysis is checked before native-tool setup. Scan remains available for retries; full and deep inspection remain explicit actions. Adding files and starting other operations remain disabled while the batch is active.

Settings includes **Automatically scan added files** and **Use cached analysis results**, both enabled by default, including for existing settings files. Save defaults persists both options. Disabling cache reuse bypasses saved evidence and probe metadata for subsequent analysis and conversion planning; successful fresh analysis is still saved. **Clear analysis cache** immediately removes saved analysis JSON entries only, leaving media, settings, current displayed results and in-progress temporary files intact. An ongoing operation in another process may save fresh entries after clearing. No automatic size or age limit is applied.

## Verification

Full inspection supports a verified metadata-free ending: it checks the complete Annex B access-unit map against uniquely timestamped video packets, sorts into presentation order, and requires RPU-bearing frames to form a continuous prefix with no enhancement-layer data in the remaining tail. Leading/internal gaps, duplicate metadata, missing delimiters and unsupported multi-slice layouts remain blocked. Deep inspection still requires complete per-frame RPU coverage.

The result explains the tail frame count. When FEL is identified but brightness metadata is insufficient, the UI reports **FEL · Unclassified** and conversion requires explicit FEL approval (or `--force` in the CLI). Conversion retains the base-layer ending without synthesizing metadata. Verification checks original/output RPU counts and positions, output RPU profiles, absence of enhancement-layer data in the converted tail case, and the existing base-layer SHA-256, timestamps and retained-track checks. Coverage checking adds an extra sequential read of extracted video and packet timestamps. The ordinary safe-extraction fallback still applies.

```powershell
dotnet build DoViFixer.sln --no-restore
dotnet test DoViFixer.sln --no-build --no-restore --filter 'TestCategory!=NativeIntegration'
dotnet run --project tests/DoViFixer.App.StartupCheck -- artifacts/wpf-startup
```

App tests include full-analysis planning without a manual scan, approval invalidation after edits/selection, declined archive approval, actual DI registration/disposal, WPF binding checks and a dispatcher-driven per-file cancellation/recovery/skip scenario. Rendering writes the real WPF views, with fake media services, to `artifacts/wpf/` at normal and minimum sizes. The separate startup check launches the real WPF App, exercises all three navigation items and verifies retained views and ViewModels, renders `artifacts/wpf-startup/shell.png`, then shuts down. Its settings/log root is isolated beneath the supplied artifact directory.

No real media conversions, deletions or software installations are performed by these checks. Native end-to-end validation retains the separate fixture/integration scope documented in upstream-parity.md. The UI does not synthesize sample percentages or claim frame evidence that the shared result does not contain.
