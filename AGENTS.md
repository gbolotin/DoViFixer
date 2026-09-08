# DoViFixer Codex instructions

## Project architecture

DoViFixer starts as a native Windows C#/.NET console application. A later WPF application will use MVVM and Prism. Both applications share the same domain, application, and infrastructure code.

The solution architecture and implementation plan are documented in [docs/architecture.md](docs/architecture.md).

Main projects:

- DoViFixer.Domain: domain models, business rules, state, and core abstractions.
- DoViFixer.Application: shared use cases, workflow coordination, requests, results, progress models, and interfaces for external capabilities.
- DoViFixer.Infrastructure: repositories, persistence, configuration, file system, and other infrastructure.
- DoViFixer.Console: console entry point, command parsing, interaction, rendering, and dependency injection composition.
- DoViFixer.App: future WPF entry point, views, ViewModels, commands, dialogs, Prism navigation, and dependency injection composition.
- DoViFixer.Common.Wpf: optional shared WPF utilities and reusable UI infrastructure; add when there is concrete reuse.

## Layering rules

- Keep domain logic in DoViFixer.Domain.
- Do not put file system access, external process execution, console interaction, WPF, or Prism-specific code in Domain.
- Keep use-case coordination in Application. Application depends on Domain, not Infrastructure or either UI project.
- Put interfaces needed by application workflows in Application/Abstractions; keep domain-specific abstractions in Domain when appropriate.
- Infrastructure implements the inner layers' abstractions and may reference Application and Domain. Inner layers must not reference Infrastructure.
- Keep Prism navigation, regions, dialogs, and ViewModels in DoViFixer.App. Common.Wpf must remain reusable presentation infrastructure.
- ViewModels should coordinate UI state, commands, validation, and navigation.
- ViewModels and console commands call application services; they must not implement conversion rules or low-level tool/file operations.
- Executable projects may reference Infrastructure for startup registration. Runtime UI code uses injected application services.
- Neither executable project references the other. Shared functionality belongs in the shared libraries.

## Dependency injection

- Use Microsoft.Extensions.DependencyInjection through the .NET Generic Host in DoViFixer.Console.
- Use Prism with DryIoc in DoViFixer.App. Import shared IServiceCollection registrations through the supported Prism integration for the selected package versions.
- Define AddDoViFixerApplication and AddDoViFixerInfrastructure registration extensions in their respective libraries, using Microsoft.Extensions.DependencyInjection.Abstractions.
- Keep container-specific configuration and root resolution in each executable's Composition folder and startup code.
- Create one root container per application process. Do not call BuildServiceProvider inside registration extensions or create a separate Microsoft provider alongside Prism's container.
- Prefer constructor injection. Do not inject IServiceProvider, IContainerProvider, or a concrete container into business services or ViewModels to locate dependencies.
- Domain must have no DI package dependency. Application services must not depend on a concrete DI container; its registration extensions may use DI abstractions.
- Default workflow services, commands, and ViewModels to transient. Use singleton services only when their shared state is intentional and thread-safe.
- Keep operation state, cancellation sources, processes, and temporary workspaces per operation. Dispose owned resources on success, failure, and cancellation.
- Use explicit operation scopes only when scoped dependencies are introduced; WPF does not provide automatic per-job scopes.

## SOLID rules for this project

- Use interfaces at architectural boundaries:
  - media probing and processing
  - file discovery, temporary storage, and output publication
  - archive storage and repositories where persistence is needed
  - configuration
  - logging
  - update retrieval
  - time/clock access
- Prefer existing .NET abstractions such as ILogger<T> and TimeProvider where suitable instead of adding redundant wrappers.
- Do not create interfaces for every class automatically.
- Prefer small focused services over large manager classes.
- Keep media analysis, conversion policy, workflow coordination, tool execution, and UI presentation separated.
- Use typed requests, results, and progress models. Shared services must not prompt the user or produce formatted terminal output.
- Support CancellationToken and UI-independent progress reporting for long-running operations.

## Native dependencies

- Detect FFmpeg (ffmpeg.exe and ffprobe.exe), MKVToolNix (mkvmerge.exe and mkvextract.exe), MediaInfo CLI (mediainfo.exe), and dovi_tool (dovi_tool.exe).
- Automatically check the dependencies required by a requested media operation before starting it. Keep help, argument parsing, and DI registration free of dependency probes and installation.
- Offer automatic installation of missing dependencies. Present the exact tools, sources, versions, installation scope, and any elevation requirement before asking for consent. An explicit dependency-install command or installation-specific unattended option may provide that consent; conversion confirmation alone does not.
- Reuse approval for the same installation plan; do not prompt again for each file in a batch. Do not silently install or upgrade unrelated software.
- Keep dependency detection and installation coordination in Application, installer/download/process details in Infrastructure, and prompts/progress in Console or App. Register these services through the existing DI extensions.
- Re-detect and validate executables after installation, persist selected tool paths in user settings, and apply them immediately in the current process before resuming the requested operation.
- If installation fails, is declined, or is unavailable, report the affected tools and actionable recovery steps; do not start an operation with unmet requirements.

## C# style

- Always use braces for if/else/for/foreach/while blocks.
- Use camelCase for private fields.
- Use async/await correctly.
- Do not block async code with .Result, .Wait(), or .GetAwaiter().GetResult() unless there is a very strong reason.
- Prefer immutable models where practical.

## Testing

- New domain and service logic should be unit-testable.
- Prefer small focused tests.
- Do not add abstractions only for tests unless they also improve design.
- Verify container registrations, service lifetimes, and disposal without running media conversions.
- Keep tests that require native tools and media fixtures separate from fast unit tests.

## Refactoring expectations

When reviewing or refactoring DoViFixer code, look especially for:

- ViewModels with too much business logic.
- Direct dependencies on concrete infrastructure.
- Native-tool command lines and output parsing leaking outside Infrastructure.
- Duplicated conversion logic between console commands and ViewModels.
- Service locator usage, duplicate root containers, and shared mutable operation state.
- Large services with multiple responsibilities.
- Too-large interfaces.
- Code that is hard to unit test.
- Over-engineering.
