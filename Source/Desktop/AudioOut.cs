using System.Runtime.InteropServices;

namespace SubWake;

/// <summary>One sine component of a burst.</summary>
readonly record struct Tone(double Frequency, double LevelDb)
{
    public bool IsLow => Frequency < 1000;
}

enum PlaybackProblem { Missing, Busy, Failed }

/// <summary>A burst that couldn't play, with the reason the status shows.</summary>
sealed class PlaybackException(PlaybackProblem problem, string message, Exception? inner = null) : Exception(message, inner)
{
    public PlaybackProblem Problem { get; } = problem;

    public static string StatusText(PlaybackProblem problem) => problem switch
    {
        PlaybackProblem.Missing => "Output not found",
        PlaybackProblem.Busy => "Output busy",
        _ => "Playback problem",
    };
}

/// <summary>
/// Plays bursts through Windows' shared-mode audio (WASAPI), mixed with other audio, at the output's own
/// sample rate. The output is found by its endpoint ID, with its name as a fallback.
/// </summary>
static class AudioOut
{
    const double FadeSeconds = 0.2;              // Raised-cosine fade in/out so bursts never click.
    const long BufferDuration = 2_000_000;       // 200 ms, in 100 ns units.
    const int AutoConvertPcm = unchecked((int)0x80000000), SrcDefaultQuality = 0x08000000;
    static readonly Guid FloatFormat = new("00000003-0000-0010-8000-00aa00389b71");
    static readonly Guid PcmFormat = new("00000001-0000-0010-8000-00aa00389b71");

