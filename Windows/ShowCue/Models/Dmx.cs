using System.Globalization;

namespace ShowCue.Models;

/// <summary>DMX-nivåer skrevet som tekst: «1@255 2-4@50% 10@0» (kanal@verdi, verdi 0–255 eller prosent).</summary>
public static class DmxLevels
{
    public static bool TryParse(string? text, out SortedDictionary<int, byte> levels, out string error)
    {
        levels = new SortedDictionary<int, byte>();
        error = "";
        foreach (var raw in (text ?? "").Split(new[] { ' ', ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var at = raw.Split('@', '=');
            if (at.Length != 2) { error = $"«{raw}»: skriv kanal@verdi, f.eks. 1@255 eller 1-4@50%"; return false; }
            var range = at[0].Split('-');
            if (!int.TryParse(range[0], NumberStyles.None, CultureInfo.InvariantCulture, out var from)
                || !int.TryParse(range[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var to)
                || range.Length > 2 || from < 1 || to > 512 || to < from)
            { error = $"«{raw}»: kanal må være 1–512"; return false; }
            var v = at[1].Trim();
            double value;
            if (v.EndsWith('%'))
            {
                if (!double.TryParse(v[..^1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct) || pct < 0 || pct > 100)
                { error = $"«{raw}»: prosent må være 0–100"; return false; }
                value = pct * 255 / 100;
            }
            else if (!double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value < 0 || value > 255)
            { error = $"«{raw}»: verdi må være 0–255"; return false; }
            for (int c = from; c <= to; c++) levels[c] = (byte)Math.Round(value, MidpointRounding.AwayFromZero);
        }
        return true;
    }

    public static string Summary(string? text)
    {
        if (!TryParse(text, out var l, out _) || l.Count == 0) return "";
        return l.Count == 1 ? $"kanal {l.Keys.First()}" : $"{l.Count} kanaler";
    }
}

/// <summary>Art-Net (ArtDmx, UDP port 6454): sender ett univers med 512 DMX-kanaler.</summary>
public static class ArtNet
{
    public const int Port = 6454;

    public static byte[] DmxPacket(int universe, byte sequence, byte[] data)
    {
        var p = new byte[18 + 512];
        "Art-Net\0"u8.CopyTo(p);
        p[8] = 0x00; p[9] = 0x50;          // OpCode 0x5000 (ArtDmx), lav byte først
        p[10] = 0; p[11] = 14;             // protokollversjon 14
        p[12] = sequence;
        p[13] = 0;                         // fysisk port
        p[14] = (byte)(universe & 0xFF);   // SubUni
        p[15] = (byte)((universe >> 8) & 0x7F); // Net
        p[16] = 0x02; p[17] = 0x00;        // lengde 512, høy byte først
        Array.Copy(data, 0, p, 18, Math.Min(512, data.Length));
        return p;
    }
}

/// <summary>Nivåene i ett DMX-univers, med fades. Tiden gis utenfra (sekunder), så den kan testes.</summary>
public sealed class DmxFader
{
    private readonly double[] _from = new double[512];
    private readonly double[] _to = new double[512];
    private readonly double[] _start = new double[512];
    private readonly double[] _length = new double[512];
    private readonly object _lock = new();

    /// <summary>Fade kanalene til nye verdier. En kanal som allerede fader, fortsetter fra der den er nå.</summary>
    public void FadeTo(IReadOnlyDictionary<int, byte> levels, double seconds, double now)
    {
        lock (_lock)
        {
            foreach (var (ch, v) in levels)
            {
                int i = ch - 1;
                if (i < 0 || i >= 512) continue;
                _from[i] = ValueAt(i, now);
                _to[i] = v;
                _start[i] = now;
                _length[i] = Math.Max(0, seconds);
            }
        }
    }

    private double ValueAt(int i, double now)
    {
        if (_length[i] <= 0 || now >= _start[i] + _length[i]) return _to[i];
        var p = Math.Clamp((now - _start[i]) / _length[i], 0, 1);
        return _from[i] + (_to[i] - _from[i]) * p;
    }

    public byte[] Values(double now)
    {
        var b = new byte[512];
        lock (_lock) for (int i = 0; i < 512; i++) b[i] = (byte)Math.Round(Math.Clamp(ValueAt(i, now), 0, 255));
        return b;
    }
}
