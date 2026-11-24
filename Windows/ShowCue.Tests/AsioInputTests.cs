using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.Asio;
using ShowCue.Engine;
using ShowCue.Models;

namespace ShowCue.Tests;

/// <summary>ASIO-innganger: regler, innstillinger og ringbuffer-flyt uten maskinvare.</summary>
public class AsioInputTests
{
    // ---------------- Valg av kanaler ----------------

    [Fact]
    public void PlannerIsOffWhenDisabledAndReportsMissingInputs()
    {
        Assert.Equal(AsioInputStatus.Off, AsioInputPlanner.Plan(false, 32, 1, 2).Status);
        Assert.Equal(AsioInputStatus.NoDriverInputs, AsioInputPlanner.Plan(true, 0, 1, 2).Status);
        Assert.Equal(AsioInputStatus.NoDriverInputs, AsioInputPlanner.Plan(true, -3, 1, 2).Status);
    }

    [Fact]
    public void PlannerUsesRequestedChannelsZeroBased()
    {
        var p = AsioInputPlanner.Plan(true, 32, 5, 4);
        Assert.Equal(new AsioInputPlan(AsioInputStatus.Ok, 4, 4), p);
    }

    [Theory]
    [InlineData(32, 31, 4, 30, 2)]   // nær slutten: antallet krympes så det passer
    [InlineData(32, 99, 2, 31, 1)]   // første kanal utenfor driveren: siste inngang
    [InlineData(8, 1, 64, 0, 8)]     // alle inngangene
    [InlineData(8, 0, 0, 0, 1)]      // ugyldige verdier: første kanal og minst én
    [InlineData(8, -5, -5, 0, 1)]
    [InlineData(256, 1, 1000, 0, 64)] // aldri flere enn grensa
    public void PlannerClampsToDriver(int driver, int first, int count, int expectedFirst, int expectedCount)
    {
        var p = AsioInputPlanner.Plan(true, driver, first, count);
        Assert.Equal(AsioInputStatus.Ok, p.Status);
        Assert.Equal(expectedFirst, p.FirstChannel);
        Assert.Equal(expectedCount, p.Channels);
        Assert.InRange(p.FirstChannel + p.Channels, 1, driver);
    }

    // ---------------- Innstillinger ----------------

    [Fact]
    public void AsioInputIsOffByDefaultAndOutputPathIsUnchanged()
    {
        var s = new AppSettings();
        Assert.False(s.AsioInputEnabled);
        Assert.Equal(1, s.AsioInputFirstChannel);
        Assert.Equal(2, s.AsioInputChannels);
        Assert.Equal(AudioDriverType.Wasapi, s.AudioDriver);
    }

