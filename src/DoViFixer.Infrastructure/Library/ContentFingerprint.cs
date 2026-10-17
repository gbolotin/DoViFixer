using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using DoViFixer.Domain.Media;

namespace DoViFixer.Infrastructure.Library;

/// <summary>Versioned SHA-256 of the length and three 8 MiB content windows, independent of the path.</summary>
internal static class ContentFingerprint
{
    private const int windowSize = 8 * 1024 * 1024;

    public static async Task<string> ComputeAsync(FileIdentity source, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        CheckIdentity(source, stream.Length);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] length = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(length, source.Length);
        hash.AppendData(length);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            if (source.Length <= 3L * windowSize)
            {
                await AppendAsync(0, source.Length);
            }
            else
            {
                await AppendAsync(0, windowSize);
                await AppendAsync((source.Length - windowSize) / 2, windowSize);
                await AppendAsync(source.Length - windowSize, windowSize);
            }
            cancellationToken.ThrowIfCancellationRequested();
            CheckIdentity(source, stream.Length);
            return "q1:" + Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        async Task AppendAsync(long offset, long remaining)
        {
            stream.Position = offset;
            while (remaining > 0)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException("The source changed while its fingerprint was being calculated.");
                }
                hash.AppendData(buffer, 0, read);
                remaining -= read;
            }
        }
    }

    private static void CheckIdentity(FileIdentity source, long length)
    {
        if (length != source.Length || File.GetLastWriteTimeUtc(source.Path) != source.LastWriteUtc)
        {
            throw new IOException("The source changed while its fingerprint was being calculated.");
        }
    }
}
