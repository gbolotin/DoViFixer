using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;

namespace DoViFixer.Application.Library;

public sealed record LibraryFile(string Fingerprint, FileIdentity Source, MediaAnalysis? Analysis);

public sealed record LibraryEvent(DateTimeOffset When, string Action, string Detail);
