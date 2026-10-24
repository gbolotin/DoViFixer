namespace DoViFixer.Application.Abstractions;

/// <summary>
/// The settings and media library a previous installation left in the data folder. Setup asks the new version which of
/// them it can read, so it keeps or removes them before the app starts.
/// </summary>
public interface IStoredUserData
{
    Task<StoredUserDataState> InspectAsync(CancellationToken cancellationToken);
    Task RemoveAsync(UserDataParts parts, CancellationToken cancellationToken);
}

[Flags]
public enum UserDataParts
{
    None = 0,
    /// <summary>settings.json and presets.json.</summary>
    Settings = 1,
    /// <summary>library.db and the analysis and frame cache.</summary>
    Library = 2
}

/// <summary>The stored parts this version can read, and those it cannot.</summary>
public sealed record StoredUserDataState(UserDataParts Compatible, UserDataParts Incompatible);
