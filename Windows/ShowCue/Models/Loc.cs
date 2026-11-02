using System.Globalization;
using System.IO;
using System.Text.Json;

namespace ShowCue.Models;

/// <summary>Språk i brukerflaten. Teksten i koden er norsk og er samtidig nøkkelen i oversettelsesfila (i18n/en.json).</summary>
public static class Loc
{
    /// <summary>Språkene programmet kan vises på (kode, navn).</summary>
    public static readonly (string Code, string Name)[] Languages = { ("", "Samme som systemet / Same as system"), ("nb", "Norsk"), ("en", "English") };

    private static Dictionary<string, string>? _table;

    /// <summary>Gjeldende språk: «nb» eller «en».</summary>
    public static string Language { get; private set; } = "nb";

    /// <summary>Velger språk. Tom kode betyr systemets språk (norsk på norske maskiner, ellers engelsk).</summary>
    public static void Use(string? code, Func<Stream?>? english = null)
    {
        Language = Resolve(code, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        _table = null;
        if (Language != "en") return;
        using var stream = english?.Invoke() ?? typeof(Loc).Assembly.GetManifestResourceStream("en.json");
        if (stream != null) _table = ReadTable(stream);
    }

    /// <summary>Leser en oversettelsesfil (norsk tekst → oversatt tekst).</summary>
    public static Dictionary<string, string> ReadTable(Stream json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();

    public static string Resolve(string? code, string systemLanguage) => code switch
    {
        "nb" or "en" => code,
        _ => systemLanguage is "nb" or "nn" or "no" ? "nb" : "en",
    };

    /// <summary>Teksten på gjeldende språk.</summary>
    public static string T(string text) =>
        _table != null && _table.TryGetValue(text, out var t) && t.Length > 0 ? t : text;

    /// <summary>Tekst med {0}, {1} … fylt inn.</summary>
    public static string F(string format, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(format), args);
}
