# DoViFixer-specific rules

Apply these rules together with the [common development rules](common-rules.md). The solution architecture and implementation plan are documented in [../architecture.md](../architecture.md).

## Project architecture

DoViFixer provides native Windows C#/.NET Console and WPF applications. The WPF application uses MVVM and standard WPF. Both applications share the same domain, application, and infrastructure code.

Main projects:

- DoViFixer.Domain: domain models, business rules, state, and core abstractions.
- DoViFixer.Application: shared use cases, workflow coordination, requests, results, progress models, and interfaces for external capabilities.
- DoViFixer.Infrastructure: repositories, persistence, configuration, file system, and other infrastructure.
- DoViFixer.Console: console entry point, command parsing, interaction, rendering, and dependency injection composition.
- DoViFixer.Mcp: Model Context Protocol server over stdio for AI assistants; tool definitions, plan and job tracking, and dependency injection composition.
- DoViFixer.App: WPF entry point, views, ViewModels, commands, dialogs, WPF navigation, and dependency injection composition.
- WpfFoundation (separate repository and NuGet package): shared WPF styles, controls, behaviors, converters, navigation, dialogs and theme service. DoViFixer.App references the package pinned in `Directory.Packages.props`, or `..\WpfFoundation` through a project reference when that checkout exists (see [WpfFoundation reference](#wpffoundation-reference)).

## Layering and media workflows

- Keep domain logic in DoViFixer.Domain and use-case coordination in DoViFixer.Application.
- Put interfaces needed by application workflows in DoViFixer.Application/Abstractions; keep domain-specific abstractions in Domain when appropriate.
- DoViFixer.Infrastructure implements the inner layers' abstractions. Neither Domain nor Application may reference Infrastructure, Console, or App.
- Keep WPF navigation, dialogs, and ViewModels in DoViFixer.App. Reusable presentation infrastructure belongs in WpfFoundation, not in DoViFixer; never move DoViFixer workflows or wording into it.
- Use CommunityToolkit.Mvvm's `ObservableObject`, `RelayCommand` and `AsyncRelayCommand` directly. Their `Execute` and `ExecuteAsync` do not check `CanExecute`, so tests invoke commands through `CommandInvocation`, as a bound button would.
- ViewModels and console commands call application services; they must not implement conversion rules or low-level tool/file operations.
- Never hardcode the app name in text users see or services receive (window titles, version text, help, messages, user agents). Use `ApplicationTitle.Name` (DoViFixer.Application/Updates), which reads the `AssemblyTitle` set in Directory.Build.props; XAML binds it with `{x:Static}`. Namespaces, executable and solution names, data folders, configuration keys and repository URLs keep their literal names.
- No executable project (DoViFixer.Console, DoViFixer.App, DoViFixer.Mcp) references another. Shared functionality belongs in the shared libraries.
- MCP tools that change files take a plan id from a matching plan tool, so the assistant can show the exact plan and get the user's approval first. Never write to standard output in DoViFixer.Mcp; it carries the protocol.
- Keep media analysis, conversion policy, workflow coordination, tool execution, and UI presentation separated.
- Use interfaces at the media-specific boundaries: media probing and processing; file discovery, temporary storage, and output publication; archive storage and repositories where persistence is needed.

## Dependency injection choices

- Use Microsoft.Extensions.DependencyInjection through the .NET Generic Host in DoViFixer.Console.
- Use Microsoft.Extensions.DependencyInjection in DoViFixer.App. Register shared services, ViewModels, and the shell in one IServiceCollection; build the root provider in WPF startup and dispose it on exit.
- Define AddDoViFixerApplication and AddDoViFixerInfrastructure registration extensions in their respective libraries, using Microsoft.Extensions.DependencyInjection.Abstractions.
- Do not build additional service providers inside registration extensions or alongside the application root.
- WPF does not provide automatic per-job scopes; introduce explicit operation scopes only when scoped dependencies are introduced.

- `Directory.Build.props` turns on `UseLocalWpfFoundation` only when building `DoViFixer.Local.sln` and `..\WpfFoundation\src\WpfFoundation\WpfFoundation.csproj` exists. DoViFixer.App then references that project instead of the `WpfFoundation` package, so library code can be debugged and edited in the same session. Keep the switch tied to that solution: Visual Studio restore fails with NU1105 when a project references one the open solution does not list.
- `DoViFixer.sln` lists only DoViFixer's projects and always uses the pinned package, so it builds anywhere. `DoViFixer.Local.sln` also lists the WpfFoundation project for local work.
- Upgrade the library by changing the `WpfFoundation` version in `Directory.Packages.props`.
- CI and release builds pass `-p:UseLocalWpfFoundation=false`, so a shipped build always uses the pinned package, never local, possibly uncommitted library code.

## Versioning

- Every pull request bumps `VersionPrefix` in Directory.Build.props: CI releases each push to main as that version and tags it, and fails if the tag already exists. Never push tags by hand from an agent session.

## Native dependencies

- Detect FFmpeg (ffmpeg.exe and ffprobe.exe), MKVToolNix (mkvmerge.exe and mkvextract.exe), MediaInfo CLI (mediainfo.exe), and dovi_tool (dovi_tool.exe).
- Automatically check the dependencies required by a requested media operation before starting it. Keep help, argument parsing, and DI registration free of dependency probes and installation.
- Offer automatic installation of missing dependencies. Present the exact tools, sources, versions, installation scope, and any elevation requirement before asking for consent. An explicit dependency-install command or installation-specific unattended option may provide that consent; conversion confirmation alone does not.
- Reuse approval for the same installation plan; do not prompt again for each file in a batch. Do not silently install or upgrade unrelated software.
- Keep dependency detection and installation coordination in Application, installer/download/process details in Infrastructure, and prompts/progress in Console or App. Register these services through the existing DI extensions.
- Re-detect and validate executables after installation, persist selected tool paths in user settings, and apply them immediately in the current process before resuming the requested operation.
- If installation fails, is declined, or is unavailable, report the affected tools and actionable recovery steps; do not start an operation with unmet requirements.

## Testing and refactoring checks

- Verify container registrations, service lifetimes, and disposal without running media conversions.
- Keep tests that require native tools and media fixtures separate from fast unit tests.
- Check for native-tool command lines and output parsing leaking outside Infrastructure.
- Check for duplicated conversion logic between console commands and ViewModels.
