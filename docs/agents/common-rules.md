# Common development rules

These rules apply throughout the repository. Application-specific project names, framework choices, and workflow requirements belong in [DoViFixer-specific rules](dovifixer-rules.md).

## Architecture and layering

- Keep domain logic in the domain layer. Do not put file system access, external process execution, console interaction, UI frameworks, or container-specific code there.
- Keep use-case coordination in the application layer. It depends on the domain layer, not infrastructure or executable UI projects.
- Put interfaces required by application workflows in the application layer's Abstractions folder; keep domain-specific abstractions in the domain layer when appropriate.
- Infrastructure implements the inner layers' abstractions and may reference them. Inner layers must not reference infrastructure.
- ViewModels coordinate UI state, commands, validation, and navigation.
- ViewModels and console commands call application services; they must not implement business rules or low-level tool/file operations.
- Executable projects may reference infrastructure for startup registration. Runtime UI code uses injected application services.
- Executable projects must not reference one another. Shared functionality belongs in shared libraries.
- Shared UI utilities must remain reusable presentation infrastructure; extract them only when concrete reuse exists.

## Dependency injection and resource ownership

- Keep container-specific configuration and root resolution in each executable's Composition folder and startup code.
- Create one root container per application process. Do not call BuildServiceProvider inside registration extensions or create a second provider alongside the chosen container.
- Prefer constructor injection. Do not inject IServiceProvider, IContainerProvider, or a concrete container into business services or ViewModels to locate dependencies.
- The domain layer must have no DI package dependency. Application services must not depend on a concrete DI container; registration extensions may use DI abstractions.
- Default workflow services, commands, and ViewModels to transient. Use singleton services only when their shared state is intentional and thread-safe.
- Keep operation state, cancellation sources, processes, and temporary workspaces per operation. Dispose owned resources on success, failure, and cancellation.
- Use explicit operation scopes only when scoped dependencies are introduced. Do not assume the UI framework provides automatic per-job scopes.

## SOLID and service design

- Use interfaces at architectural boundaries, including external processing, file operations, persistence, configuration, logging, update retrieval, and time/clock access.
- Prefer existing .NET abstractions such as ILogger<T> and TimeProvider where suitable instead of adding redundant wrappers.
- Do not create interfaces for every class automatically.
- Prefer small focused services over large manager classes.
- Keep business policy, workflow coordination, external-tool execution, and UI presentation separated.
- Use typed requests, results, and progress models. Shared services must not prompt the user or produce formatted terminal output.
- Support CancellationToken and UI-independent progress reporting for long-running operations.

## C# style

- Always use braces for if/else/for/foreach/while blocks.
- Use camelCase for private fields.
- Use async/await correctly.
- Do not block async code with .Result, .Wait(), or .GetAwaiter().GetResult() unless there is a very strong reason.
- Prefer immutable models where practical.

## XAML style

- Put only one tag on each line, including closing tags.
- Keep the opening tag name and its first attribute on the same line.
- A tag with up to two attributes may remain on one line. Never put more than two attributes on a line.
- For a tag with more than two attributes, keep the first attribute on the opening line and put each remaining attribute on its own line, aligned with the first attribute.
- Preserve nesting indentation. Keep `>` or `/>` at the end of the final attribute line.

```xml
<StackPanel Grid.Row="1"
            Orientation="Horizontal"
            Margin="0,12,0,0">
    <Button Content="Convert"
            Command="{Binding ConvertCommand}"
            Margin="0,0,10,0" />
    <TextBlock Text="{Binding Status}" TextWrapping="Wrap" />
</StackPanel>
```

## Testing

- New domain and service logic should be unit-testable.
- Prefer small focused tests.
- Do not add abstractions only for tests unless they also improve design.
- Verify container registrations, service lifetimes, and disposal without running real external workflows.
- Keep tests that require native tools or external fixtures separate from fast unit tests.

## Refactoring expectations

When reviewing or refactoring code, look especially for:

- ViewModels with too much business logic.
- Direct dependencies on concrete infrastructure.
- External-tool command lines and output parsing leaking outside infrastructure.
- Duplicated business logic between executable UIs.
- Service locator usage, duplicate root containers, and shared mutable operation state.
- Large services with multiple responsibilities.
- Too-large interfaces.
- Code that is hard to unit test.
- Over-engineering.
