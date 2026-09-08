using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text.Json;
using DoViFixer.Application.Abstractions;

namespace DoViFixer.Infrastructure.Archives;

internal sealed class BackupArchiveStore : IBackupArchiveStore
{
    public async Task WriteAsync(string stagedArchive, ArchiveManifest manifest, ITemporaryWorkspace workspace, CancellationToken cancellationToken)
    {
        await using (var stream = new FileStream(stagedArchive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        await using (var writer = new TarWriter(stream, TarEntryFormat.Pax))
        {
            await using var el = File.OpenRead(workspace.File("el.hevc"));
            await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, "el.hevc") { DataStream = el }, cancellationToken);
            await using var json = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifest));
            await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, "manifest.json") { DataStream = json }, cancellationToken);
        }
        var verified = await ReadCoreAsync(stagedArchive, null, false, cancellationToken);
        if (verified != manifest)
        {
            throw new InvalidDataException("Archive verification did not reproduce the expected manifest.");
        }
    }

    public Task<ArchiveManifest?> ReadAsync(string archive, ITemporaryWorkspace workspace, bool allowLegacy, CancellationToken cancellationToken)
        => ReadCoreAsync(archive, workspace, allowLegacy, cancellationToken);

    private static async Task<ArchiveManifest?> ReadCoreAsync(string archive, ITemporaryWorkspace? workspace, bool allowLegacy, CancellationToken cancellationToken)
    {
        await using var source = File.OpenRead(archive);
        await using var reader = new TarReader(source);
        var names = new HashSet<string>(StringComparer.Ordinal);
        ArchiveManifest? manifest = null;
        string? payloadHash = null;
        long length = 0;
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) ||
                entry.Name is not ("el.hevc" or "manifest.json") || !names.Add(entry.Name) || entry.DataStream is null)
            {
                throw new InvalidDataException("Archive contains an unexpected, duplicate, linked or unsafe entry.");
            }
            if (entry.Length < 0 || entry.Length > source.Length)
            {
                throw new InvalidDataException("Archive entry size is invalid.");
            }
            if (entry.Name == "manifest.json")
            {
                if (entry.Length > 65536)
                {
                    throw new InvalidDataException("Archive manifest exceeds 64 KiB.");
                }
                manifest = await JsonSerializer.DeserializeAsync<ArchiveManifest>(entry.DataStream, cancellationToken: cancellationToken)
                    ?? throw new InvalidDataException("Archive manifest is empty.");
            }
            else
            {
                length = entry.Length;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using FileStream? output = workspace is null ? null
                    : new FileStream(workspace.File("el.hevc"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                var buffer = new byte[1024 * 1024];
                int count;
                long actual = 0;
                while ((count = await entry.DataStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    actual += count;
                    hash.AppendData(buffer, 0, count);
                    if (output is not null)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    }
                }
                if (actual != length || actual == 0)
                {
                    throw new InvalidDataException("Archive enhancement-layer payload is empty or truncated.");
                }
                payloadHash = Convert.ToHexString(hash.GetHashAndReset());
            }
        }
        if (payloadHash is null)
        {
            throw new InvalidDataException("Archive lacks el.hevc.");
        }
        if (manifest is null)
        {
            if (!allowLegacy)
            {
                throw new InvalidDataException("Legacy archive has no pairing evidence. Explicit --allow-legacy-archive is required.");
            }
            return null;
        }
        if (manifest.FormatVersion != 1 || manifest.BaseLayerSha256 is null || manifest.BaseLayerSha256.Length != 64 ||
            !manifest.BaseLayerSha256.All(Uri.IsHexDigit) || length != manifest.EnhancementLayerLength ||
            !string.Equals(payloadHash, manifest.EnhancementLayerSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Archive version, manifest or payload SHA-256 is invalid.");
        }
        return manifest;
    }
}
