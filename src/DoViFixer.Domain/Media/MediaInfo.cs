namespace DoViFixer.Domain.Media;

public enum DolbyVisionProfile { Unknown, None, Profile5, Profile7, Profile81, Other }
public enum EnhancementLayer { Unknown, None, Mel, Fel }

public sealed record FileIdentity(string Path, long Length, DateTime LastWriteUtc);

public sealed record MediaTrack(int Id, string Type, string Codec, string Language, string Name,
    bool Default, bool Forced, string? Uid);

public sealed record MediaInfo(FileIdentity Source, DolbyVisionProfile Profile, string VideoCodec,
    int VideoTrackId, int Width, int Height, long? FrameCount, double? FramesPerSecond,
    double? DurationSeconds, long VideoDelayNanoseconds, double? MaxCll,
    IReadOnlyList<MediaTrack> Tracks, int AttachmentCount, int ChapterCount, string? Title,
    string IdentificationJson);
