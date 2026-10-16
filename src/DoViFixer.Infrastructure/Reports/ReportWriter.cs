using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Reports;

namespace DoViFixer.Infrastructure.Reports;

internal sealed class ReportWriter(IOutputPublisher publisher) : IReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task WriteAsync(string destination, ReportSnapshot snapshot, CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(destination).ToLowerInvariant();
        if (extension is not (".csv" or ".json" or ".txt"))
        {
            throw new ArgumentException("Report format must be .csv, .json, or .txt.", nameof(destination));
        }

        await using var stage = publisher.Stage(destination);
        await using (var stream = new FileStream(stage.Path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
        {
            if (extension == ".json")
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
            }
            else
            {
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                if (extension == ".csv")
                {
                    await WriteCsvAsync(writer, snapshot, cancellationToken);
                }
                else
                {
                    await WriteTextAsync(writer, snapshot, cancellationToken);
                }
            }
        }

        await stage.PublishAsync(cancellationToken);
    }

    private static async Task WriteCsvAsync(TextWriter writer, ReportSnapshot snapshot, CancellationToken token)
    {
        await writer.WriteLineAsync("Path,Status,Error,SourceBytes,SourceLastWriteUtc,Profile,Layer,Method,Verdict,Explanation,EvidenceComplete,RpuFrames,SuccessfulSamples,RequestedSamples,MaxCllNits,RpuPeakNits,BaseLayerPeakNits,ComparedFrames,ExpandedFrames,MetadataFreeTailFrames,EvidenceLimitations,OperationStatus,OperationMessage,OutputPath");
        foreach (var entry in snapshot.Entries)
        {
            token.ThrowIfCancellationRequested();
            string?[] fields = [entry.Path, entry.Status, entry.Error, Number(entry.SourceBytes), entry.SourceLastWriteUtc?.ToString("O", CultureInfo.InvariantCulture),
                entry.Profile?.ToString(), entry.Layer?.ToString(), entry.Method?.ToString(), entry.Verdict?.ToString(), entry.Explanation,
                entry.EvidenceComplete?.ToString(), Number(entry.RpuFrames), Number(entry.SuccessfulSamples), Number(entry.RequestedSamples),
                Number(entry.MaxCllNits), Number(entry.RpuPeakNits), Number(entry.BaseLayerPeakNits), Number(entry.ComparedFrames),
                Number(entry.ExpandedFrames), Number(entry.MetadataFreeTailFrames), entry.EvidenceLimitations, entry.OperationStatus?.ToString(),
                entry.OperationMessage, entry.OutputPath];
            await writer.WriteLineAsync(string.Join(',', fields.Select(CsvCell)));
        }
    }

    private static string? Number<T>(T? value) where T : struct, IFormattable => value?.ToString(null, CultureInfo.InvariantCulture);

    private static string CsvCell(string? value)
    {
        string text = value ?? "";
        // Quoting alone does not stop spreadsheet applications from evaluating a formula.
        if (text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' || text.StartsWith('\t') || text.StartsWith('\r'))
        {
            text = "'" + text;
        }

        return '"' + text.Replace("\"", "\"\"") + '"';
    }

    private static async Task WriteTextAsync(TextWriter writer, ReportSnapshot snapshot, CancellationToken token)
    {
        await writer.WriteLineAsync($"Report schema {snapshot.SchemaVersion} | {snapshot.CreatedAtUtc:O} | {snapshot.Entries.Count} files");
        foreach (var entry in snapshot.Entries)
        {
            token.ThrowIfCancellationRequested();
            await writer.WriteLineAsync();
            await writer.WriteLineAsync($"{entry.Path}\n  Status: {entry.Status}");
            if (entry.Error is not null)
            {
                await writer.WriteLineAsync($"  Error: {entry.Error}");
            }
            if (entry.Verdict is not null)
            {
                await writer.WriteLineAsync($"  Profile: {entry.Profile}; layer: {entry.Layer}; method: {entry.Method}; verdict: {entry.Verdict}");
                await writer.WriteLineAsync($"  Explanation: {entry.Explanation}");
                await writer.WriteLineAsync($"  Evidence complete: {entry.EvidenceComplete}; RPU frames: {entry.RpuFrames}; RPU peak: {Number(entry.RpuPeakNits) ?? "unavailable"} nits; MaxCLL: {Number(entry.MaxCllNits) ?? "unavailable"} nits");
                await writer.WriteLineAsync($"  Samples: {entry.SuccessfulSamples}/{entry.RequestedSamples}; decoded frames compared: {Number(entry.ComparedFrames) ?? "unavailable"}; frames with brightness expansion: {Number(entry.ExpandedFrames) ?? "unavailable"}; base-layer peak: {Number(entry.BaseLayerPeakNits) ?? "unavailable"} nits");
                if (entry.MetadataFreeTailFrames is not null)
                {
                    await writer.WriteLineAsync($"  Verified metadata-free tail: {entry.MetadataFreeTailFrames} frames");
                }
                if (entry.EvidenceLimitations is not null)
                {
                    await writer.WriteLineAsync($"  Limits: {entry.EvidenceLimitations}");
                }
            }
            if (entry.OperationStatus is not null)
            {
                await writer.WriteLineAsync($"  Operation: {entry.OperationStatus}; {entry.OperationMessage}");
            }
        }
    }
}
