using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NAudio.Wave;
using NAudio.Wave.Asio;
using Cue = ShowCue.Models.Cue;
using ShowCue.Engine;
using ShowCue.Models;

// Tester av samspillet mellom funksjonene som ble slått sammen i 0.27.0: scener, øvelsesmodus, forhåndslasting, routingforhåndsvalg,
// nye showfelt, klokke-triggere, innstillinger, ASIO-innganger, showlogg og nettsiden (ekte sockets). Se README.md.
public static partial class Program
{
    // ---------- scener x øvelsesmodus x forhåndslasting ----------
    static readonly string[] SceneNames = { "A", "B", "C" };

    static Cue RandomFlowCue(int index, bool multiScene)
    {
        Cue c = Rng.Next(6) switch
        {
            0 => new StopCue { Target = Rng.Next(8).ToString() },
            1 => new MemoCue(),
            2 => new GroupCue { Targets = Rng.Next(8) + "," + Rng.Next(8) },
            3 => new GotoCue { Target = Rng.Next(8).ToString(), TargetScene = Rng.Next(3) == 0 ? SceneNames[Rng.Next(3)] : "" },
            _ => new AudioCue { FilePath = "f" + index + ".wav" },
        };
        c.Number = Rng.Next(8).ToString();
        c.Scene = multiScene ? SceneNames[Rng.Next(SceneNames.Length)] : "";
        c.Disabled = Rng.Next(5) == 0;
        c.SkipInRehearsal = Rng.Next(3) == 0;
        c.ContinueMode = (ContinueMode)Rng.Next(3);
        return c;
    }

    /// <summary>Simulerer GO gjennom hver scene (med og uten øvelsesmodus) og sjekker at markøren, auto-fortsett-kjeden og forhåndslastingen er enige.</summary>
    static void SceneRehearsalFlow()
    {
        for (int i = 0; i < 3000; i++)
        {
            CurrentCase = $"runde {i}";
            bool multi = Rng.Next(3) != 0;
            var cues = new List<Cue>();
            for (int n = Rng.Next(0, 16); n > 0; n--) cues.Add(RandomFlowCue(cues.Count, multi));
            bool rehearsal = Rng.Next(2) == 0;
            string Dump() => string.Join(" ", cues.Select(c => $"{c.GetType().Name[..3]}:{c.Scene}:{c.Number}{(c.Disabled ? "x" : "")}{(c.SkipInRehearsal ? "s" : "")}{(int)c.ContinueMode}")) + (rehearsal ? " R" : "");
            const string S = "Scener x øvelsesmodus";
            try
            {
                var scenes = SceneLogic.Normalize(null, cues);
                foreach (var scene in scenes.Count == 0 ? new List<string> { "" } : scenes)
                {
                    var list = SceneLogic.CuesIn(cues, scene);
                    var first = SceneLogic.FirstEnabled(cues, scene);
                    if (first != null && first.Disabled) Bug(S, "FirstEnabled gir en deaktivert cue", Dump());
                    int head = first == null ? -1 : list.IndexOf(first);
                    int guard = 0;
                    while (head >= 0 && head < list.Count)
                    {
                        if (++guard > list.Count + 2) { Bug(S, "GO-løkka stopper ikke", Dump()); break; }
                        int idx = Rehearsal.GoIndex(list, head, rehearsal);
                        if (idx < 0) break;
                        if (idx < head) { Bug(S, "GO starter en cue før markøren", Dump()); break; }
                        var fired = list[idx];
                        if (!Rehearsal.IsPlayable(fired, rehearsal)) { Bug(S, "GO starter en cue som ikke kan spilles", Dump()); break; }
                        var cur = fired;
                        bool ranOff = false;
                        while (cur.ContinueMode != ContinueMode.None)
                        {
                            var nx = SceneLogic.NextEnabledAfter(cues, cur, rehearsal);
                            if (nx == null) { ranOff = true; break; }
                            if (!SceneLogic.SameScene(nx.Scene, fired.Scene)) { Bug(S, "auto-fortsett forlot scenen", Dump()); ranOff = true; break; }
                            if (!Rehearsal.IsPlayable(nx, rehearsal)) { Bug(S, "auto-fortsett starter en cue som ikke kan spilles", Dump()); ranOff = true; break; }
                            if (list.IndexOf(nx) <= list.IndexOf(cur)) { Bug(S, "auto-fortsett går ikke framover", Dump()); ranOff = true; break; }
                            cur = nx;
                        }
                        var np = SceneLogic.NextPlayhead(cues, fired, rehearsal);
                        if (ranOff) { if (np != null) Bug(S, "kjeden gikk ut av lista, men markøren står ikke på slutten", Dump()); break; }
                        int expected = -1;
                        for (int k = list.IndexOf(cur) + 1; k < list.Count; k++)
                            if (Rehearsal.IsPlayable(list[k], rehearsal)) { expected = k; break; }
                        if (expected < 0) { if (np != null) Bug(S, "markøren skulle stå på slutten av scenen", Dump()); break; }
                        if (np == null || list.IndexOf(np) != expected) { Bug(S, "GO-markøren står feil etter kjeden", Dump()); break; }
                        head = expected;
                    }
                }
                // Oppslag på tvers av scener kaster aldri, og gir bare cuer som finnes.
                foreach (var c in cues)
                {
                    string tgt = c is GotoCue gc ? gc.Target : c.Number;
                    string tsc = c is GotoCue gc2 ? gc2.TargetScene : "";
                    foreach (var m in SceneLogic.Matching(cues, c.Number, c.Scene)) if (!cues.Contains(m)) Bug(S, "Matching ga en cue som ikke finnes", Dump());
                    var t = SceneLogic.ResolveGoto(cues, scenes, SceneLogic.GotoSpec(tgt, tsc), c.Scene);
                    if (t?.Cue != null && !cues.Contains(t.Cue)) Bug(S, "ResolveGoto ga en cue som ikke finnes", Dump());
                    if (c is GroupCue g) CueTargets.Resolve(cues, g.Targets, g);
                }
                // Forhåndslastingen planlegger bare det GO kan starte i markørens scene.
                bool plain = !cues.Any(c => c is GotoCue or GroupCue);
                for (int p = -1; p <= cues.Count; p++)
                {
                    var plan = PreloadPlanner.Plan(cues, p, 3, rehearsal);
                    if (plan.Count > PreloadPlanner.MaxItems) Bug(S, "planen har for mange elementer", Dump());
                    if (plan.Select(x => x.Key).Distinct().Count() != plan.Count) Bug(S, "dobbelt element i planen", Dump());
                    if (!plain || p < 0 || p >= cues.Count) continue;
                    foreach (var item in plan)
                    {
                        var src = cues.OfType<AudioCue>().FirstOrDefault(a => a.FilePath == item.Path);
                        if (src == null || src.Disabled || Rehearsal.IsSkipped(src, rehearsal) || !SceneLogic.InScene(src, cues[p].Scene))
                            Bug(S, "forhåndslastingen planlegger en cue GO aldri starter", Dump() + " @" + p);
                    }
                }
            }
            catch (Exception e) { Bug(S, e, Dump()); }
        }
    }

