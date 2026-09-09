using System.Globalization;

namespace DoViFixer.Infrastructure.MediaTools;

internal static class TrackLanguage
{
    private static readonly IReadOnlyDictionary<string, string> aliases = CreateAliases();

    internal static bool Equivalent(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string language)
    {
        int separator = language.IndexOf('-');
        string primary = separator < 0 ? language : language[..separator];
        string suffix = separator < 0 ? "" : language[separator..];
        return aliases.TryGetValue(primary, out string? canonical) ? canonical + suffix : language;
    }

    private static IReadOnlyDictionary<string, string> CreateAliases()
    {
        // ISO 639-2 bibliographic codes used by legacy Matroska language fields.
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["alb"] = "sq", ["arm"] = "hy", ["baq"] = "eu", ["bur"] = "my",
            ["chi"] = "zh", ["cze"] = "cs", ["dut"] = "nl", ["fre"] = "fr",
            ["geo"] = "ka", ["ger"] = "de", ["gre"] = "el", ["ice"] = "is",
            ["mac"] = "mk", ["mao"] = "mi", ["may"] = "ms", ["per"] = "fa",
            ["rum"] = "ro", ["slo"] = "sk", ["tib"] = "bo", ["wel"] = "cy"
        };
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
        {
            if (culture.Name.Length == 2 && culture.ThreeLetterISOLanguageName.Length == 3)
            {
                result.TryAdd(culture.ThreeLetterISOLanguageName, culture.Name);
            }
        }
        return result;
    }
}
