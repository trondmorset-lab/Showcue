using System.Globalization;

namespace ShowCue.Models;

/// <summary>Klokkeslett-trigger: en cue starter når veggklokka (lokal tid) passerer et klokkeslett «tt:mm:ss».
/// Ren logikk uten klokke og maskinvare; kalleren gir inn forrige og nåværende tidspunkt.</summary>
public static class ClockTriggers
{
    /// <summary>Lengste intervall mellom to sjekker som regnes som vanlig gange. Lengre opphold (dvale, klokkestilling,
    /// sommertid) regnes som hopp, og da starter ingen cuer.</summary>
    public static readonly TimeSpan MaxInterval = TimeSpan.FromSeconds(2);

    /// <summary>Leser «tt:mm:ss» (ett eller to sifre per del). Tom tekst, feil format eller verdier utenfor 0–23/0–59/0–59 gir false.</summary>
    public static bool TryParse(string? text, out TimeSpan time)
    {
        time = default;
        var parts = (text ?? "").Trim().Split(':');
        if (parts.Length != 3) return false;
        var v = new int[3];
        for (int i = 0; i < 3; i++)
            if (parts[i].Length is 0 or > 2 || !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out v[i])) return false;
        if (v[0] > 23 || v[1] > 59 || v[2] > 59) return false;
        time = new TimeSpan(v[0], v[1], v[2]);
        return true;
    }

    /// <summary>Gyldig tekst i fast format «tt:mm:ss», ellers tom tekst.</summary>
    public static string Normalize(string? text) =>
        TryParse(text, out var t) ? $"{t.Hours:00}:{t.Minutes:00}:{t.Seconds:00}" : "";

    /// <summary>Ble klokkeslettet passert fra <paramref name="previous"/> til <paramref name="now"/>? Passert betyr
    /// forrige &lt; klokkeslettet ≤ nå. Tåler midnatt. For lokale tidspunkter (<see cref="DateTime.Now"/>) måles også
    /// faktisk medgått tid, så en omstilling til sommer- eller vintertid regnes som hopp.</summary>
    public static bool Crossed(DateTime previous, DateTime now, string? trigger)
    {
        var real = previous.Kind == DateTimeKind.Local && now.Kind == DateTimeKind.Local
            ? now.ToUniversalTime() - previous.ToUniversalTime()
            : now - previous;
        return Crossed(previous, now, real, trigger);
    }

    /// <summary>Som over, men med faktisk medgått tid gitt av kalleren (måler som ikke påvirkes av klokkestilling).
    /// Intervall som ikke er framover, eller som er lengre enn <see cref="MaxInterval"/> på veggklokka eller i
    /// faktisk tid (hopp i klokka, dvale, sommertid), gir alltid false.</summary>
    public static bool Crossed(DateTime previous, DateTime now, TimeSpan realElapsed, string? trigger)
    {
        if (!TryParse(trigger, out var time)) return false;
        var wall = now - previous;
        if (wall <= TimeSpan.Zero || wall > MaxInterval) return false;
        if (realElapsed <= TimeSpan.Zero || realElapsed > MaxInterval) return false;
        // Intervallet er kort og kan høyst spenne over én midnatt: sjekk klokkeslettet på begge datoene.
        for (var day = previous.Date; day <= now.Date; day = day.AddDays(1))
        {
            var target = day + time;
            if (previous < target && target <= now) return true;
        }
        return false;
    }

    /// <summary>Cuene som skal starte: ikke deaktiverte, med klokketrigger som ble passert. Hver cue er med høyst én gang.</summary>
    public static List<Cue> Due(IEnumerable<Cue> cues, DateTime previous, DateTime now) =>
        cues.Where(c => !c.Disabled && Crossed(previous, now, c.ClockTrigger)).ToList();
}