    static Cue NewSceneCue(List<string> scenes)
    {
        string scene = scenes.Count > 0 && Rng.Next(5) != 0 ? scenes[Rng.Next(scenes.Count)] : "";
        string num = Rng.Next(6).ToString();
        Cue c = Rng.Next(5) switch
        {
            0 => new StopCue { Target = num },
            1 => new FadeCue { Target = scenes.Count > 0 ? scenes[Rng.Next(scenes.Count)] + ":" + num : num },
            2 => new GotoCue { Target = num, TargetScene = scenes.Count > 0 ? scenes[Rng.Next(scenes.Count)] : "" },
            3 => new GroupCue { Targets = "1,2," + num },
            _ => new MemoCue(),
        };
        c.Number = num;
        c.Scene = scene;
        return c;
    }

    /// <summary>Tilfeldige redigeringer av scenelista (legg til, gi nytt navn, slett, flytt, nummerer om) med sjekk av reglene etterpå, og rundtur i showfila.</summary>
    static void SceneOperations()
    {
        const string S = "Scene-redigering";
        for (int i = 0; i < 1500; i++)
        {
            CurrentCase = $"runde {i}";
            var cues = new List<Cue>();
            var scenes = new List<string>();
            string last = "";
            try
            {
                for (int step = 0; step < 40; step++)
                {
                    string Name() => Rng.Next(4) == 0 ? RandText() : "S" + Rng.Next(5);
                    string Pick() => scenes.Count > 0 ? scenes[Rng.Next(scenes.Count)] : "";
                    int op = Rng.Next(9);
                    last = $"steg {step} operasjon {op}";
                    int before = cues.Count;
                    switch (op)
                    {
                        case 0: cues.Add(NewSceneCue(scenes)); before = cues.Count; break;
                        case 1: SceneLogic.AddScene(scenes, cues, Name()); break;
                        case 2: SceneLogic.RenameScene(scenes, cues, Pick(), Name()); break;
                        case 3: SceneLogic.DeleteScene(scenes, cues, Pick()); break;
                        case 4: SceneLogic.MoveScene(scenes, Pick(), Rng.Next(-2, 3)); break;
                        case 5:
                            if (cues.Count > 0 && scenes.Count > 0) SceneLogic.MoveToScene(cues, cues.Where(_ => Rng.Next(3) == 0).ToList(), Pick());
                            break;
                        case 6:
                            {
                                var scene = Pick();
                                SceneLogic.Renumber(cues, scene);
                                int n = 0;
                                foreach (var c in SceneLogic.CuesIn(cues, scene))
                                    if (c.Number != (++n).ToString()) { Bug(S, "nummerering er ikke 1, 2, 3 …", last); break; }
                                break;
                            }
                        case 7: if (cues.Count > 0) { cues.RemoveAt(Rng.Next(cues.Count)); before = cues.Count; } break;
                        default: if (cues.Count > 0) cues[Rng.Next(cues.Count)].Scene = RandText(); break; // en scene som ikke finnes i lista
                    }
                    if (cues.Count != before) Bug(S, "antall cuer endret seg", last);
                    scenes = SceneLogic.Normalize(scenes, cues);
                    if (scenes.Count == 0)
                    {
                        if (cues.Any(c => c.Scene.Length > 0)) Bug(S, "cue med scene, men lista er tom", last);
                        continue;
                    }
                    foreach (var s in scenes)
                        if (s.Length == 0 || s.Length > 60 || SceneLogic.CleanName(s) != s) Bug(S, "ugyldig scenenavn", s);
                    if (scenes.Select(s => s.ToLowerInvariant()).Distinct().Count() != scenes.Count) Bug(S, "dobbelt scenenavn", last);
                    foreach (var c in cues)
                        if (!scenes.Any(s => s == c.Scene)) { Bug(S, "cue i en scene som ikke finnes", last); break; }
                }
                var doc = new ShowDocument();
                foreach (var c in cues) doc.Cues.Add(c);
                doc.Scenes = scenes;
                doc.NormalizeScenes();
                var json = doc.ToJson();
                var back = JsonSerializer.Deserialize<ShowDocument>(json, ShowDocument.JsonOptions)!;
                back.NormalizeScenes();
                var again = back.ToJson();
                if (json != again) Bug(S, "showfil med scener blir annerledes etter åpne + lagre", FirstDiff(json, again));
            }
            catch (Exception e) { Bug(S, e, last); }
        }
    }

    // ---------- nye felt i showfila ----------
    static List<RoutingPair> RandomPairs() => Enumerable.Range(0, Rng.Next(0, 12)).Select(_ => new RoutingPair(Rng.Next(-5, 80),
        Rng.Next(6) switch { 0 => double.NaN, 1 => double.PositiveInfinity, 2 => double.NegativeInfinity, _ => Rng.NextDouble() * 100 - 70 })).ToList();

