using DoViFixer.Application.Reports;

namespace DoViFixer.Application.Abstractions;

public interface IReportWriter
{
    Task WriteAsync(string destination, ReportSnapshot snapshot, CancellationToken cancellationToken);
}
