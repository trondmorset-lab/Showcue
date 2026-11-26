namespace ShowCue.Models;

/// <summary>
/// Stopper en løpsk kjede av «gå til»-cuer som starter hverandre uten at det går tid (for eksempel to cuer som peker på hverandre).
/// Hver kjøring av en «gå til»-cue som starter målet meldes til <see cref="Allow"/>; kommer mer enn <see cref="MaxBurst"/> tett på
/// hverandre (hver innen <see cref="WindowMs"/> etter forrige), avvises resten. Vanlige sløyfer med lyd eller pause imellom påvirkes ikke.
/// Ren logikk: tiden gis inn av kalleren (stigende millisekunder).
/// </summary>
public sealed class GotoLoopGuard
{
    public const int MaxBurst = 25;
    public const long WindowMs = 200;

    private long _last;
    private bool _any;
    private int _count;

    /// <summary>True hvis «gå til» kan utføres nå.</summary>
    public bool Allow(long nowMs)
    {
        if (!_any || nowMs < _last || unchecked((ulong)(nowMs - _last)) > (ulong)WindowMs) _count = 0; // uten overflyt, selv med ekstreme tider
        _any = true;
        _last = nowMs;
        if (_count < int.MaxValue) _count++;
        return _count <= MaxBurst;
    }

    public void Reset() { _any = false; _count = 0; }
}
