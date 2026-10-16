using DoViFixer.Application.Diagnostics;

namespace DoViFixer.Application.Abstractions;

public interface IFileLockInspector
{
    Task<FileLockReport> InspectAsync(string path, CancellationToken cancellationToken);
}

public interface IDiagnosticBundleWriter
{
    Task<DiagnosticExportResult> WriteAsync(string destination, DiagnosticSnapshot snapshot, CancellationToken cancellationToken);
}
