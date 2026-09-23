using NAudio.CoreAudioApi;
using NAudio.Wave;
using SenseVoiceInput.Core;
namespace SenseVoiceInput.Windows;

/// <summary>Start/Stop and delivery are dispatcher-owned; only frame processing runs on WASAPI's thread.</summary>
public sealed class WasapiVadService(Func<string?> deviceId, Func<string> modelPath, Action<Action> dispatch) : IVoiceActivityDetector, IDisposable
{
    private Run? current;
    private long samplesProcessed;
    public long SamplesProcessed => Interlocked.Read(ref samplesProcessed);
    public bool IsAvailable => File.Exists(modelPath());
    public event Action? SpeechStarted;
    public event Action<AudioData>? SegmentReady;
    public event Action<Exception>? Failed;
    public void Start(int silenceTimeoutMs)
    {
        if (current != null) return;
        var run = new Run();
        try
        {
            run.Engine = new(modelPath(), silenceTimeoutMs);
            using var enumerator = new MMDeviceEnumerator();
            run.Device = deviceId() is { } id ? enumerator.GetDevice(id) : enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            run.Capture = new(run.Device);
            run.Capture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(run.Capture.WaveFormat.SampleRate, run.Capture.WaveFormat.Channels);
            run.Resampler = new(run.Capture.WaveFormat.SampleRate);
            run.Capture.DataAvailable += (_, e) => OnData(run, e);
            run.Capture.RecordingStopped += (_, e) =>
            {
                if (!run.Cancelled && !run.Completed) Post(run, () => Failed?.Invoke(e.Exception ?? new IOException("自動録音のマイクが停止しました。")));
            };
            current = run;
            run.Capture.StartRecording();
        }
        catch { current = null; run.Dispose(); throw; }
    }
    private void Post(Run run, Action action) => dispatch(() => { if (ReferenceEquals(current, run) && !run.Cancelled) action(); });
    private void OnData(Run run, WaveInEventArgs e)
    {
        if (run.Cancelled || run.Completed) return;
        float[]? mono = null, samples = null;
        try
        {
            mono = PcmConverter.ToMono(e.Buffer.AsSpan(0, e.BytesRecorded), run.Capture!.WaveFormat.Channels, 32, true);
            samples = run.Resampler!.Convert(mono);
            var result = run.Engine!.Accept(samples);
            Interlocked.Add(ref samplesProcessed, samples.Length);
            if (!run.Announced && (result.Speech || result.Segment != null))
            { run.Announced = true; Post(run, () => SpeechStarted?.Invoke()); }
            if (result.Segment is { } segment)
            {
                run.Completed = true; run.Pending = segment;
                Post(run, () =>
                {
                    run.Pending = null;
                    if (SegmentReady is { } handler) handler(segment); else Array.Clear(segment.Samples);
                });
            }
        }
        catch (Exception error) { run.Completed = true; Post(run, () => Failed?.Invoke(error)); }
        finally { if (mono != null) Array.Clear(mono); if (samples != null) Array.Clear(samples); }
    }
    public void Stop() { var run = current; current = null; run?.Dispose(); }
    public void Dispose() => Stop();
    private sealed class Run : IDisposable
    {
        public WasapiCapture? Capture;
        public MMDevice? Device;
        public SileroVadEngine? Engine;
        public VadResampler? Resampler;
        public volatile bool Cancelled, Completed;
        public bool Announced;
        public AudioData? Pending;
        public void Dispose()
        {
            Cancelled = true;
            // Joining capture first ensures no native inference or buffer writes remain.
            Capture?.Dispose(); Capture = null;
            Device?.Dispose(); Device = null;
            Engine?.Dispose(); Engine = null;
            Resampler = null;
            if (Pending != null) { Array.Clear(Pending.Samples); Pending = null; }
        }
    }
}
