using System.Globalization;

namespace ShowCue.Models;

/// <summary>Bilderate for tidskode. 29,97 er alltid «drop frame» (som i kringkasting).</summary>
public enum TimecodeRate { Fps24, Fps25, Fps2997Drop, Fps30 }

/// <summary>SMPTE-tidskode tt:mm:ss:ff. Regnes om til og fra et fortløpende bildenummer.</summary>
public readonly record struct Timecode(int Hours, int Minutes, int Seconds, int Frames)
{
    /// <summary>Bilder per sekund i tellingen (30 for 29,97 drop frame).</summary>
    public static int Nominal(TimecodeRate r) => r switch { TimecodeRate.Fps24 => 24, TimecodeRate.Fps25 => 25, _ => 30 };

    /// <summary>Faktiske bilder per sekund.</summary>
    public static double Fps(TimecodeRate r) => r == TimecodeRate.Fps2997Drop ? 30000.0 / 1001 : Nominal(r);

    public static string Label(TimecodeRate r) => r switch
    {
        TimecodeRate.Fps24 => "24 fps (film)",
        TimecodeRate.Fps25 => "25 fps (PAL/Europa)",
        TimecodeRate.Fps2997Drop => "29,97 fps drop frame (NTSC)",
        _ => "30 fps",
    };

    /// <summary>Bildenummer fra 00:00:00:00 (tar hensyn til drop frame).</summary>
    public long ToFrame(TimecodeRate r)
    {
        long fps = Nominal(r);
        long n = ((Hours * 60L + Minutes) * 60 + Seconds) * fps + Frames;
        if (r != TimecodeRate.Fps2997Drop) return n;
        long minutes = Hours * 60L + Minutes;
        return n - 2 * (minutes - minutes / 10);
    }

    public static Timecode FromFrame(long n, TimecodeRate r)
    {
        long fps = Nominal(r);
        long perDay = 24 * 3600 * fps;
        if (r == TimecodeRate.Fps2997Drop)
        {
            perDay = 24 * 6 * 17982;
            n = ((n % perDay) + perDay) % perDay;
            long d = n / 17982, m = n % 17982;
            n += 18 * d + (m > 1 ? 2 * ((m - 2) / 1798) : 0);
        }
        else n = ((n % perDay) + perDay) % perDay;
        return new Timecode((int)(n / (3600 * fps) % 24), (int)(n / (60 * fps) % 60), (int)(n / fps % 60), (int)(n % fps));
    }

    public double ToSeconds(TimecodeRate r) => ToFrame(r) / Fps(r);
    public static Timecode FromSeconds(double s, TimecodeRate r) => FromFrame((long)Math.Floor(s * Fps(r) + 1e-6), r);

    public string Format(TimecodeRate r) => $"{Hours:00}:{Minutes:00}:{Seconds:00}{(r == TimecodeRate.Fps2997Drop ? ';' : ':')}{Frames:00}";
    public override string ToString() => $"{Hours:00}:{Minutes:00}:{Seconds:00}:{Frames:00}";

    /// <summary>Leser «tt:mm:ss:ff» (også med ; eller . som skille). Tom tekst eller feil format gir false.</summary>
    public static bool TryParse(string? text, out Timecode tc)
    {
        tc = default;
        var parts = (text ?? "").Trim().Split(':', ';', '.', ',');
        if (parts.Length != 4) return false;
        var v = new int[4];
        for (int i = 0; i < 4; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out v[i])) return false;
        if (v[0] > 23 || v[1] > 59 || v[2] > 59 || v[3] > 29) return false;
        tc = new Timecode(v[0], v[1], v[2], v[3]);
        return true;
    }

    /// <summary>Har tidskoden passert <paramref name="target"/> fra forrige til nå? Hopp på mer enn
    /// to sekunder (spoling) og bakover regnes ikke som passering.</summary>
    public static bool Crossed(long previousFrame, long nowFrame, long target, TimecodeRate r)
        => nowFrame >= previousFrame && nowFrame - previousFrame <= 2 * Fps(r) && previousFrame < target && target <= nowFrame;
}

/// <summary>MIDI Time Code: kvartbilder (F1 xx) og fullbilde-meldinger (F0 7F 7F 01 01 … F7).</summary>
public static class Mtc
{
    public static int RateCode(TimecodeRate r) => r switch
    {
        TimecodeRate.Fps24 => 0, TimecodeRate.Fps25 => 1, TimecodeRate.Fps2997Drop => 2, _ => 3,
    };

    public static TimecodeRate FromRateCode(int c) => c switch
    {
        0 => TimecodeRate.Fps24, 1 => TimecodeRate.Fps25, 2 => TimecodeRate.Fps2997Drop, _ => TimecodeRate.Fps30,
    };

    /// <summary>Databyte nr. <paramref name="piece"/> (0–7) for tidskoden. Hele rekken på 8 tar to bilder.</summary>
    public static byte QuarterFrame(Timecode tc, TimecodeRate r, int piece)
    {
        int v = piece switch
        {
            0 => tc.Frames & 0xF, 1 => tc.Frames >> 4,
            2 => tc.Seconds & 0xF, 3 => tc.Seconds >> 4,
            4 => tc.Minutes & 0xF, 5 => tc.Minutes >> 4,
            6 => tc.Hours & 0xF, _ => (tc.Hours >> 4) | (RateCode(r) << 1),
        };
        return (byte)((piece << 4) | (v & 0xF));
    }

