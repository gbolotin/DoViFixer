using System.Collections.Immutable;

namespace DoViFixer.Application.Settings;

/// <summary>Application-owned, UI-framework-independent settings for the media table.</summary>
public sealed record MediaListLayout(ImmutableArray<MediaColumnLayout> Columns, string? SortColumn = null, bool Descending = false);

public sealed record MediaColumnLayout(string Id, double? Width);
