# DoViFixer-specific rules

Apply these rules together with the [common development rules](common-rules.md). The solution architecture and implementation plan are documented in [../architecture.md](../architecture.md).

## Project architecture

DoViFixer provides native Windows C#/.NET Console and WPF applications. The WPF application uses MVVM and Prism. Both applications share the same domain, application, and infrastructure code.

Main projects:

- DoViFixer.Domain: domain models, business rules, state, and core abstractions.
- DoViFixer.Application: shared use cases, workflow coordination, requests, results, progress models, and interfaces for external capabilities.
- DoViFixer.Infrastructure: repositories, persistence, configuration, file system, and other infrastructure.
- DoViFixer.Console: console entry point, command parsing, interaction, rendering, and dependency injection composition.
- DoViFixer.App: WPF entry point, views, ViewModels, commands, dialogs, Prism navigation, and dependency injection composition.
- DoViFixer.Common.Wpf: optional shared WPF utilities and reusable UI infrastructure; add when there is concrete reuse.

## Layering and media workflows

- Keep domain logic in DoViFixer.Domain and use-case coordination in DoViFixer.Application.
- Put interfaces needed by application workflows in DoViFixer.Application/Abstractions; keep domain-specific abstractions in Domain when appropriate.
- DoViFixer.Infrastructure implements the inner layers' abstractions. Neither Domain nor Application may reference Infrastructure, Console, or App.
- Keep Prism navigation, regions, dialogs, and ViewModels in DoViFixer.App. Common.Wpf must remain reusable presentation infrastructure.
- ViewModels and console commands call application services; they must not implement conversion rules or low-level tool/file operations.
- Neither DoViFixer.Console nor DoViFixer.App references the other. Shared functionality belongs in the shared libraries.
- Keep media analysis, conversion policy, workflow coordination, tool execution, and UI presentation separated.
- Use interfaces at the media-specific boundaries: media probing and processing; file discovery, temporary storage, and output publication; archive storage and repositories where persistence is needed.

## Dependency injection choices

- Use Microsoft.Extensions.DependencyInjection through the .NET Generic Host in DoViFixer.Console.
- Use Prism with DryIoc in DoViFixer.App. Import shared IServiceCollection registrations through the supported Prism integration for the selected package versions.
- Define AddDoViFixerApplication and AddDoViFixerInfrastructure registration extensions in their respective libraries, using Microsoft.Extensions.DependencyInjection.Abstractions.
- Do not create a separate Microsoft provider alongside Prism's container.
- WPF does not provide automatic per-job scopes; introduce explicit operation scopes only when scoped dependencies are introduced.

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
