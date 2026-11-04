using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShowCue.Models;

/// <summary>Innholdet i en .showcue-fil.</summary>
public sealed class ShowDocument
{
    public int FormatVersion { get; set; } = 1;
    public string Title { get; set; } = Loc.T("Nytt show");
    public ObservableCollection<Cue> Cues { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IgnoreReadOnlyProperties = true,
        Converters = { new JsonStringEnumConverter(), new CueJsonConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public void Save(string path)
    {
        // Skriv til temp-fil først så en krasj midt i lagring ikke ødelegger showet.
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ToJson());
        File.Move(tmp, path, overwrite: true);
    }

    public static ShowDocument Load(string path)
    {
        var doc = JsonSerializer.Deserialize<ShowDocument>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidDataException(Loc.T("Tom eller ugyldig showfil."));
        // Medier som er flyttet sammen med showfila finnes ofte ved siden av den, eller i undermappa «Media».
        var dir = Path.GetDirectoryName(path) ?? "";
        string Locate(string file)
        {
            if (string.IsNullOrEmpty(file) || File.Exists(file)) return file;
            var name = MediaTools.FileName(file);
            foreach (var candidate in new[] { Path.Combine(dir, name), Path.Combine(dir, "Media", name), Path.Combine(dir, "..", "Media", name) })
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            return file;
        }
        foreach (var fc in doc.Cues.OfType<IFileCue>()) fc.FilePath = Locate(fc.FilePath);
        foreach (var pl in doc.Cues.OfType<IFileListCue>())
            if (pl.Files.Any(f => Locate(f) != f)) pl.Files = pl.Files.Select(Locate).ToList();
        return doc;
    }
}

/// Leser og skriver cuer med "type" som første felt. Ved lesing kan "type" stå hvor som helst
/// (Mac-versjonen garanterer ikke rekkefølgen på feltene).
public sealed class CueJsonConverter : JsonConverter<Cue>
{
    private static readonly Dictionary<string, Type> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audio"] = typeof(AudioCue), ["video"] = typeof(VideoCue), ["image"] = typeof(ImageCue), ["pdf"] = typeof(PdfCue),
        ["fade"] = typeof(FadeCue), ["stop"] = typeof(StopCue), ["osc"] = typeof(OscCue), ["midi"] = typeof(MidiCue),
        ["goto"] = typeof(GotoCue), ["playlist"] = typeof(PlaylistCue), ["memo"] = typeof(MemoCue), ["network"] = typeof(NetworkCue),
        ["web"] = typeof(WebCue), ["slideshow"] = typeof(SlideshowCue), ["timecode"] = typeof(TimecodeCue), ["light"] = typeof(LightCue), ["input"] = typeof(InputCue),
    };

    public override bool CanConvert(Type t) => t == typeof(Cue);

    public override Cue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException(Loc.T("Cue må være et objekt"));
        if (!root.TryGetProperty("type", out var t) || t.GetString() is not string name || !Types.TryGetValue(name, out var type))
            throw new JsonException(Loc.T("Ukjent eller manglende cuetype"));
        return (Cue?)root.Deserialize(type, options);
    }

    public override void Write(Utf8JsonWriter writer, Cue value, JsonSerializerOptions options)
    {
        var name = Types.First(kv => kv.Value == value.GetType()).Key;
        using var doc = JsonSerializer.SerializeToDocument(value, value.GetType(), options);
        writer.WriteStartObject();
        writer.WriteString("type", name);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.NameEquals("type")) continue;
            prop.WriteTo(writer);
        }
        writer.WriteEndObject();
    }
}
