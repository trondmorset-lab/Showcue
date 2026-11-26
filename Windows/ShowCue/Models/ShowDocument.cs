using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShowCue.Models;

/// <summary>Innholdet i en .showcue-fil.</summary>
public sealed class ShowDocument
{
    public int FormatVersion { get; set; } = 1;
    private string _title = Loc.T("Nytt show");
    private ObservableCollection<Cue> _cues = new();

    public string Title { get => _title; set => _title = value ?? ""; }

    /// <summary>Cuene. En ødelagt fil kan ha «null» i stedet for lista eller for en cue: tomme oppføringer fjernes.</summary>
    public ObservableCollection<Cue> Cues
    {
        get => _cues;
        set
        {
            value ??= new();
            for (int i = value.Count - 1; i >= 0; i--)
                if (value[i] is null) value.RemoveAt(i);
            _cues = value;
        }
    }

    private List<string> _scenes = new();

    /// <summary>Scenenavnene i rekkefølge (cuelistene). Tom = én scene uten navn. Hver cue hører til scenen i <see cref="Cue.Scene"/>.
    /// Eldre versjoner av programmet ignorerer feltet og leser alle cuene som én liste.</summary>
    public List<string> Scenes
    {
        get => _scenes;
        set => _scenes = (value ?? new()).Where(n => n != null).Select(SceneLogic.CleanName).Where(n => n.Length > 0)
            .Aggregate(new List<string>(), (l, n) => { if (!l.Any(x => SceneLogic.SameScene(x, n))) l.Add(n); return l; });
    }

    /// <summary>Retter opp scenene etter lasting eller redigering: tar med scener cuene bruker, og gir hver cue sitt kanoniske scenenavn.</summary>
    public void NormalizeScenes() => _scenes = SceneLogic.Normalize(_scenes, Cues);

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
        doc.NormalizeScenes();
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
        ["web"] = typeof(WebCue), ["slideshow"] = typeof(SlideshowCue), ["timecode"] = typeof(TimecodeCue), ["light"] = typeof(LightCue), ["input"] = typeof(InputCue), ["group"] = typeof(GroupCue),
    };

    public override bool CanConvert(Type t) => t == typeof(Cue);

    /// <summary>Typenøkkelen i showfila («audio», «video» …) for en cue.</summary>
    public static string KeyOf(Cue cue) => Types.FirstOrDefault(kv => kv.Value == cue.GetType()).Key ?? "";

    public override Cue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException(Loc.T("Cue må være et objekt"));
        if (!root.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String || !Types.TryGetValue(t.GetString() ?? "", out var type))
            throw new JsonException(Loc.T("Ukjent eller manglende cuetype"));
        try { return (Cue?)root.Deserialize(type, options); }
        catch (JsonException) { return ReadFieldByField(root, type, options); }
    }

    /// <summary>Når ett felt har ugyldig verdi (tekst i et tallfelt, ukjent navn i en liste …) leses de andre feltene
    /// likevel; det ugyldige feltet beholder standardverdien. Én skadet verdi skal ikke gjøre hele showet uåpnelig.</summary>
    private static Cue ReadFieldByField(JsonElement root, Type type, JsonSerializerOptions options)
    {
        var cue = (Cue)Activator.CreateInstance(type)!;
        foreach (var prop in root.EnumerateObject())
        {
            var pi = type.GetProperty(prop.Name, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null || !pi.CanWrite || pi.GetIndexParameters().Length > 0) continue;
            if (pi.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always }) continue;
            try { pi.SetValue(cue, prop.Value.Deserialize(pi.PropertyType, options)); }
            catch (Exception) { /* ugyldig verdi: feltet beholder standardverdien */ }
        }
        return cue;
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
