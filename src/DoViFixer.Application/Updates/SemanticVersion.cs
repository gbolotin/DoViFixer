namespace DoViFixer.Application.Updates;
/// <summary>SemVer 2.0 precedence for release tags ("v1.2.3") and build versions ("0.1.0-dev+abc1234"). Build metadata is ignored.</summary>
public readonly record struct SemanticVersion(int Major, int Minor, int Patch, string PreRelease) : IComparable<SemanticVersion>
{
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        int plus = value.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            value = value[..plus];
        }

        int dash = value.IndexOf('-', StringComparison.Ordinal);
        string preRelease = dash < 0 ? "" : value[(dash + 1)..];
        string[] core = (dash < 0 ? value : value[..dash]).Split('.');
        if (core.Length != 3 || (dash >= 0 && preRelease.Length == 0) || !int.TryParse(core[0], out int major) || !int.TryParse(core[1], out int minor) || !int.TryParse(core[2], out int patch) || major < 0 || minor < 0 || patch < 0)
        {
            return false;
        }

        version = new(major, minor, patch, preRelease);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        int result = Major.CompareTo(other.Major);
        if (result == 0)
        {
            result = Minor.CompareTo(other.Minor);
        }

        if (result == 0)
        {
            result = Patch.CompareTo(other.Patch);
        }

        return result != 0 ? result : ComparePreRelease(PreRelease, other.PreRelease);
    }

    // A release outranks its pre-releases; identifiers compare numerically when both are numbers, otherwise ordinally, and numbers rank lower.
    private static int ComparePreRelease(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return right.Length.CompareTo(left.Length);
        }

        string[] a = left.Split('.');
        string[] b = right.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            bool aNumber = ulong.TryParse(a[i], out ulong x);
            bool bNumber = ulong.TryParse(b[i], out ulong y);
            int result = aNumber && bNumber ? x.CompareTo(y) : aNumber ? -1 : bNumber ? 1 : string.CompareOrdinal(a[i], b[i]);
            if (result != 0)
            {
                return Math.Sign(result);
            }
        }

        return a.Length.CompareTo(b.Length);
    }
}
