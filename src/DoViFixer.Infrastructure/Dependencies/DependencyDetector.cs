using System.Text.RegularExpressions;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Infrastructure.Configuration;
using DoViFixer.Infrastructure.MediaTools.Processes;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.Dependencies;

internal sealed class DependencyDetector(ISettingsStore settings, StorageOptions storage, IProcessRunner processes, ILogger<DependencyDetector> logger) : IDependencyDetector
{
    public async Task<DependencyReport> DetectAsync(IReadOnlyList<NativeTool> tools, CancellationToken cancellationToken)
    {
        var snapshot = await settings.ReadAsync(cancellationToken);
        var results = new List<DependencyStatus>();
        foreach (var tool in tools.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot.ToolPaths.TryGetValue(tool, out string? configured))
            {
                var result = await ValidatePathAsync(tool, configured, cancellationToken);
                results.Add(result.State == DependencyState.Ready ? result : result with
                {
                    Diagnostic = $"Configured path is invalid: {configured}. {result.Diagnostic} Use 'settings tool' or explicitly approve 'dependencies install --repair'."
                });
                continue;
            }
            DependencyStatus? failure = null;
            DependencyStatus? ready = null;
            foreach (string candidate in Candidates(tool))
            {
                var result = await ValidatePathAsync(tool, candidate, cancellationToken);
                if (result.State == DependencyState.Ready)
                {
                    ready = result;
                    break;
                }
                failure ??= result;
            }
            results.Add(ready ?? failure ?? new(tool, DependencyState.Missing, null, null, $"{ToolDefinitions.Executable(tool)} was not found."));
        }
        return new(results.AsReadOnly());
    }

    public async Task<DependencyStatus> ValidatePathAsync(NativeTool tool, string path, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            return new(tool, DependencyState.Unusable, path, null, "Executable path must be absolute and exist.");
        }
        try
        {
            if (tool == NativeTool.MediaInfo && IsGuiExecutable(path))
            {
                return new(tool, DependencyState.Unusable, path, null, "This is a GUI-only MediaInfo executable; install MediaInfo CLI.");
            }
            string argument = tool is NativeTool.FFmpeg or NativeTool.FFprobe ? "-version" : "--version";
            var result = await processes.RunAsync(new(path, new[] { argument }, Timeout: TimeSpan.FromSeconds(8)), cancellationToken);
            string output = result.Output + result.Error;
            string identity = tool switch
            {
                NativeTool.MediaInfo => "MediaInfo Command line",
                NativeTool.DoviTool => "dovi_tool",
                _ => Path.GetFileNameWithoutExtension(ToolDefinitions.Executable(tool))
            };
            if (!output.Contains(identity, StringComparison.OrdinalIgnoreCase))
            {
                return new(tool, DependencyState.Unusable, path, null, "Executable did not identify as the expected command-line tool.");
            }
            var versionMatch = Regex.Match(output, @"(?i)(?:version\s+|\bv|dovi_tool\s+)(\d+\.\d+(?:\.\d+)?)");
            if (!versionMatch.Success || !Version.TryParse(versionMatch.Groups[1].Value, out var version))
            {
                return new(tool, DependencyState.Unusable, path, null, "Could not establish a supported version.");
            }
            if (version.Major < ToolDefinitions.MinimumMajor(tool))
            {
                return new(tool, DependencyState.Incompatible, path, version.ToString(), $"Requires version {ToolDefinitions.MinimumMajor(tool)} or later.");
            }
            if (tool == NativeTool.DoviTool)
            {
                var help = await processes.RunAsync(new(path, new[] { "--help" }, Timeout: TimeSpan.FromSeconds(8)), cancellationToken);
                if (!new[] { "extract-rpu", "convert", "demux", "mux", "export", "remove" }.All(help.Output.Contains))
                {
                    return new(tool, DependencyState.Incompatible, path, version.ToString(), "Required dovi_tool subcommands are unavailable.");
                }
            }
            return new(tool, DependencyState.Ready, Path.GetFullPath(path), version.ToString(), "Validated command-line executable.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Dependency validation failed for {Tool} at {ToolPath}", tool, path);
            return new(tool, DependencyState.Unusable, path, null, ex.Message);
        }
    }

    private static bool IsGuiExecutable(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 64 || reader.ReadUInt16() != 0x5A4D)
        {
            return false;
        }
        stream.Position = 0x3C;
        int peOffset = reader.ReadInt32();
        if (peOffset < 0 || peOffset + 94L > stream.Length)
        {
            return false;
        }
        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550)
        {
            return false;
        }
        stream.Position = peOffset + 24 + 68;
        return reader.ReadUInt16() == 2;
    }

    private IEnumerable<string> Candidates(NativeTool tool)
    {
        string executable = ToolDefinitions.Executable(tool);
        var found = new List<string>();
        foreach (string root in new[] { storage.ToolsDirectory, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages") })
        {
            if (Directory.Exists(root))
            {
                found.AddRange(Directory.EnumerateFiles(root, executable, new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = 5,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                }).OrderDescending(StringComparer.OrdinalIgnoreCase));
            }
        }
        string environmentPath = string.Join(Path.PathSeparator, Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User), Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
        foreach (string directory in environmentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string path = Path.Combine(Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"')), executable);
            if (Path.IsPathFullyQualified(path) && File.Exists(path))
            {
                found.Add(path);
            }
        }
        foreach (string programFiles in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            foreach (string directory in new[] { "MKVToolNix", "MediaInfo CLI", "MediaInfo", "ffmpeg\\bin", "dovi_tool" })
            {
                string path = Path.Combine(programFiles, directory, executable);
                if (File.Exists(path))
                {
                    found.Add(path);
                }
            }
        }
        return found.Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