    /// <summary>
    /// One channel of samples. The low tones are scaled together so their combined peak never goes above
    /// their set level; the high tone is added on top at its own fixed level.
    /// </summary>
    public static float[] MakeBurst(double seconds, IReadOnlyList<Tone> tones, int sampleRate)
    {
        int frames = (int)(seconds * sampleRate);
        int fade = Math.Max(1, (int)(FadeSeconds * sampleRate));
        var low = tones.Where(t => t.IsLow).ToArray();
        var high = tones.Where(t => !t.IsLow).ToArray();
        double lowTarget = low.Length > 0 ? low.Max(t => Amplitude(t.LevelDb)) : 0;

        var lowMix = new double[frames];
        double lowPeak = 0;
        for (int i = 0; i < frames; i++)
        {
            double value = 0;
            foreach (var t in low) value += Amplitude(t.LevelDb) * Math.Sin(2 * Math.PI * t.Frequency * i / sampleRate);
            lowMix[i] = value;
            lowPeak = Math.Max(lowPeak, Math.Abs(value));
        }
        double scale = lowPeak > lowTarget && lowPeak > 0 ? lowTarget / lowPeak : 1;

        var buffer = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            double envelope = 1;
            if (i < fade) envelope = 0.5 - 0.5 * Math.Cos(Math.PI * i / fade);
            int fromEnd = frames - 1 - i;
            if (fromEnd < fade) envelope = Math.Min(envelope, 0.5 - 0.5 * Math.Cos(Math.PI * fromEnd / fade));
            double value = lowMix[i] * scale;
            foreach (var t in high) value += Amplitude(t.LevelDb) * Math.Sin(2 * Math.PI * t.Frequency * i / sampleRate);
            buffer[i] = (float)Math.Clamp(value * envelope, -1, 1);
        }
        return buffer;
    }

    static double Amplitude(double db) => Math.Pow(10, db / 20);

    /// <summary>Plays a burst and blocks until it has finished. Run it off the UI thread. Returns the output used.</summary>
    public static AudioDevice Play(string? deviceId, string deviceName, Func<int, float[]> makeSamples)
    {
        var enumerator = CoreAudio.CreateEnumerator();
        IMMDevice? device = null;
        IAudioClient? client = null;
        IAudioRenderClient? render = null;
        IntPtr mixFormat = IntPtr.Zero, ownFormat = IntPtr.Zero;
        AudioDevice? found = null;
        try
        {
            var endpoints = CoreAudio.Endpoints(enumerator, CoreAudio.Render);
            found = Devices.Resolve(endpoints.Select(e => e.Info).ToList(), deviceId, deviceName);
            foreach (var (info, d) in endpoints)
                if (info == found) device = d; else CoreAudio.Release(d);
            if (found == null || device == null)
                throw new PlaybackException(PlaybackProblem.Missing, $"Output '{deviceName}' not found. Is it connected?");

            client = CoreAudio.Activate<IAudioClient>(device);
            CoreAudio.Check(client.GetMixFormat(out mixFormat));
            var format = Format.Read(mixFormat);
            IntPtr useFormat = mixFormat;
            int flags = 0;
            if (!format.IsFloat32 && !format.IsPcm16)
            {
                // Unusual mix format: hand Windows plain float stereo and let it convert.
                ownFormat = Format.FloatStereo(format.SampleRate);
                useFormat = ownFormat;
                flags = AutoConvertPcm | SrcDefaultQuality;
                format = Format.Read(ownFormat);
            }
            Check(client.Initialize(0 /* shared */, flags, BufferDuration, 0, useFormat, IntPtr.Zero), found.Name);
            Check(client.GetBufferSize(out uint bufferFrames), found.Name);
            var renderId = typeof(IAudioRenderClient).GUID;
            Check(client.GetService(ref renderId, out object service), found.Name);
            render = (IAudioRenderClient)service;

            float[] samples = makeSamples(format.SampleRate);
            var deadline = DateTime.UtcNow.AddSeconds((double)samples.Length / format.SampleRate + 5);
            int written = 0;
            bool started = false;
            while (written < samples.Length)
            {
                Check(client.GetCurrentPadding(out uint padding), found.Name);
                int count = Math.Min((int)(bufferFrames - padding), samples.Length - written);
                if (count > 0)
                {
                    Check(render.GetBuffer((uint)count, out IntPtr data), found.Name);
                    Write(data, samples, written, count, format);
                    Check(render.ReleaseBuffer((uint)count, 0), found.Name);
                    written += count;
                }
                if (!started) { Check(client.Start(), found.Name); started = true; }
                else Thread.Sleep(15);
                if (DateTime.UtcNow > deadline) throw new PlaybackException(PlaybackProblem.Failed, "The output didn't finish playing the burst.");
            }
            // Let the buffer play out.
            while (client.GetCurrentPadding(out uint left) == 0 && left > 0 && DateTime.UtcNow < deadline) Thread.Sleep(15);
            client.Stop();
            return found;
        }
        catch (COMException ex) { throw Problem(ex.HResult, found?.Name ?? deviceName, ex); }
        catch (InvalidCastException ex) { throw new PlaybackException(PlaybackProblem.Failed, "Playback failed: " + ex.Message, ex); }
        finally
        {
            CoreAudio.Release(render);
            CoreAudio.Release(client);
            CoreAudio.Release(device);
            CoreAudio.Release(enumerator);
            if (mixFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(mixFormat);
            if (ownFormat != IntPtr.Zero) Marshal.FreeHGlobal(ownFormat);
        }
    }

    static void Check(int hr, string name) { if (hr < 0) throw Problem(hr, name, null); }

    /// <summary>Turns a Windows audio error into the reason shown in the window.</summary>
    public static PlaybackException Problem(int hr, string name, Exception? inner) => unchecked((uint)hr) switch
    {
        0x8889000A or 0x88890012 => new PlaybackException(PlaybackProblem.Busy, $"'{name}' is in use by another app in exclusive mode.", inner),
        0x88890004 or 0x80070490 => new PlaybackException(PlaybackProblem.Missing, $"Output '{name}' not found. Is it connected?", inner),
        0x88890010 => new PlaybackException(PlaybackProblem.Failed, "The Windows audio service isn't running.", inner),
        _ => new PlaybackException(PlaybackProblem.Failed, $"Playback failed on '{name}' (error 0x{hr:X8}).", inner),
    };

    static void Write(IntPtr data, float[] samples, int offset, int frames, Format format)
    {
        int channels = format.Channels;
        if (format.IsFloat32)
        {
            var chunk = new float[frames * channels];
            for (int i = 0; i < frames; i++)
                for (int c = 0; c < channels; c++) chunk[i * channels + c] = samples[offset + i];
            Marshal.Copy(chunk, 0, data, chunk.Length);
        }
        else
        {
            var chunk = new short[frames * channels];
            for (int i = 0; i < frames; i++)
            {
                short value = (short)Math.Round(samples[offset + i] * short.MaxValue);
                for (int c = 0; c < channels; c++) chunk[i * channels + c] = value;
            }
            Marshal.Copy(chunk, 0, data, chunk.Length);
        }
    }

    /// <summary>The parts of a WAVEFORMATEX(TENSIBLE) the app needs.</summary>
    readonly record struct Format(int Channels, int SampleRate, int Bits, bool Float, bool Pcm)
    {
        public bool IsFloat32 => Float && Bits == 32;
        public bool IsPcm16 => Pcm && Bits == 16;

        public static Format Read(IntPtr p)
        {
            int tag = (ushort)Marshal.ReadInt16(p, 0);
            int channels = (ushort)Marshal.ReadInt16(p, 2);
            int rate = Marshal.ReadInt32(p, 4);
            int bits = (ushort)Marshal.ReadInt16(p, 14);
            int extra = (ushort)Marshal.ReadInt16(p, 16);
            bool isFloat = tag == 3, isPcm = tag == 1;
            if (tag == 0xFFFE && extra >= 22)
            {
                var bytes = new byte[16];
                Marshal.Copy(p + 24, bytes, 0, 16);
                var sub = new Guid(bytes);
                isFloat = sub == FloatFormat;
                isPcm = sub == PcmFormat;
            }
            return new Format(channels, rate, bits, isFloat, isPcm);
        }

        public static IntPtr FloatStereo(int rate)
        {
            IntPtr p = Marshal.AllocHGlobal(18);
            Marshal.WriteInt16(p, 0, 3);              // WAVE_FORMAT_IEEE_FLOAT
            Marshal.WriteInt16(p, 2, 2);
            Marshal.WriteInt32(p, 4, rate);
            Marshal.WriteInt32(p, 8, rate * 8);
            Marshal.WriteInt16(p, 12, 8);
            Marshal.WriteInt16(p, 14, 32);
            Marshal.WriteInt16(p, 16, 0);
            return p;
        }
    }
}
