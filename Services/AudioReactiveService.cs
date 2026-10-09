using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DeskCanvas.Services;

internal sealed record AudioFrame(DateTime TimestampUtc, float Rms, float Peak, IReadOnlyList<float> Spectrum);

/// <summary>Captures system playback through WASAPI loopback and publishes a small audio spectrum.</summary>
internal sealed class AudioReactiveService : IDisposable
{
    private const int FftSize = 512;
    private const int BandCount = 32;
    private readonly object _gate = new();
    private readonly float[] _ring = new float[FftSize * 4];
    private readonly float[] _fftInput = new float[FftSize];
    private int _ringCount;
    private WasapiLoopbackCapture? _capture;
    private System.Threading.Timer? _publishTimer;
    private AudioFrame _latest = new(DateTime.UtcNow, 0, 0, new float[BandCount]);
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");
    private bool _running;
    public bool IsRunning => _running;
    public event EventHandler<AudioFrame>? FrameAvailable;

    public bool TryStart()
    {
        if (_running) return true;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _capture = new WasapiLoopbackCapture(device);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            _publishTimer = new System.Threading.Timer(PublishLatest, null, 33, 33);
            _running = true;
            return true;
        }
        catch (Exception ex)
        {
            StartupLog.Write("系统音频响应不可用，壁纸将继续运行", ex);
            Stop();
            return false;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_capture is null || e.BytesRecorded == 0) return;
        WaveFormat format = _capture.WaveFormat;
        int bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
        int frameSize = Math.Max(1, bytesPerSample * format.Channels);
        int sampleCount = e.BytesRecorded / frameSize;
        lock (_gate)
        {
            for (int i = 0; i < sampleCount; i++)
            {
                float sum = 0;
                for (int channel = 0; channel < format.Channels; channel++)
                {
                    int offset = i * frameSize + channel * bytesPerSample;
                    sum += ReadSample(e.Buffer, offset, format);
                }
                AppendSample(sum / format.Channels);
            }
            if (_ringCount < FftSize) return;
            int start = (_ring.Length + (_ringCount - FftSize)) % _ring.Length;
            for (int i = 0; i < FftSize; i++) _fftInput[i] = _ring[(start + i) % _ring.Length];
            _latest = Analyze(_fftInput);
        }
    }

    private void AppendSample(float sample)
    {
        int index = _ringCount % _ring.Length;
        _ring[index] = Math.Clamp(sample, -1f, 1f);
        _ringCount++;
    }

    private static float ReadSample(byte[] buffer, int offset, WaveFormat format)
    {
        if (format.BitsPerSample == 32)
        {
            bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                format is WaveFormatExtensible extensible && extensible.SubFormat == IeeeFloatSubFormat;
            int value = BitConverter.ToInt32(buffer, offset);
            return isFloat ? BitConverter.Int32BitsToSingle(value) : value / 2147483648f;
        }
        if (format.BitsPerSample == 16) return BitConverter.ToInt16(buffer, offset) / 32768f;
        if (format.BitsPerSample == 24)
        {
            int value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
            if ((value & 0x800000) != 0) value |= unchecked((int)0xff000000);
            return value / 8388608f;
        }
        return 0;
    }

    private static AudioFrame Analyze(float[] samples)
    {
        double energy = 0;
        float peak = 0;
        var real = new double[FftSize];
        var imaginary = new double[FftSize];
        for (int i = 0; i < FftSize; i++)
        {
            float sample = samples[i];
            energy += sample * sample;
            peak = Math.Max(peak, Math.Abs(sample));
            real[i] = sample * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1)));
        }
        for (int i = 1, j = 0; i < FftSize; i++)
        {
            int bit = FftSize >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
        }
        for (int length = 2; length <= FftSize; length <<= 1)
        {
            double angle = -2 * Math.PI / length;
            for (int start = 0; start < FftSize; start += length)
            {
                for (int i = 0; i < length / 2; i++)
                {
                    int even = start + i;
                    int odd = even + length / 2;
                    double phase = angle * i;
                    double cosine = Math.Cos(phase), sine = Math.Sin(phase);
                    double tr = real[odd] * cosine - imaginary[odd] * sine;
                    double ti = real[odd] * sine + imaginary[odd] * cosine;
                    real[odd] = real[even] - tr;
                    imaginary[odd] = imaginary[even] - ti;
                    real[even] += tr;
                    imaginary[even] += ti;
                }
            }
        }
        var bands = new float[BandCount];
        for (int band = 0; band < BandCount; band++)
        {
            int from = 1 + band * (FftSize / 2 - 1) / BandCount;
            int to = 1 + (band + 1) * (FftSize / 2 - 1) / BandCount;
            double sum = 0;
            for (int i = from; i < to; i++) sum += Math.Sqrt(real[i] * real[i] + imaginary[i] * imaginary[i]) / FftSize;
            bands[band] = (float)Math.Clamp(sum / Math.Max(1, to - from) * 8, 0, 1);
        }
        return new AudioFrame(DateTime.UtcNow, (float)Math.Clamp(Math.Sqrt(energy / FftSize) * 2, 0, 1), peak, bands);
    }

    private void PublishLatest(object? state)
    {
        AudioFrame frame;
        lock (_gate) frame = _latest;
        try { FrameAvailable?.Invoke(this, frame); }
        catch (Exception ex) { StartupLog.Write("音频响应事件处理失败", ex); }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null) StartupLog.Write("系统音频捕获已停止", e.Exception);
    }

    public void Stop()
    {
        _running = false;
        _publishTimer?.Dispose();
        _publishTimer = null;
        if (_capture is not null)
        {
            try { _capture.DataAvailable -= OnDataAvailable; _capture.RecordingStopped -= OnRecordingStopped; _capture.StopRecording(); } catch { }
            _capture.Dispose();
            _capture = null;
        }
    }

    public void Dispose() => Stop();
}
