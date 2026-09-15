# WPF phase 4

Implemented 2026-09-11 against `docs/mockups/option-2-workflow-v3` for Media and `option-2-workflow-v2` for the other views. The older standalone `mockup/` project remains a prototype.

## Composition and presentation

`DoViFixer.App` is a separate .NET 10 Windows executable. Prism.DryIoc 9.0.537 imports the shared IServiceCollection descriptors through `IContainerExtension.Populate`. This is the API supplied by its pinned Prism.Container.DryIoc 9.0.106 dependency; there is no second provider or Generic Host. Composition tests check actual service resolution, transient and singleton behavior, factory dependencies, disposal and absence of registration-time filesystem activity. Prism 9 packages have their own [licensing terms](https://www.nuget.org/packages/Prism.DryIoc/9.0.537).

The shell retains three Prism region views, each with an injected transient ViewModel. Navigation is disabled during operations. File dialogs, approval dialogs and Explorer actions belong to presentation. Shared services receive typed requests and progress, never UI controls. Common.Wpf was not introduced because there is no second WPF consumer.

Media selection and focus are separate. Scan uses sampled inspection; standard/deep inspection uses the existing inspection service. Full analysis runs automatically during conversion planning. Changing an option or selection discards the prepared plan; rebuilding the review enables the single execution approval button. Outputs, archives, retention choices, exclusions and FEL warnings come from the prepared plans. Native processing revalidates identities, storage and output collisions through the existing services.

`ControlledBatchService` and per-operation `BatchControl` implement sequential, synchronized pending selection and current-file cancellation. A skipped item cannot start. Cancelling an active file waits for its workflow to return after recovery, then continues. Batch cancellation stops subsequent work. The Console's existing batch cancellation behavior is unchanged. Queued UI progress cannot overwrite a subsequent operation's final state. Progress without a native percentage remains indeterminate.

The right panel keeps source analysis and the last conversion outcome, including real output, verification and original-retention messages. Analysis failure remains distinct from Unknown and Complex FEL. Scan/inspection do not erase earlier conversion outcomes. Classification text accompanies every color.

Backup and restore present exact plans before execution. Cleanup lists exact discovered `.dovi` and `.bak.dovi_convert` files and requires a plan-specific `APPROVE` code. Tool setup displays package/version, source, destination, scope, elevation, digest and unavailable routes, then independently validates installed tools before resuming the requested workflow. No tools are installed at startup.

Preferences are persisted in one settings update after path validation. Existing console settings and tool paths remain compatible. Clearing the temporary directory restores the system default. Serilog is owned by the Prism root, uses the shared history/audit conventions and does not replace the static logger. App logs use a separate filename prefix in the shared Logs directory.

## Verification

```powershell
dotnet build DoViFixer.sln --no-restore
dotnet test DoViFixer.sln --no-build --no-restore --filter 'TestCategory!=NativeIntegration'
dotnet run --project tests/DoViFixer.App.StartupCheck -- artifacts/wpf-startup
```

App tests include full-analysis planning without a manual scan, approval invalidation after edits/selection, declined archive approval, actual Prism registration/disposal, WPF binding checks and a dispatcher-driven per-file cancellation/recovery/skip scenario. Rendering writes the real WPF views, with fake media services, to `artifacts/wpf/` at normal and minimum sizes. The separate startup check launches the real Prism App, verifies all three region views and activation, renders `artifacts/wpf-startup/shell.png`, then shuts down. Its settings/log root is isolated beneath the supplied artifact directory.

No real media conversions, deletions or software installations are performed by these checks. Native end-to-end validation retains the separate fixture/integration scope documented in upstream-parity.md. The UI does not synthesize sample percentages or claim frame evidence that the shared result does not contain.
