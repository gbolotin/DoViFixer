# DoViFixer WPF mockups

Standalone .NET 10 WPF prototype. Open `DoViFixer.Mockup/DoViFixer.Mockup.csproj` in Visual Studio, or run from the repository root:

```powershell
dotnet run --project mockup/DoViFixer.Mockup
```

Use the top selector to switch between Fluent workspace, Studio console, and Guided workflow. File selections and retention choices carry across variants. Scan and inspection return sample messages. Studio and Guided provide timer-driven simulation, cancellation, and reset. Sidebar navigation is a labeled visual preview, not implemented pages.

There are no project references, external packages, native tool calls, media access, or real conversions. `MockWorkspace` owns sample data and disposable simulation state. This prototype does not start production phase 4 or introduce a production DI container.

- `images/`: all three original generated concepts and their prompts; these have illustrative metadata inconsistencies documented in the prompts.
- `DoViFixer.Mockup/Views/`: one XAML view per concept.
- `DoViFixer.Mockup/Styles/`: shared control templates plus three separate theme dictionaries.
- `DoViFixer.Mockup/ViewModels/`: local bindable sample data and commands.
- `rendered/`: actual WPF previews, distinct from the generated concepts.

Export actual XAML previews without opening an interactive window:

```powershell
dotnet run --project mockup/DoViFixer.Mockup -- --render mockup/rendered
```

Only that explicit developer export writes preview PNGs. Sample paths are display text. Simulated progress is illustrative, and no actual verification or deletion occurs.