    [Fact]
    public void AsioInputSettingsRoundTripAndOldFilesGetDefaults()
    {
        var s = new AppSettings { AsioInputEnabled = true, AsioInputFirstChannel = 9, AsioInputChannels = 8 };
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, ShowDocument.JsonOptions), ShowDocument.JsonOptions)!;
        Assert.True(back.AsioInputEnabled);
        Assert.Equal(9, back.AsioInputFirstChannel);
        Assert.Equal(8, back.AsioInputChannels);

        var old = JsonSerializer.Deserialize<AppSettings>("{\"AsioDriverName\":\"X\"}", ShowDocument.JsonOptions)!;
        Assert.False(old.AsioInputEnabled);
        Assert.Equal(1, old.AsioInputFirstChannel);
        Assert.Equal(2, old.AsioInputChannels);
    }

    // ---------------- Kanalomforming ----------------

    [Fact]
    public void RemapKeepsCommonChannelsAndPadsWithSilence()
    {
        float[] src = { 1, 2, 3, 4, 5, 6 }; // 2 rammer à 3 kanaler
        var two = new float[4];
        ChannelMap.Remap(src, 3, two, 2, 2);
        Assert.Equal(new float[] { 1, 2, 4, 5 }, two);

        var four = new float[8];
        Array.Fill(four, 9f);
        ChannelMap.Remap(src, 3, four, 4, 2);
        Assert.Equal(new float[] { 1, 2, 3, 0, 4, 5, 6, 0 }, four);
    }

    // ---------------- Ringbuffer-flyt ----------------

    [Fact]
    public void FeedWritesToEveryListenerUntilClosed()
    {
        var feed = new AsioInputFeed(2, 48000);
        feed.PrepareBuffer(64);
        var a = feed.Open();
        var b = feed.Open();
        Assert.Equal(2, feed.ListenerCount);
        feed.Write(new float[] { 0.1f, 0.2f, 0.3f, 0.4f }, 2);
        Assert.Equal(2, a.BufferedFrames);
        Assert.Equal(2, b.BufferedFrames);

        feed.Close(a);
        feed.Close(a); // dobbel lukking er ufarlig
        Assert.Equal(1, feed.ListenerCount);
        feed.Write(new float[] { 0.5f, 0.6f }, 2);
        Assert.Equal(2, a.BufferedFrames);
        Assert.Equal(3, b.BufferedFrames);
    }

    [Fact]
    public void FeedWithoutListenersOrWithBadInputDoesNothing()
    {
        var feed = new AsioInputFeed(2, 48000);
        feed.PrepareBuffer(16);
        feed.Write(new float[] { 1, 2 }, 2);   // ingen lyttere
        var r = feed.Open();
        feed.Write(new float[] { 1, 2 }, 0);   // ugyldig kanalantall
        feed.Write(ReadOnlySpan<float>.Empty, 2);
        feed.Write(new float[] { 1 }, 2);       // mindre enn én ramme
        Assert.Equal(0, r.BufferedFrames);
    }

    [Fact]
    public void FeedAdaptsBlocksWithOtherChannelCount()
    {
        var feed = new AsioInputFeed(2, 48000);
        feed.PrepareBuffer(8);
        var r = feed.Open();
        feed.Write(new float[] { 1, 2, 3, 4, 5, 6 }, 3); // 2 rammer à 3 kanaler → 2 kanaler
        var dst = new float[4];
        r.Read(dst, 0, 4);
        Assert.Equal(new float[] { 1, 2, 4, 5 }, dst);

        feed.Write(new float[16 * 2], 2);
        Assert.Equal(0, feed.DroppedBlocks);
        feed.Write(new float[16 * 3], 3);      // større enn forhåndslaget buffer: forkastes, ingen tildeling
        Assert.Equal(1, feed.DroppedBlocks);
    }

    [Fact]
    public void RingToleratesBlocksLargerThanDefaultLatency()
    {
        var feed = new AsioInputFeed(1, 48000);
        feed.PrepareBuffer(8192);            // 170 ms per blokk
        var r = feed.Open();
        feed.Write(Enumerable.Repeat(0.25f, 8192).ToArray(), 1);
        Assert.Equal(8192, r.BufferedFrames); // ikke kastet bort
    }

    [Fact]
    public void FeedFlowsThroughLiveInputStream()
    {
        var feed = new AsioInputFeed(2, 48000);
        feed.PrepareBuffer(32);
        var ring = feed.Open();
        var stream = new LiveInputStream(ring, feed.SampleRate, feed.Close);
        feed.Write(new float[] { 0.5f, -0.5f, 0.25f, -0.25f }, 2);
        var buf = new float[6];
        Assert.Equal(6, stream.Read(buf, 0, 6));
        Assert.Equal(new float[] { 0.5f, -0.5f, 0.25f, -0.25f, 0, 0 }, buf); // resten er stillhet
        Assert.Equal(2, stream.WaveFormat.Channels);
        stream.Dispose();
        Assert.Equal(0, feed.ListenerCount);
    }

    // ---------------- Driverhendelsen (med lagde bufferne i stedet for en driver) ----------------

    private static AsioAudioAvailableEventArgs Args(AsioSampleType type, int frames, params byte[][] channels)
    {
        var ptrs = channels.Select(c => { var p = Marshal.AllocHGlobal(c.Length); Marshal.Copy(c, 0, p, c.Length); return p; }).ToArray();
        return new AsioAudioAvailableEventArgs(ptrs, Array.Empty<IntPtr>(), frames, type);
    }

    private static byte[] Floats(params float[] v) { var b = new byte[v.Length * 4]; Buffer.BlockCopy(v, 0, b, 0, b.Length); return b; }
    private static byte[] Shorts(params short[] v) { var b = new byte[v.Length * 2]; Buffer.BlockCopy(v, 0, b, 0, b.Length); return b; }

    [Fact]
    public void DriverEventDeliversFloatSamplesInterleaved()
    {
        var feed = new AsioInputFeed(2, 48000);
        feed.PrepareBuffer(4);
        var ring = feed.Open();
        feed.OnAudioAvailable(null, Args(AsioSampleType.Float32LSB, 2, Floats(0.1f, 0.2f), Floats(-0.1f, -0.2f)));
        var dst = new float[4];
        ring.Read(dst, 0, 4);
        Assert.Equal(new float[] { 0.1f, -0.1f, 0.2f, -0.2f }, dst);
        Assert.False(feed.Failed);
    }

    [Fact]
    public void DriverEventConvertsSixteenBitIntegers()
    {
        var feed = new AsioInputFeed(1, 48000);
        feed.PrepareBuffer(4);
        var ring = feed.Open();
        feed.OnAudioAvailable(null, Args(AsioSampleType.Int16LSB, 2, Shorts(short.MaxValue, 0)));
        var dst = new float[2];
        ring.Read(dst, 0, 2);
        Assert.Equal(1.0, (double)dst[0], 3);
        Assert.Equal(0f, dst[1]);
    }

    [Fact]
    public void UnsupportedDriverFormatMarksFeedFailedInsteadOfThrowing()
    {
        var feed = new AsioInputFeed(1, 48000);
        feed.PrepareBuffer(4);
        feed.Open();
        feed.OnAudioAvailable(null, Args(AsioSampleType.Int32MSB, 2, new byte[8]));
        Assert.True(feed.Failed);
        feed.OnAudioAvailable(null, Args(AsioSampleType.Float32LSB, 2, Floats(1, 1))); // videre blokker ignoreres
    }

    [Fact]
    public void DriverBlockLargerThanPreparedBufferIsDropped()
    {
        var feed = new AsioInputFeed(1, 48000);
        feed.PrepareBuffer(2);
        var ring = feed.Open();
        feed.OnAudioAvailable(null, Args(AsioSampleType.Float32LSB, 4, Floats(1, 1, 1, 1)));
        Assert.Equal(0, ring.BufferedFrames);
        Assert.Equal(1, feed.DroppedBlocks);
        Assert.False(feed.Failed);
    }

    // ---------------- Valg av kilde ----------------

    [Fact]
    public void OpenForUsesAsioFeedAndReleasesIt()
    {
        using var input = new AudioInput();
        var feed = new AsioInputFeed(4, 48000);
        feed.PrepareBuffer(32);
        var (ring, release) = input.OpenFor(true, feed, "", "", 48000);
        Assert.Equal(4, ring.Channels);
        Assert.Equal(1, feed.ListenerCount);
        release(ring);
        Assert.Equal(0, feed.ListenerCount);
    }

    [Fact]
    public void OpenForGivesClearErrorsInsteadOfFallingBackToAnotherDevice()
    {
        using var input = new AudioInput();
        var ex = Assert.Throws<InvalidOperationException>(() => input.OpenFor(true, null, "Ingen innganger", "", 48000));
        Assert.Contains("Ingen innganger", ex.Message);
        Assert.Throws<InvalidOperationException>(() => input.OpenFor(true, null, "", "", 48000));

        var feed = new AsioInputFeed(1, 48000);
        feed.PrepareBuffer(4);
        feed.Open();
        feed.OnAudioAvailable(null, Args(AsioSampleType.Int32MSB, 1, new byte[4]));
        Assert.Throws<InvalidOperationException>(() => input.OpenFor(true, feed, "", "", 48000));
    }

    [Fact]
    public void ChannelsForFollowsAsioFeed()
    {
        Assert.Equal(0, AudioInput.ChannelsFor(true, null, ""));
        Assert.Equal(6, AudioInput.ChannelsFor(true, new AsioInputFeed(6, 48000), ""));
    }
}
