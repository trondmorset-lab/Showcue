using NAudio.Wave;

namespace ShowCue.Engine;

public enum AsioInputStatus { Off, NoDriverInputs, Ok }

/// <summary>Valgt utsnitt av driverens innganger: første kanal (fra 0) og antall.</summary>
public readonly record struct AsioInputPlan(AsioInputStatus Status, int FirstChannel, int Channels);

/// <summary>Regler for hvilke ASIO-innganger som åpnes. Ingen maskinvare her, så reglene kan testes.</summary>
public static class AsioInputPlanner
{
    /// <summary>Flest innganger ShowCue henter fra driveren.</summary>
    public const int MaxChannels = 64;
    /// <summary>Største «første kanal» (fra 1) som godtas i innstillingene.</summary>
    public const int MaxFirstChannel = 256;

    public static int ClampFirst(int firstChannel) => Math.Clamp(firstChannel, 1, MaxFirstChannel);
    public static int ClampCount(int channels) => Math.Clamp(channels, 1, MaxChannels);

    /// <param name="enabled">Innstillingen «Bruk ASIO-innganger».</param>
    /// <param name="driverInputs">Antall innganger driveren melder.</param>
    /// <param name="firstChannel">Første kanal som skal brukes, talt fra 1 som i brukerflaten.</param>
    /// <param name="channels">Ønsket antall kanaler.</param>
    public static AsioInputPlan Plan(bool enabled, int driverInputs, int firstChannel, int channels)
    {
        if (!enabled) return new(AsioInputStatus.Off, 0, 0);
        if (driverInputs < 1) return new(AsioInputStatus.NoDriverInputs, 0, 0);
        int first = Math.Min(ClampFirst(firstChannel), driverInputs) - 1;
        int count = Math.Min(ClampCount(channels), driverInputs - first);
        return new(AsioInputStatus.Ok, first, count);
    }
}

/// <summary>Omforming mellom kanalantall i flettede blokker.</summary>
public static class ChannelMap
{
    /// <summary>Kopierer <paramref name="frames"/> rammer fra <paramref name="srcChannels"/> til <paramref name="dstChannels"/> kanaler:
    /// kanalene de har felles følger hverandre (kanal 1 til kanal 1 osv.), resten fylles med stillhet.</summary>
    public static void Remap(ReadOnlySpan<float> src, int srcChannels, Span<float> dst, int dstChannels, int frames)
    {
        int common = Math.Min(srcChannels, dstChannels);
        for (int f = 0; f < frames; f++)
        {
            int s = f * srcChannels, d = f * dstChannels;
            for (int c = 0; c < common; c++) dst[d + c] = src[s + c];
            for (int c = common; c < dstChannels; c++) dst[d + c] = 0f;
        }
    }
}

/// <summary>Fører lyd fra ASIO-driverens innganger inn i ringbufferne til live-inngang-cuene.
/// Inngangene hentes fra samme ASIO-økt som utgangen (en ASIO-driver eies av én økt).
/// <see cref="OnAudioAvailable"/> kjører i driverens lydtråd og gjør ingen minnetildeling, låsing eller filtilgang:
/// bufferne lages på forhånd, og listen over ringbuffere byttes ut hele (aldri endret på stedet).
/// Ringbufferen leses i samme lydtråd rett etterpå, så den interne låsen i <see cref="LiveRing"/> er aldri i konflikt.</summary>
public sealed class AsioInputFeed
{
    private LiveRing[] _rings = Array.Empty<LiveRing>();
    private readonly object _gate = new(); // bare for Open/Close fra brukerflaten
    private float[] _buffer = Array.Empty<float>();
    private float[] _scratch = Array.Empty<float>();
    private int _blockFrames;
    private volatile bool _failed;
    private long _droppedBlocks;

    public int Channels { get; }
    public int SampleRate { get; }

    /// <summary>Driveren leverte et lydformat vi ikke kan lese; innganger er da stille.</summary>
    public bool Failed => _failed;
    /// <summary>Antall blokker som ble forkastet fordi de var større enn forhåndsallokert buffer.</summary>
    public long DroppedBlocks => Interlocked.Read(ref _droppedBlocks);
    public int ListenerCount => Volatile.Read(ref _rings).Length;

    public AsioInputFeed(int channels, int sampleRate)
    {
        Channels = Math.Clamp(channels, 1, AsioInputPlanner.MaxChannels);
        SampleRate = Math.Max(8000, sampleRate);
    }

    /// <summary>Lager bufferne for blokker på opptil <paramref name="framesPerBlock"/> rammer. Kalles før lyden startes.</summary>
    public void PrepareBuffer(int framesPerBlock)
    {
        _blockFrames = Math.Clamp(framesPerBlock, 1, 65536);
        _buffer = new float[_blockFrames * Channels];
        _scratch = new float[_blockFrames * Channels];
    }

    /// <summary>Ny lytter (én per live-cue). Ringbufferen tåler et par driverblokker.</summary>
    public LiveRing Open()
    {
        var ring = new LiveRing(Channels, SampleRate, Math.Max(0.1, 3.0 * _blockFrames / SampleRate));
        lock (_gate)
        {
            var old = _rings;
            var next = new LiveRing[old.Length + 1];
            Array.Copy(old, next, old.Length);
            next[^1] = ring;
            Volatile.Write(ref _rings, next);
        }
        return ring;
    }

    public void Close(LiveRing ring)
    {
        lock (_gate)
        {
            var old = _rings;
            int i = Array.IndexOf(old, ring);
            if (i < 0) return;
            var next = new LiveRing[old.Length - 1];
            Array.Copy(old, 0, next, 0, i);
            Array.Copy(old, i + 1, next, i, old.Length - i - 1);
            Volatile.Write(ref _rings, next);
        }
    }

    /// <summary>Skriver én blokk (flettet, <paramref name="channels"/> kanaler) til alle lyttere.
    /// Avviker kanalantallet fra det som ble åpnet, tilpasses blokka med <see cref="ChannelMap.Remap"/>.</summary>
    public void Write(ReadOnlySpan<float> interleaved, int channels)
    {
        var rings = Volatile.Read(ref _rings);
        if (rings.Length == 0 || channels < 1) return;
        int frames = interleaved.Length / channels;
        if (frames < 1) return;
        var data = interleaved[..(frames * channels)];
        if (channels != Channels)
        {
            var scratch = _scratch;
            if (scratch.Length < frames * Channels) { Interlocked.Increment(ref _droppedBlocks); return; }
            ChannelMap.Remap(data, channels, scratch, Channels, frames);
            data = scratch.AsSpan(0, frames * Channels);
        }
        for (int i = 0; i < rings.Length; i++) rings[i].Write(data);
    }

    /// <summary>Hendelsen fra ASIO-driveren når en blokk med innganger er klar (kjører i lydtråden).</summary>
    public void OnAudioAvailable(object? sender, AsioAudioAvailableEventArgs e)
    {
        if (_failed) return;
        try
        {
            int ch = e.InputBuffers.Length, frames = e.SamplesPerBuffer;
            if (ch < 1 || frames < 1) return;
            var buf = _buffer;
            if (buf.Length < frames * ch) { Interlocked.Increment(ref _droppedBlocks); return; }
            int n = e.GetAsInterleavedSamples(buf);
            Write(buf.AsSpan(0, n), ch);
        }
        catch
        {
            // Et lydformat som ikke kan leses (eller en feil i driveren) skal aldri velte lydtråden.
            _failed = true;
        }
    }
}
