using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using DoViFixer.Infrastructure.Configuration;
using DoViFixer.Infrastructure.FileSystem;
using DoViFixer.Infrastructure.MediaTools.Processes;

namespace DoViFixer.Infrastructure.Dependencies;

internal sealed class DependencyInstaller(StorageOptions storage, IProcessRunner processes, HttpClient http) : IDependencyInstaller
{
    public async Task<InstallationPlan> PrepareAsync(DependencyReport report, bool allowRepair, CancellationToken cancellationToken)
    {
        var unavailable = new List<string>();
        var items = new List<InstallationItem>();
        bool winget = await WinGetAvailableAsync(cancellationToken);
        foreach (var status in report.Tools.Where(t => t.State != DependencyState.Ready))
        {
            if (status.State != DependencyState.Missing && !allowRepair)
            {
                unavailable.Add($"{status.Tool}: explicit --repair is required to replace an incompatible or unusable configured tool. {status.Diagnostic}");
                continue;
            }
            var package = InstallationCatalog.Packages.SingleOrDefault(p => p.Tools.Contains(status.Tool));
            if (package is null)
            {
                unavailable.Add($"{status.Tool}: no verified installation route for this architecture. Install the official Windows CLI and use 'settings tool'.");
                continue;
            }
            if (items.Any(i => i.Id == package.Id))
            {
                continue;
            }
            bool useWinGet = winget && package.WinGet && await PackageAvailableAsync(package, cancellationToken);
            items.Add(InstallationCatalog.Item(package, storage, useWinGet));
        }
        return new(Guid.NewGuid(), items.AsReadOnly(), unavailable.AsReadOnly());
    }

    public async Task<IReadOnlyList<InstallationOutcome>> InstallAsync(InstallationPlan approvedPlan,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var results = new List<InstallationOutcome>();
        foreach (var item in approvedPlan.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var package = InstallationCatalog.Packages.SingleOrDefault(p => p.Id == item.Id)
                ?? throw new InvalidOperationException("Installation plan contains an unknown package.");
            var expected = InstallationCatalog.Item(package, storage, item.Provider == InstallationProvider.WinGet);
            if (JsonSerializer.Serialize(expected) != JsonSerializer.Serialize(item))
            {
                throw new InvalidOperationException("The installation source, version or scope changed. Prepare and approve a new plan.");
            }
            progress?.Report(new(approvedPlan.Id, $"Installing {item.Id} {item.Version}"));
            try
            {
                if (item.Provider == InstallationProvider.WinGet)
                {
                    if (!await PackageAvailableAsync(package, cancellationToken))
                    {
                        throw new IOException("Approved WinGet package/version is no longer available; prepare a new installation plan.");
                    }
                    await processes.RunAsync(new(WinGetPath(), new[] { "install", "--id", item.Id, "--exact", "--version", item.Version,
                        "--source", "winget", "--scope", "user", "--installer-type", "zip", "--architecture", "x64",
                        "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" }, Timeout: TimeSpan.FromMinutes(30)), cancellationToken);
                }
                else
                {
                    await InstallZipAsync(item, cancellationToken);
                }
                results.Add(new(item.Id, true, "Installation finished; independent executable validation follows."));
            }
            catch (OperationCanceledException)
            {
                throw new OperationCanceledException("Installation cancelled. Partial package-manager changes may remain; run dependencies check before retrying.", cancellationToken);
            }
            catch (Exception ex)
            {
                results.Add(new(item.Id, false, ex.Message + " Use the official source shown in the plan, then configure the executable path."));
            }
        }
        return results.AsReadOnly();
    }

    private async Task InstallZipAsync(InstallationItem item, CancellationToken cancellationToken)
    {
        FileOperations.RejectReparsePoints(storage.RootDirectory);
        if (Directory.Exists(item.Destination))
        {
            throw new IOException($"Managed destination already exists: {item.Destination}. Inspect it and configure its executable path; it will not be overwritten.");
        }
        string parent = Path.GetDirectoryName(item.Destination)!;
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, ".install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            string zip = Path.Combine(staging, "download.zip");
            using var response = await http.GetAsync(item.Source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var output = new FileStream(zip, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await response.Content.CopyToAsync(output, cancellationToken);
            }
            await using (var input = File.OpenRead(zip))
            {
                string digest = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
                if (!string.Equals(digest, item.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Downloaded archive failed the pinned SHA-256 integrity check.");
                }
            }
            string extracted = Path.Combine(staging, "extracted");
            Directory.CreateDirectory(extracted);
            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string target = Path.GetFullPath(Path.Combine(extracted, entry.FullName));
                    if (!target.StartsWith(extracted + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("Unsafe ZIP entry path.");
                    }
                    if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using var source = entry.Open();
                    await using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                    await source.CopyToAsync(destination, cancellationToken);
                }
            }
            foreach (var tool in item.Tools)
            {
                if (!Directory.EnumerateFiles(extracted, ToolDefinitions.Executable(tool), SearchOption.AllDirectories).Any())
                {
                    throw new InvalidDataException($"The archive lacks {ToolDefinitions.Executable(tool)}.");
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            FileOperations.RejectReparsePoints(parent);
            Directory.Move(extracted, item.Destination);
        }
        finally
        {
            FileOperations.RejectReparsePoints(staging);
            Directory.Delete(staging, recursive: true);
        }
    }

    private static string WinGetPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
    private async Task<bool> WinGetAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            await processes.RunAsync(new(WinGetPath(), new[] { "--version" }, Timeout: TimeSpan.FromSeconds(8)), cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
    private async Task<bool> PackageAvailableAsync(ToolPackage package, CancellationToken cancellationToken)
    {
        try
        {
            var result = await processes.RunAsync(new(WinGetPath(), new[] { "show", "--id", package.Id, "--exact", "--version", package.Version,
                "--source", "winget", "--accept-source-agreements", "--disable-interactivity" }, Timeout: TimeSpan.FromSeconds(30)), cancellationToken);
            return result.Output.Contains(package.Url, StringComparison.OrdinalIgnoreCase)
                && result.Output.Contains(package.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}