    static void CheckPresets(List<RoutingPreset> list, string where, string input)
    {
        if (list.Count > RoutingPresets.MaxPresets) Bug(where, "for mange forhåndsvalg", input);
        var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var p in list)
        {
            if (p.Name.Length == 0 || p.Name.Length > RoutingPresets.MaxNameLength || p.Name != p.Name.Trim()) Bug(where, "ugyldig navn på forhåndsvalg", input);
            if (!names.Add(p.Name)) Bug(where, "dobbelt navn på forhåndsvalg", input);
            if (p.Pairs.Count < 1 || p.Pairs.Count > RoutingPresets.MaxPairs) Bug(where, "feil antall par", input);
            if (p.Pairs.Select(x => x.Bus).Distinct().Count() != p.Pairs.Count) Bug(where, "to par på samme utgang", input);
            foreach (var pr in p.Pairs)
                if (pr.Bus < 1 || pr.Bus > RoutingPresets.MaxBus - 1 || !double.IsFinite(pr.GainDb) || pr.GainDb < -60 || pr.GainDb > 12) Bug(where, "par utenfor grensene", input);
        }
    }

    static void CheckLoaded(ShowDocument doc, string where, string input)
    {
        CheckPresets(doc.RoutingPresets, where, input);
        foreach (var s in doc.Scenes)
            if (s.Length == 0 || SceneLogic.CleanName(s) != s) Bug(where, "ugyldig scenenavn etter åpning", input);
        if (doc.Scenes.Select(s => s.ToLowerInvariant()).Distinct().Count() != doc.Scenes.Count) Bug(where, "dobbelt scenenavn etter åpning", input);
        foreach (var c in doc.Cues)
        {
            if (doc.Scenes.Count == 0 ? c.Scene.Length != 0 : !doc.Scenes.Any(s => s == c.Scene)) Bug(where, "cue i en scene som ikke finnes etter åpning", input);
            if (c.ClockTrigger.Length > 0 && !ClockTriggers.TryParse(c.ClockTrigger, out _)) Bug(where, "ugyldig klokketrigger etter åpning", input);
        }
        doc.ToJson();
    }

    static readonly string[] BadScenes = { "5", "\"x\"", "[null]", "[1,2]", "[{}]", "[\"a:b\",\"\",\"A\",\"a\"]", "[[\"x\"]]", "null", "[\"" + "z" + "\"," + "null]", "{}" };
    static readonly string[] BadPresets = { "5", "\"x\"", "[null]", "[{\"Name\":5}]", "[{\"Name\":\"x\",\"Pairs\":[{\"Bus\":1e30}]}]", "[{\"Name\":\"ok\",\"Pairs\":[{\"Bus\":2,\"GainDb\":\"NaN\"}]}]",
        "[{\"Name\":\"ok\",\"Pairs\":null}]", "[{\"Name\":\"ok\",\"Pairs\":[null,{\"Bus\":-3,\"GainDb\":1e9}]}]", "[{\"Name\":\"\",\"Pairs\":[{\"Bus\":1}]}]", "null", "{}" };
    static readonly string[] BadCueValues = { "5", "[]", "null", "{}", "true", "\"25:61:61\"", "\"aa\"", "\"12:00:00\"", "-1", "1e999", "\"" + "q" + "\"" };

    /// <summary>Rundtur og ødelagte verdier i feltene som er nye siden 0.21: scener, routingforhåndsvalg, klokketrigger og «Hopp over i øvelse». Gamle filer uten feltene åpnes som før.</summary>
    static void ShowFileCompat()
    {
        const string S = "Showfil (nye felt)";
        var path = Path.Combine(Dir, "compat.showcue");
        var path2 = Path.Combine(Dir, "compat2.showcue");
        for (int i = 0; i < 300; i++)
        {
            CurrentCase = $"runde {i}";
            string text = "";
            try
            {
                var doc = RandomShow();
                doc.Cues.Add(new GroupCue { Targets = "1-3" });
                var names = Enumerable.Range(0, Rng.Next(0, 4)).Select(k => "Scene " + k).ToList();
                doc.Scenes = names;
                foreach (var c in doc.Cues)
                {
                    if (names.Count > 0) c.Scene = names[Rng.Next(names.Count)];
                    c.ClockTrigger = Rng.Next(3) == 0 ? $"{Rng.Next(0, 24)}:{Rng.Next(0, 60)}:{Rng.Next(0, 60)}" : "";
                    c.SkipInRehearsal = Rng.Next(3) == 0;
                }
                var presets = new List<RoutingPreset>();
                for (int k = Rng.Next(0, 5); k > 0; k--) RoutingPresets.Add(presets, "P" + Rng.Next(10), RandomPairs());
                doc.RoutingPresets = presets;
                doc.NormalizeScenes();

                // 1. Lagre, åpne, lagre: samme innhold.
                doc.Save(path);
                var back = ShowDocument.Load(path);
                CheckLoaded(back, S, path);
                back.Save(path2);
                var a = File.ReadAllText(path); var b = File.ReadAllText(path2);
                if (a != b) Bug(S, "lagre → åpne → lagre gir en annen fil", FirstDiff(a, b));
                if (back.Cues.Count != doc.Cues.Count || back.Scenes.Count != doc.Scenes.Count || back.RoutingPresets.Count != doc.RoutingPresets.Count)
                    Bug(S, "antall cuer, scener eller forhåndsvalg endret seg", path);

                // 2. Gammel fil: uten de nye nøklene.
                var node = JsonNode.Parse(a)!.AsObject();
                node.Remove("Scenes"); node.Remove("RoutingPresets");
                foreach (var cn in node["Cues"]!.AsArray())
                    foreach (var key in new[] { "Scene", "TargetScene", "ClockTrigger", "SkipInRehearsal" }) cn!.AsObject().Remove(key);
                text = node.ToJsonString();
                File.WriteAllText(path, text);
                var old = ShowDocument.Load(path);
                CheckLoaded(old, S + " (gammel fil)", path);
                if (old.Scenes.Count != 0 || old.RoutingPresets.Count != 0 || old.Cues.Count != doc.Cues.Count) Bug(S, "gammel fil gir scener, forhåndsvalg eller færre cuer", path);
                if (old.Cues.Any(c => c.Scene.Length > 0 || c.ClockTrigger.Length > 0 || c.SkipInRehearsal)) Bug(S, "gammel fil gir cuer med nye felt satt", path);

                // 3. Ødelagte verdier i de nye feltene: enten åpnes filen, eller den avvises med JsonException.
                for (int m = 0; m < 6; m++)
                {
                    var bad = JsonNode.Parse(a)!.AsObject();
                    if (Rng.Next(2) == 0) bad["Scenes"] = JsonNode.Parse(BadScenes[Rng.Next(BadScenes.Length)]);
                    if (Rng.Next(2) == 0) bad["RoutingPresets"] = JsonNode.Parse(BadPresets[Rng.Next(BadPresets.Length)]);
                    foreach (var cn in bad["Cues"]!.AsArray())
                        foreach (var key in new[] { "Scene", "TargetScene", "ClockTrigger", "SkipInRehearsal" })
                            if (Rng.Next(4) == 0) cn!.AsObject()[key] = JsonNode.Parse(BadCueValues[Rng.Next(BadCueValues.Length)]);
                    text = bad.ToJsonString();
                    File.WriteAllText(path, text);
                    ShowDocument? loaded = null;
                    try { loaded = ShowDocument.Load(path); }
                    catch (JsonException) { }
                    catch (InvalidDataException) { }
                    catch (Exception e) { Bug(S + " (åpne)", e, text.Length > 300 ? text[..300] : text); }
                    if (loaded != null) CheckLoaded(loaded, S + " (ødelagt)", text.Length > 300 ? text[..300] : text);
                }
            }
            catch (Exception e) { Bug(S, e, text.Length > 300 ? text[..300] : text); }
        }
    }

    /// <summary>Routingforhåndsvalg: tilfeldige operasjoner med NaN, uendelig og tall utenfor grensene; reglene gjelder etter hvert steg.</summary>
    static void RoutingPresetFuzz()
    {
        const string S = "Routingforhåndsvalg";
        for (int i = 0; i < 1500; i++)
        {
            CurrentCase = $"runde {i}";
            var list = new List<RoutingPreset>();
            var cues = new List<Cue>();
            for (int n = Rng.Next(1, 5); n > 0; n--)
                cues.Add(new AudioCue
                {
                    FileChannels = Rng.Next(-3, 70),
                    Routes = Rng.Next(3) == 0 ? null : Enumerable.Range(0, Rng.Next(0, 6)).Select(_ => new AudioRoute(Rng.Next(-1, 70), Rng.Next(-1, 70), Rng.NextDouble() * 80 - 70)).ToList(),
                });
            string last = "";
            try
            {
                for (int step = 0; step < 30; step++)
                {
                    int op = Rng.Next(8);
                    last = $"steg {step} operasjon {op}";
                    switch (op)
                    {
                        case 0: RoutingPresets.Add(list, RandText(), RandomPairs()); break;
                        case 1: RoutingPresets.Update(list, Rng.Next(-1, list.Count + 1), Rng.Next(3) == 0 ? RandText() : "P" + Rng.Next(6), RandomPairs()); break;
                        case 2: RoutingPresets.Rename(list, Rng.Next(-1, list.Count + 1), Rng.Next(3) == 0 ? RandText() : "P" + Rng.Next(6)); break;
                        case 3: RoutingPresets.Remove(list, Rng.Next(-1, list.Count + 1)); break;
                        case 4:
                            if (list.Count > 0) RoutingPresets.Apply(cues.Where(_ => Rng.Next(2) == 0).ToList(), list[Rng.Next(list.Count)], Rng.Next(-2, 70));
                            break;
                        case 5:
                            {
                                var clip = new RouteClipboard();
                                clip.Copy(cues[Rng.Next(cues.Count)]);
                                RoutingPresets.Paste(cues, clip, Rng.Next(-2, 70));
                                break;
                            }
                        case 6: RoutingPresets.TryParsePairs(RandText(), out _); break;
                        default:
                            {
                                var text = string.Join(Rng.Next(2) == 0 ? "," : "+", Enumerable.Range(0, Rng.Next(0, 10)).Select(_ => $"{Rng.Next(-1, 70)}-{Rng.Next(-1, 70)}" + (Rng.Next(3) == 0 ? ":" + Pieces[Rng.Next(Pieces.Length)] : "")));
                                if (RoutingPresets.TryParsePairs(text, out var parsed) && (parsed.Count == 0 || parsed.Count > RoutingPresets.MaxPairs)) Bug(S, "TryParsePairs ga feil antall par", text);
                                break;
                            }
                    }
                    CheckPresets(list, S, last);
                    foreach (var p in list)
                    {
                        int ch = Rng.Next(-2, 100), buses = Rng.Next(-2, 100);
                        var routes = RoutingPresets.Routes(p, ch, buses);
                        var seen = new HashSet<(int, int)>();
                        foreach (var r in routes)
                        {
                            if (r.Source < 1 || r.Source > Math.Min(Math.Max(ch, 0), RoutingPresets.MaxChannels) || r.Bus < 1 || r.Bus > RoutingPresets.MaxBus
                                || (buses > 0 && r.Bus > buses) || !double.IsFinite(r.GainDb) || r.GainDb < -60 || r.GainDb > 12 || !seen.Add((r.Source, r.Bus)))
                                Bug(S, "ugyldig rute fra forhåndsvalg", $"{ch} kanaler, {buses} utganger, rute {r.Source}->{r.Bus} {r.GainDb}");
                        }
                        if (RoutingPresets.TryParsePairs(RoutingPresets.FormatPairs(p), out var again))
                        {
                            if (!again.Select(x => x.Bus).SequenceEqual(p.Pairs.Select(x => x.Bus))) Bug(S, "format → tolk gir andre par", RoutingPresets.FormatPairs(p));
                        }
                        else Bug(S, "parene lar seg ikke lese etter formatering", RoutingPresets.FormatPairs(p));
                    }
                    foreach (var c in cues.OfType<AudioCue>())
                    {
                        RoutingPresets.Describe(c.Routes, c.FileChannels, list, Rng.Next(-1, 70));
                        if (c.Routes != null && c.Routes.Any(r => !double.IsFinite(r.GainDb))) Bug(S, "cue har rute med ugyldig nivå", last);
                    }
                }
                var doc = new ShowDocument { RoutingPresets = list };
                var json = doc.ToJson();
                var back = JsonSerializer.Deserialize<ShowDocument>(json, ShowDocument.JsonOptions)!;
                if (back.ToJson() != json) Bug(S, "forhåndsvalg blir annerledes etter åpne + lagre", FirstDiff(json, back.ToJson()));
            }
            catch (Exception e) { Bug(S, e, last); }
        }
    }

    // ---------- klokke-triggere ----------
    /// <summary>Klokketrigger: tolkeren tåler alt, et klokkeslett utløses nøyaktig én gang over midnatt, og hopp i klokka (sommertid, stilling, dvale) utløser aldri.</summary>
    static void ClockTriggerFuzz()
    {
        const string S = "Klokke-triggere";
        for (int i = 0; i < 100_000; i++)
        {
            var t = Rng.Next(3) == 0 ? $"{Rng.Next(-1, 30)}:{Rng.Next(-1, 70)}:{Rng.Next(-1, 70)}" : RandText();
            try
            {
                bool ok = ClockTriggers.TryParse(t, out var tm);
                var norm = ClockTriggers.Normalize(t);
                if (ok != (norm.Length > 0)) Bug(S, "TryParse og Normalize er uenige", t);
                if (ok && (tm < TimeSpan.Zero || tm >= TimeSpan.FromDays(1))) Bug(S, "klokkeslett utenfor døgnet", t);
                if (ClockTriggers.Normalize(norm) != norm) Bug(S, "Normalize er ikke idempotent", t);
            }
            catch (Exception e) { Bug(S, e, t); }
        }
        var triggers = new[] { "00:00:00", "23:59:59", "00:00:01", "23:59:58", "12:00:00", "0:0:0", "7:5:3" };
        for (int i = 0; i < 400; i++)
        {
            CurrentCase = $"runde {i}";
            var start = new DateTime(2026, 12, 31, 23, 58, 0).AddSeconds(Rng.Next(0, 180)).AddMilliseconds(Rng.Next(0, 1000));
            var end = start.AddSeconds(Rng.Next(30, 400));
            try
            {
                foreach (var trig in triggers)
                {
                    int fired = 0;
                    var prev = start;
                    while (prev < end)
                    {
                        var now = prev.AddMilliseconds(Rng.Next(1, 2001));
                        if (ClockTriggers.Crossed(prev, now, now - prev, trig)) fired++;
                        prev = now;
                    }
                    ClockTriggers.TryParse(trig, out var tm);
                    int expected = 0;
                    for (var day = start.Date; day <= prev.Date; day = day.AddDays(1))
                    {
                        var target = day + tm;
                        if (target > start && target <= prev) expected++;
                    }
                    if (fired != expected) Bug(S, $"«{trig}» utløst {fired} ganger, ventet {expected}", $"{start:O} til {prev:O}");
                }
                // Hopp i klokka: vegg- eller reell tid utenfor (0, 2 s] gir aldri utløsing.
                for (int k = 0; k < 200; k++)
                {
                    var prev = start.AddSeconds(Rng.Next(0, 3600));
                    double wall = Rng.Next(6) switch { 0 => -1.0, 1 => 0.0, 2 => 3600.0, 3 => 2.5, 4 => -3600.0, _ => Rng.NextDouble() * 4 - 1 };
                    double real = Rng.Next(4) switch { 0 => 1.0, 1 => -1.0, 2 => 3600.0, _ => Rng.NextDouble() * 4 - 1 };
                    var now = prev.AddSeconds(wall);
                    foreach (var trig in triggers)
                    {
                        bool inside = wall > 0 && wall <= 2 && real > 0 && real <= 2;
                        if (!inside && ClockTriggers.Crossed(prev, now, TimeSpan.FromSeconds(real), trig)) Bug(S, "utløst ved hopp i klokka", $"{prev:O} → {now:O} (reell {real} s) {trig}");
                    }
                }
                // Cuer: hver cue med trigger er med høyst én gang per intervall, deaktiverte aldri.
                var cues = triggers.Select(tr => (Cue)new StopCue { ClockTrigger = tr }).ToList();
                cues.Add(new StopCue { ClockTrigger = "00:00:00", Disabled = true });
                var due = ClockTriggers.Due(cues, new DateTime(2026, 12, 31, 23, 59, 59, 500), new DateTime(2027, 1, 1, 0, 0, 0, 500));
                if (due.Count != 2 || due.Distinct().Count() != due.Count || due.Any(c => c.Disabled)) Bug(S, "Due over midnatt gir feil cuer", string.Join(",", due.Select(c => c.ClockTrigger)));
                // Lokale tidspunkter: ingen unntak (også over en eventuell sommertidsgrense).
                var local = DateTime.Now;
                ClockTriggers.Crossed(local, local.AddSeconds(1), "12:00:00");
                ClockTriggers.Crossed(DateTime.SpecifyKind(local, DateTimeKind.Utc), local.AddSeconds(1), "12:00:00");
            }
            catch (Exception e) { Bug(S, e, start.ToString("O")); }
        }
    }

    // ---------- innstillinger ----------
    /// <summary>Innstillingsfil med feil typer og ekstreme tall: åpnes eller avvises med JsonException, og etter Normalize gjelder grensene.</summary>
    static void SettingsFuzz()
    {
        const string S = "Innstillinger";
        var baseNode = JsonNode.Parse(JsonSerializer.Serialize(new AppSettings(), ShowDocument.JsonOptions))!.AsObject();
        var keys = baseNode.Select(kv => kv.Key).ToList();
        string[] vals = { "null", "1e999", "-1", "0", "99999999999", "\"x\"", "true", "[]", "{}", "-0.0", "2147483647", "-2147483648", "1.5", "65536", "1023", "\"123456\"", "\"abc\"",
            "[null]", "[{\"Name\":null,\"Channels\":null,\"GainDb\":12345}]", "[{\"Name\":\"x\",\"Channels\":[0,1,99999,-4],\"GainDb\":-1e300}]" };
        for (int i = 0; i < 20_000; i++)
        {
            CurrentCase = $"runde {i}";
            var node = JsonNode.Parse(baseNode.ToJsonString())!.AsObject();
            for (int k = Rng.Next(1, 5); k > 0; k--) node[keys[Rng.Next(keys.Count)]] = JsonNode.Parse(vals[Rng.Next(vals.Length)]);
            var text = node.ToJsonString();
            AppSettings? s;
            try { s = JsonSerializer.Deserialize<AppSettings>(text, ShowDocument.JsonOptions); }
            catch (JsonException) { continue; }
            catch (Exception e) { Bug(S + " (åpne)", e, text.Length > 300 ? text[..300] : text); continue; }
            if (s == null) continue;
            try
            {
                s = AppSettings.Normalize(s);
                if (s.WebRemotePort < 1024 || s.WebRemotePort > 65535) Bug(S, "web-port utenfor grensene", text);
                if (s.OscInPort < 1 || s.OscInPort > 65535 || s.OscFeedbackPort < 1 || s.OscFeedbackPort > 65535) Bug(S, "OSC-port utenfor grensene", text);
                if (s.WebRemoteEnabled && !WebRemote.IsAcceptablePin(s.WebRemotePin)) Bug(S, "nettside på uten godkjent PIN", text);
                if (s.AsioInputFirstChannel < 1 || s.AsioInputFirstChannel > AsioInputPlanner.MaxFirstChannel || s.AsioInputChannels < 1 || s.AsioInputChannels > AsioInputPlanner.MaxChannels) Bug(S, "ASIO-innganger utenfor grensene", text);
                if (s.AsioMaxChannels < 2 || s.AsioMaxChannels > 128 || s.LatencyMs < 10 || s.LatencyMs > 500) Bug(S, "lyd utenfor grensene", text);
                if (s.Buses.Count == 0 || s.Buses.Any(b => b.Name == null || !double.IsFinite(b.GainDb) || b.Channels.Any(c => c < 1 || c > 512))) Bug(S, "patch utenfor grensene", text);
                if (!double.IsFinite(s.PanicFadeTime) || !double.IsFinite(s.LoudnessTarget) || !double.IsFinite(s.Zoom) || !double.IsFinite(s.ListTextScale)) Bug(S, "ikke-endelig tall etter Normalize", text);
                var again = JsonSerializer.Serialize(s, ShowDocument.JsonOptions);
                JsonSerializer.Deserialize<AppSettings>(again, ShowDocument.JsonOptions);
            }
            catch (Exception e) { Bug(S, e, text.Length > 300 ? text[..300] : text); }
        }
    }

    // ---------- ASIO-innganger ----------
    static AsioAudioAvailableEventArgs MakeAsioArgs(AsioSampleType type, int frames, int channels, Random rng, List<IntPtr> allocated)
    {
        int bytes = type switch { AsioSampleType.Int16LSB => 2, AsioSampleType.Int24LSB => 3, _ => 4 };
        var ptrs = new IntPtr[channels];
        for (int c = 0; c < channels; c++)
        {
            var data = new byte[frames * bytes];
            if (type == AsioSampleType.Float32LSB)
            {
                var f = new float[frames];
                for (int k = 0; k < frames; k++) f[k] = rng.Next(8) switch { 0 => float.NaN, 1 => float.PositiveInfinity, 2 => float.NegativeInfinity, _ => (float)(rng.NextDouble() * 4 - 2) };
                Buffer.BlockCopy(f, 0, data, 0, data.Length);
            }
            else rng.NextBytes(data);
            ptrs[c] = Marshal.AllocHGlobal(Math.Max(1, data.Length));
            allocated.Add(ptrs[c]);
            Marshal.Copy(data, 0, ptrs[c], data.Length);
        }
        return new AsioAudioAvailableEventArgs(ptrs, Array.Empty<IntPtr>(), frames, type);
    }

    /// <summary>ASIO-innganger uten maskinvare: planlegging med ekstreme tall, kanalomforming, og driverhendelser (med NaN, uendelig, feil kanalantall) mens lyttere åpnes og lukkes.</summary>
    static void AsioInputStress()
    {
        const string S = "ASIO-innganger";
        int[] ints = { int.MinValue, -1, 0, 1, 2, 255, 256, 257, 1000, int.MaxValue };
        for (int i = 0; i < 100_000; i++)
        {
            bool on = Rng.Next(4) != 0;
            int driver = Rng.Next(3) == 0 ? ints[Rng.Next(ints.Length)] : Rng.Next(-5, 300);
            int first = Rng.Next(2) == 0 ? ints[Rng.Next(ints.Length)] : Rng.Next(-5, 300);
            int count = Rng.Next(2) == 0 ? ints[Rng.Next(ints.Length)] : Rng.Next(-5, 100);
            var input = $"{on} {driver} {first} {count}";
            try
            {
                var p = AsioInputPlanner.Plan(on, driver, first, count);
                if (!on) { if (p.Status != AsioInputStatus.Off) Bug(S, "av gir ikke Off", input); }
                else if (driver < 1) { if (p.Status != AsioInputStatus.NoDriverInputs) Bug(S, "ingen innganger gir ikke NoDriverInputs", input); }
                else if (p.Status != AsioInputStatus.Ok || p.FirstChannel < 0 || p.Channels < 1 || p.Channels > AsioInputPlanner.MaxChannels || p.FirstChannel + p.Channels > driver)
                    Bug(S, "plan utenfor driverens innganger", input + $" → {p}");
            }
            catch (Exception e) { Bug(S, e, input); }
        }
        for (int i = 0; i < 20_000; i++)
        {
            int sc = Rng.Next(1, 20), dc = Rng.Next(1, 20), frames = Rng.Next(0, 40);
            var src = new float[frames * sc]; for (int k = 0; k < src.Length; k++) src[k] = k + 1;
            var dst = new float[frames * dc]; Array.Fill(dst, -1f);
            try
            {
                ChannelMap.Remap(src, sc, dst, dc, frames);
                for (int f = 0; f < frames; f++)
                    for (int c = 0; c < dc; c++)
                        if (dst[f * dc + c] != (c < sc ? src[f * sc + c] : 0f)) { Bug(S, "feil kanalomforming", $"{sc} → {dc}, {frames} rammer"); f = frames; break; }
            }
            catch (Exception e) { Bug(S, e, $"{sc} → {dc}, {frames} rammer"); }
        }

        int[] rates = { -5, 0, 8000, 48000 };
        for (int round = 0; round < 25; round++)
        {
            CurrentCase = $"runde {round}";
            int channels = Rng.Next(-1, 6);
            var feed = new AsioInputFeed(channels, rates[Rng.Next(rates.Length)]);
            if (Rng.Next(5) != 0) feed.PrepareBuffer(Rng.Next(-5, 1500));
            var errors = new ConcurrentBag<Exception>();
            var nonFinite = new ConcurrentBag<string>();
            var stop = new CancellationTokenSource();
            int seed = Rng.Next();
            var writer = new Thread(() =>
            {
                var rng = new Random(seed);
                var allocated = new List<IntPtr>();
                try
                {
                    for (int n = 0; n < 1500 && !stop.IsCancellationRequested; n++)
                    {
                        if (rng.Next(2) == 0)
                        {
                            var data = new float[rng.Next(0, 800)];
                            for (int k = 0; k < data.Length; k++) data[k] = rng.Next(8) == 0 ? float.NaN : (float)rng.NextDouble();
                            feed.Write(data, rng.Next(-1, 20));
                        }
                        else
                        {
                            var type = new[] { AsioSampleType.Float32LSB, AsioSampleType.Int16LSB, AsioSampleType.Int24LSB, AsioSampleType.Int32LSB }[rng.Next(4)];
                            int frames = rng.Next(0, 600), chs = rng.Next(0, 20);
                            allocated.Clear();
                            try
                            {
                                if (chs == 0) feed.OnAudioAvailable(null, new AsioAudioAvailableEventArgs(Array.Empty<IntPtr>(), Array.Empty<IntPtr>(), frames, type));
                                else feed.OnAudioAvailable(null, MakeAsioArgs(type, frames, chs, rng, allocated));
                            }
                            finally { foreach (var p in allocated) Marshal.FreeHGlobal(p); }
                        }
                    }
                }
                catch (Exception e) { errors.Add(e); }
            });
            var readers = Enumerable.Range(0, 3).Select(r => new Thread(() =>
            {
                var rng = new Random(seed + r + 1);
                try
                {
                    var buf = new float[512];
                    for (int n = 0; n < 20 && !stop.IsCancellationRequested; n++)
                    {
                        var ring = feed.Open();
                        for (int k = 0; k < 5; k++)
                        {
                            int count = Math.Min(buf.Length, rng.Next(0, 513) / ring.Channels * ring.Channels);
                            ring.Read(buf, 0, count);
                            for (int j = 0; j < count; j++) if (!float.IsFinite(buf[j])) { nonFinite.Add($"{buf[j]}"); break; }
                            Thread.Yield();
                        }
                        feed.Close(ring);
                    }
                }
                catch (Exception e) { errors.Add(e); }
            })).ToList();
            writer.Start(); readers.ForEach(t => t.Start());
            if (!writer.Join(30_000)) { stop.Cancel(); Bug(S, "driverhendelsene ble ikke ferdige", $"{channels} kanaler"); }
            stop.Cancel();
            readers.ForEach(t => { if (!t.Join(30_000)) Bug(S, "lytterne ble ikke ferdige", $"{channels} kanaler"); });
            foreach (var e in errors) Bug(S, e, $"{channels} kanaler");
            if (!nonFinite.IsEmpty) Bug(S, "ikke-endelig sample nådde en lytter", nonFinite.First());
            if (feed.ListenerCount != 0) Bug(S, "lyttere ble ikke fjernet", feed.ListenerCount.ToString());
        }

        // Et lydformat som ikke kan leses setter feeden i feilstilling uten å kaste (driver-tråden skal aldri falle).
        try
        {
            var feed = new AsioInputFeed(2, 48000);
            feed.PrepareBuffer(64);
            var allocated = new List<IntPtr>();
            try { feed.OnAudioAvailable(null, MakeAsioArgs(AsioSampleType.Float64LSB, 8, 2, new Random(1), allocated)); }
            finally { foreach (var p in allocated) Marshal.FreeHGlobal(p); }
            if (!feed.Failed) Bug(S, "ukjent lydformat satte ikke feeden i feilstilling", "Float64LSB");
        }
        catch (Exception e) { Bug(S, e, "ukjent lydformat"); }
    }

    // ---------- showlogg: avslutning midt i skrivingen og disk som feiler ----------
    static void ShowLogDisposeRace()
    {
        const string S = "Showlogg (avslutning)";
        for (int i = 0; i < 15; i++)
        {
            CurrentCase = $"runde {i}";
            var root = Path.Combine(Dir, "showlog2-" + Guid.NewGuid().ToString("N"));
            var errors = new ConcurrentBag<Exception>();
            var cts = new CancellationTokenSource();
            try
            {
                var log = new ShowLog(root, queueCapacity: 64, memoryLimit: 100, maxFileBytes: 4096, filesToKeep: 2);
                ShowLog.Install(log);
                var threads = Enumerable.Range(0, 4).Select(t => new Thread(() =>
                {
                    var rng = new Random(t + i);
                    try
                    {
                        while (!cts.IsCancellationRequested)
                        {
                            using (ShowLog.Via(StartSource.Osc)) ShowLog.Add(ShowLogKind.Warning, "1", "navn", "audio", StartSource.Unknown, new string('x', rng.Next(0, 300)));
                            ShowLog.AddCue(ShowLogKind.Start, null);
                            ShowLog.AddCue(ShowLogKind.Start, new StopCue { Number = "9" });
                        }
                    }
                    catch (Exception e) { errors.Add(e); }
                })).ToList();
                threads.ForEach(t => t.Start());
                Thread.Sleep(Rng.Next(5, 80));
                var sw = Stopwatch.StartNew();
                log.Dispose();
                if (sw.ElapsedMilliseconds > 4000) Bug(S, "Dispose brukte over 4 s", $"{sw.ElapsedMilliseconds} ms");
                log.Dispose();
                log.Flush(100);
                log.Record(ShowLogKind.Warning, "1");
                log.WriteFile = true;
                log.Snapshot();
                Thread.Sleep(20);
                cts.Cancel();
                foreach (var t in threads) if (!t.Join(5000)) Bug(S, "skrivertråd henger etter Dispose", "");
                ShowLog.Install(null);
                foreach (var e in errors) Bug(S, e, "etter Dispose");
            }
            catch (Exception e) { Bug(S, e, "oppsett"); }
            finally { cts.Cancel(); ShowLog.Install(null); try { Directory.Delete(root, true); } catch { } }
        }

        // Disk som feiler: datamappa er i virkeligheten en vanlig fil. Feilen meldes én gang, og logging kaster aldri.
        var fileAsRoot = Path.Combine(Dir, "showlog-fil-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(fileAsRoot, "x");
            int faults = 0;
            using var log = new ShowLog(fileAsRoot);
            log.FaultReported += _ => Interlocked.Increment(ref faults);
            for (int k = 0; k < 2000; k++) log.Record(ShowLogKind.Warning, k.ToString(), "navn");
            log.Flush(3000);
            Thread.Sleep(100);
            if (log.Fault == null) Bug(S, "feil på disk ble ikke meldt", fileAsRoot);
            if (faults != 1) Bug(S, $"feil på disk meldt {faults} ganger (skal være én)", fileAsRoot);
            if (log.Snapshot().Count == 0) Bug(S, "minnelogg tom etter diskfeil", fileAsRoot);
        }
        catch (Exception e) { Bug(S, e, "diskfeil"); }
        finally { try { File.Delete(fileAsRoot); } catch { } }
    }

    // ---------- nettsiden med ekte sockets ----------
    static int FreeTcpPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    static byte[] HttpRequest(string method, string path, string headers = "", string body = "", bool length = true) =>
        Encoding.ASCII.GetBytes($"{method} {path} HTTP/1.1\r\nHost: x\r\n{headers}{(length ? $"Content-Length: {body.Length}\r\n" : "")}\r\n{body}");

    /// <summary>Sender byte og leser til tjeneren lukker. Tom tekst ved feil eller frakobling; <paramref name="timedOut"/> hvis tjeneren ikke svarte i tide.</summary>
    static string HttpExchange(int port, byte[] request, bool halfClose, int timeoutMs, out bool timedOut)
    {
        timedOut = false;
        try
        {
            using var c = new TcpClient();
            c.ReceiveTimeout = timeoutMs;
            c.SendTimeout = timeoutMs;
            c.Connect(IPAddress.Loopback, port);
            var s = c.GetStream();
            if (request.Length > 0) s.Write(request, 0, request.Length);
            if (halfClose) c.Client.Shutdown(SocketShutdown.Send);
            var ms = new MemoryStream();
            var buf = new byte[4096];
            while (true)
            {
                int n = s.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                ms.Write(buf, 0, n);
            }
            return Encoding.ASCII.GetString(ms.ToArray());
        }
        catch (IOException e) when (e.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut }) { timedOut = true; return ""; }
        catch { return ""; }
    }

    static string Http(int port, byte[] request) => HttpExchange(port, request, false, 8000, out _);

    static int StatusOf(string response) =>
        response.Length >= 12 && response.StartsWith("HTTP/1.1 ", StringComparison.Ordinal) && int.TryParse(response.AsSpan(9, 3), out var s) ? s : 0;

    /// <summary>Nettsiden mot ekte sockets: gyldige og ødelagte forespørsler, feil PIN, store kropper, mange og trege forbindelser, kommando som kaster, stopp mens forbindelser er åpne, omstart på samme port.</summary>
    static void WebServerSockets()
    {
        const string S = "Nettside (sockets)";
        const string pin = "483920";
        int port = FreeTcpPort();
        var login = Encoding.ASCII.GetBytes("login-page");
        var control = Encoding.ASCII.GetBytes("control-page");
        int commands = 0;
        bool throwInCommand = false;
        try
        {
            using var server = new WebServer
            {
                Command = (cmd, ip) =>
                {
                    Interlocked.Increment(ref commands);
                    if (throwInCommand) throw new InvalidOperationException("mottakeren feilet");
                },
            };
            server.Start(port, new WebRemoteCore(pin, login, control, () => Environment.TickCount64));
            if (!server.IsRunning) Bug(S, "tjeneren sier den ikke kjører", "");

            // Vanlige og ødelagte forespørsler.
            var r = Http(port, HttpRequest("GET", "/", length: false));
            if (StatusOf(r) != 200 || !r.Contains("login-page")) Bug(S, "GET / uten innlogging gir ikke innloggingssiden", r.Length > 100 ? r[..100] : r);
            if (StatusOf(Http(port, HttpRequest("GET", "/nope", length: false))) != 404) Bug(S, "ukjent rute gir ikke 404", "");
            if (StatusOf(Http(port, HttpRequest("PUT", "/", length: false))) != 405) Bug(S, "PUT gir ikke 405", "");
            if (StatusOf(Http(port, HttpRequest("GET", "/api/state", length: false))) != 401) Bug(S, "tilstand uten innlogging gir ikke 401", "");
            if (StatusOf(Http(port, Encoding.ASCII.GetBytes("GARBAGE\r\n\r\n"))) != 400) Bug(S, "tull gir ikke 400", "");
            if (StatusOf(Http(port, HttpRequest("GET", "/../etc/passwd", length: false))) != 400) Bug(S, "sti med .. gir ikke 400", "");

            // Innlogging: noen feil, så riktig PIN (under grensen for sperre).
            string cookie = "";
            for (int k = 0; k < 3; k++)
                if (StatusOf(Http(port, HttpRequest("POST", "/api/login", "Content-Type: application/x-www-form-urlencoded\r\n", "pin=000111"))) != 401) Bug(S, "feil PIN gir ikke 401", "");
            r = Http(port, HttpRequest("POST", "/api/login", "", "pin=" + pin));
            var m = System.Text.RegularExpressions.Regex.Match(r, @"sc_session=([0-9a-f]{32})");
            if (StatusOf(r) != 200 || !m.Success) Bug(S, "riktig PIN gir ikke økt", r.Length > 200 ? r[..200] : r);
            else cookie = "Cookie: sc_session=" + m.Groups[1].Value + "\r\n";
            if (StatusOf(Http(port, HttpRequest("GET", "/api/state", cookie, length: false))) != 200) Bug(S, "tilstand med økt gir ikke 200", "");
            if (!Http(port, HttpRequest("GET", "/", cookie, length: false)).Contains("control-page")) Bug(S, "kontrollsiden vises ikke med økt", "");

            // Kommandoer.
            int before = commands;
            if (StatusOf(Http(port, HttpRequest("POST", "/api/cmd", cookie + "X-ShowCue: 1\r\n", "cmd=go"))) != 200 || commands != before + 1) Bug(S, "kommando med økt ble ikke utført", $"{commands - before}");
            before = commands;
            if (StatusOf(Http(port, HttpRequest("POST", "/api/cmd", cookie, "cmd=go"))) != 403 || commands != before) Bug(S, "kommando uten X-ShowCue ble ikke avvist", "");
            if (StatusOf(Http(port, HttpRequest("POST", "/api/cmd", "X-ShowCue: 1\r\n", "cmd=go"))) != 401 || commands != before) Bug(S, "kommando uten økt ble ikke avvist", "");
            if (StatusOf(Http(port, HttpRequest("POST", "/api/cmd", cookie + "X-ShowCue: 1\r\n", "cmd=format"))) != 400 || commands != before) Bug(S, "ukjent kommando ble ikke avvist", "");
            throwInCommand = true;
            if (StatusOf(Http(port, HttpRequest("POST", "/api/cmd", cookie + "X-ShowCue: 1\r\n", "cmd=stop"))) != 200) Bug(S, "kommando som kaster velter tjeneren", "");
            throwInCommand = false;

            // For store forespørsler: avvises (4xx) eller lukkes, aldri 200 og aldri hengende.
            var big = new[]
            {
                HttpRequest("POST", "/api/cmd", cookie + "X-ShowCue: 1\r\nContent-Length: 99999999\r\n", "", length: false),
                HttpRequest("POST", "/api/login", "Content-Length: 2000\r\n", new string('a', 2000), length: false),
                Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: x\r\nX-Long: " + new string('a', 30_000) + "\r\n\r\n"),
                Encoding.ASCII.GetBytes("GET /" + new string('a', 5000) + " HTTP/1.1\r\nHost: x\r\n\r\n"),
                Encoding.ASCII.GetBytes("POST /api/login HTTP/1.1\r\nHost: x\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n0\r\n\r\n"),
                new byte[20_000],
            };
            for (int k = 0; k < big.Length; k++)
            {
                r = HttpExchange(port, big[k], false, 8000, out var timedOut);
                int st = StatusOf(r);
                if (timedOut) Bug(S, "for stor forespørsel ble ikke avvist i tide", $"forespørsel {k}");
                if (st == 200) Bug(S, "for stor forespørsel ble godtatt", $"forespørsel {k}");
            }

            // Tilfeldige byte: svar eller lukk, aldri hengende (klienten lukker sendesiden).
            for (int k = 0; k < 150; k++)
            {
                var data = RandBytes(600);
                HttpExchange(port, data, true, 8000, out var timedOut);
                if (timedOut) Bug(S, "tilfeldige byte ga ingen reaksjon", Convert.ToHexString(data));
            }

            // Mange samtidige forbindelser: bare 200 eller 503 (eller frakobling), og alt frigjøres etterpå.
            var statuses = new ConcurrentBag<int>();
            var flood = Enumerable.Range(0, 60).Select(k => new Thread(() =>
            {
                var resp = HttpExchange(port, HttpRequest("GET", "/api/state", cookie, length: false), false, 8000, out var to);
                statuses.Add(to ? -1 : StatusOf(resp));
            })).ToList();
            flood.ForEach(t => t.Start());
            flood.ForEach(t => { if (!t.Join(30_000)) Bug(S, "samtidige forbindelser ble ikke ferdige", ""); });
            if (statuses.Any(s => s == -1)) Bug(S, "en forbindelse fikk ikke svar i tide", "");
            if (statuses.Any(s => s != 200 && s != 503 && s != 0)) Bug(S, "uventet status under belastning", string.Join(",", statuses.Distinct()));
            if (!statuses.Contains(200)) Bug(S, "ingen forbindelser ble besvart under belastning", "");
            for (int k = 0; k < 12; k++)
                if (StatusOf(Http(port, HttpRequest("GET", "/api/state", cookie, length: false))) != 200) { Bug(S, "forbindelser frigjøres ikke etter belastning", $"forespørsel {k}"); break; }

            // Trege klienter: åpne forbindelser uten data. Tidsfristen lukker dem, og tjeneren svarer igjen.
            var silent = new List<TcpClient>();
            for (int k = 0; k < 10; k++) { var c = new TcpClient(); c.Connect(IPAddress.Loopback, port); silent.Add(c); }
            var during = StatusOf(Http(port, HttpRequest("GET", "/api/state", cookie, length: false)));
            if (during != 200 && during != 503 && during != 0) Bug(S, "uventet status mens trege klienter henger", during.ToString());
            var closers = silent.Select(c => new Thread(() =>
            {
                try
                {
                    c.ReceiveTimeout = 12_000;
                    c.GetStream().Read(new byte[16], 0, 16); // 503 (for mange forbindelser) eller stille lukking ved tidsfristen
                }
                catch (IOException e) when (e.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut }) { Bug(S, "tidsfristen lukket ikke en treg klient", ""); }
                catch { }
            })).ToList();
            closers.ForEach(t => t.Start());
            closers.ForEach(t => t.Join(30_000));
            silent.ForEach(c => c.Dispose());
            if (StatusOf(Http(port, HttpRequest("GET", "/api/state", cookie, length: false))) != 200) Bug(S, "tjeneren svarer ikke etter at trege klienter ble lukket", "");

            // Stopp mens forbindelser er åpne.
            var open = new List<TcpClient>();
            for (int k = 0; k < 10; k++) { var c = new TcpClient(); c.Connect(IPAddress.Loopback, port); open.Add(c); }
            Thread.Sleep(300); // la tjeneren ta imot forbindelsene (ikke-aksepterte i køen avgjøres av operativsystemet)
            var sw = Stopwatch.StartNew();
            server.Stop();
            if (sw.ElapsedMilliseconds > 2000) Bug(S, "Stop brukte over 2 s", $"{sw.ElapsedMilliseconds} ms");
            if (server.IsRunning) Bug(S, "IsRunning er sann etter Stop", "");
            var gone = open.Select((c, idx) => new Thread(() =>
            {
                try
                {
                    c.ReceiveTimeout = 4000;
                    var buf = new byte[256];
                    int total = 0, n;
                    while ((n = c.GetStream().Read(buf, 0, buf.Length)) > 0 && (total += n) < 8192) { } // et eventuelt 503-svar, så lukking
                }
                catch (IOException e) when (e.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut }) { Bug(S, "forbindelser ble ikke lukket av Stop", $"forbindelse {idx} av {open.Count}, {sw.ElapsedMilliseconds} ms etter Stop"); }
                catch { }
            })).ToList();
            gone.ForEach(t => t.Start());
            gone.ForEach(t => t.Join(15_000));
            open.ForEach(c => c.Dispose());
            if (!string.IsNullOrEmpty(Http(port, HttpRequest("GET", "/", length: false)))) Bug(S, "tjeneren svarer etter Stop", "");

            // Omstart på samme port rett etter at klienter har vært koblet til.
            try
            {
                server.Start(port, new WebRemoteCore(pin, login, control, () => Environment.TickCount64));
                if (StatusOf(Http(port, HttpRequest("GET", "/", length: false))) != 200) Bug(S, "tjeneren svarer ikke etter omstart", "");
            }
            catch (Exception e) { Bug(S + " (omstart)", e, $"port {port} rett etter at tilkoblede klienter ble lukket"); }
            server.Dispose();
            server.Dispose();
            if (server.IsRunning) Bug(S, "IsRunning er sann etter Dispose", "");
        }
        catch (Exception e) { Bug(S, e, "oppsett"); }

        // Sperre mot gjetting: feil PIN gjentatte ganger gir 429, og riktig PIN slipper ikke inn mens sperren varer.
        try
        {
            int port2 = FreeTcpPort();
            using var server = new WebServer();
            server.Start(port2, new WebRemoteCore(pin, login, control, () => Environment.TickCount64));
            var codes = new List<int>();
            string last = "";
            for (int k = 0; k < 8; k++) { last = Http(port2, HttpRequest("POST", "/api/login", "", "pin=" + (100000 + k))); codes.Add(StatusOf(last)); }
            if (!codes.Contains(429)) Bug(S, "gjetting ga aldri sperre", string.Join(",", codes));
            if (!last.Contains("Retry-After:")) Bug(S, "sperren mangler Retry-After", last.Length > 200 ? last[..200] : last);
            var right = Http(port2, HttpRequest("POST", "/api/login", "", "pin=" + pin));
            if (StatusOf(right) == 200) Bug(S, "riktig PIN slapp inn under sperre", "");
            if (right.Contains("sc_session=")) Bug(S, "økt utstedt under sperre", "");
        }
        catch (Exception e) { Bug(S + " (sperre)", e, "oppsett"); }
    }
}