    /// <summary>Kvartbilde nr. <paramref name="index"/> (0, 1, 2 …) i en strøm som starter på <paramref name="startFrame"/>.
    /// Hver rekke på 8 bærer tidskoden for bildet der rekken startet.</summary>
    public static byte QuarterFrameAt(long index, long startFrame, TimecodeRate r)
        => QuarterFrame(Timecode.FromFrame(startFrame + index / 8 * 2, r), r, (int)(index % 8));

    public static byte[] FullFrame(Timecode tc, TimecodeRate r)
        => new byte[] { 0xF0, 0x7F, 0x7F, 0x01, 0x01, (byte)((RateCode(r) << 5) | tc.Hours), (byte)tc.Minutes, (byte)tc.Seconds, (byte)tc.Frames, 0xF7 };
}

/// <summary>Leser innkommende MTC. Gir tidskoden når en hel rekke kvartbilder (eller et fullbilde) er mottatt.</summary>
public sealed class MtcDecoder
{
    private readonly int[] _nibbles = new int[8];
    private int _expected;

    public TimecodeRate Rate { get; private set; } = TimecodeRate.Fps25;

    /// <summary>Databyten etter F1. Returnerer tidskoden når rekken er komplett (hvert annet bilde).</summary>
    public Timecode? QuarterFrame(byte data)
    {
        int piece = (data >> 4) & 7;
        if (piece != _expected) { _expected = piece == 0 ? 1 : 0; if (piece == 0) _nibbles[0] = data & 0xF; return null; }
        _nibbles[piece] = data & 0xF;
        _expected = (piece + 1) % 8;
        if (piece != 7) return null;
        Rate = Mtc.FromRateCode((_nibbles[7] >> 1) & 3);
        var tc = new Timecode(_nibbles[6] | ((_nibbles[7] & 1) << 4), _nibbles[4] | (_nibbles[5] << 4),
                              _nibbles[2] | (_nibbles[3] << 4), _nibbles[0] | (_nibbles[1] << 4));
        // Rekken tok to bilder å sende: nå er vi to bilder videre.
        return Timecode.FromFrame(tc.ToFrame(Rate) + 2, Rate);
    }

    /// <summary>Fullbilde-melding (ved start og etter spoling). Andre sysex-meldinger gir null.</summary>
    public Timecode? FullFrame(byte[]? b)
    {
        if (b is not { Length: >= 10 } || b[0] != 0xF0 || b[1] != 0x7F || b[3] != 0x01 || b[4] != 0x01) return null;
        Rate = Mtc.FromRateCode((b[5] >> 5) & 3);
        _expected = 0;
        return new Timecode(b[5] & 0x1F, b[6], b[7], b[8]);
    }
}

/// <summary>Lineær tidskode (LTC) som lyd: 80 bit per bilde, bifasekoding. Brukes av tidskode-cuen.</summary>
public static class Ltc
{
    /// <summary>De 80 bitene for ett bilde (bit 0 først). Brukerbiter er 0.</summary>
    public static bool[] Bits(Timecode tc, TimecodeRate r)
    {
        var b = new bool[80];
        void Put(int start, int count, int value) { for (int i = 0; i < count; i++) b[start + i] = ((value >> i) & 1) == 1; }
        Put(0, 4, tc.Frames % 10); Put(8, 2, tc.Frames / 10);
        b[10] = r == TimecodeRate.Fps2997Drop;
        Put(16, 4, tc.Seconds % 10); Put(24, 3, tc.Seconds / 10);
        Put(32, 4, tc.Minutes % 10); Put(40, 3, tc.Minutes / 10);
        Put(48, 4, tc.Hours % 10); Put(56, 2, tc.Hours / 10);
        // Synkord 0011 1111 1111 1101 (bit 64–79).
        for (int i = 66; i <= 77; i++) b[i] = true;
        b[79] = true;
        // Polaritetsbit: gjør antall 0-biter partall, så hvert bilde starter med samme polaritet.
        int zeros = b.Count(x => !x);
        if (zeros % 2 == 1) b[r == TimecodeRate.Fps25 ? 59 : 27] = true;
        return b;
    }

    /// <summary>Leser tidskoden fra 80 bit (null hvis synkordet mangler).</summary>
    public static Timecode? Decode(IReadOnlyList<bool> b)
    {
        if (b.Count < 80 || b[64] || b[65] || b[78] || !b[79]) return null;
        for (int i = 66; i <= 77; i++) if (!b[i]) return null;
        int Get(int start, int count) { int v = 0; for (int i = 0; i < count; i++) if (b[start + i]) v |= 1 << i; return v; }
        return new Timecode(Get(48, 4) + 10 * Get(56, 2), Get(32, 4) + 10 * Get(40, 3), Get(16, 4) + 10 * Get(24, 3), Get(0, 4) + 10 * Get(8, 2));
    }
}
