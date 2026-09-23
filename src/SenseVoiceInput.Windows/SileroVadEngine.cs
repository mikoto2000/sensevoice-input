using System.Runtime.InteropServices;
using SherpaOnnx;
using SenseVoiceInput.Core;
namespace SenseVoiceInput.Windows;
public readonly record struct VadFrameResult(bool Speech, AudioData? Segment);
/// <summary>Single-thread-owned, checked native handles. Bounded to 30 seconds per segment.</summary>
public sealed class SileroVadEngine : IDisposable
{
    private nint handle;
    private readonly float[] frame = new float[512];
    private int frameCount;
    private readonly SpeechPreroll preroll = new(16000 * 65, 8000);
    private readonly Queue<AudioData> segments = new();
    public SileroVadEngine(string model, int silenceTimeoutMs)
    {
        if (!File.Exists(model)) throw new FileNotFoundException("Silero VADモデルがありません。download-vad-model.ps1を実行してください。");
        model = Path.GetFullPath(model);
        if (model.Any(c => c > 127)) throw new NotSupportedException("VADモデルにはASCII文字のみのパスを指定してください。");
        var config = new VadModelConfig();
        config.SampleRate = 16000; config.NumThreads = 1; config.Provider = "cpu";
        config.SileroVad.Model = model; config.SileroVad.Threshold = 0.5f;
        config.SileroVad.MinSilenceDuration = Math.Clamp(silenceTimeoutMs, 200, 5000) / 1000f;
        config.SileroVad.MinSpeechDuration = 0.25f; config.SileroVad.MaxSpeechDuration = 30; config.SileroVad.WindowSize = 512;
        handle = Native.Create(ref config, 65);
        if (handle == 0) throw new InvalidOperationException("Silero VADを初期化できません。");
    }
    public VadFrameResult Accept(float[] samples)
    {
        ObjectDisposedException.ThrowIf(handle == 0, this);
        // Fixed 32ms frames avoid native start positions depending on WASAPI packet size.
        int offset = 0;
        while (offset < samples.Length)
        {
            int count = Math.Min(frame.Length - frameCount, samples.Length - offset);
            Array.Copy(samples, offset, frame, frameCount, count);
            frameCount += count; offset += count;
            if (frameCount != frame.Length) continue;
            preroll.Append(frame);
            Native.Accept(handle, frame, frame.Length);
            frameCount = 0;
            while (Native.Empty(handle) != 1)
            {
                var pointer = Native.Front(handle);
                if (pointer == 0) throw new InvalidOperationException("VAD音声区間を取得できません。");
                try
                {
                    var segment = new SpeechSegment(pointer);
                    var audio = segment.Samples;
                    try { segments.Enqueue(new(preroll.Extend(segment.Start, audio), 16000)); }
                    finally { Array.Clear(audio); }
                }
                finally { Native.DestroySegment(pointer); Native.Pop(handle); }
            }
        }
        bool speech = Native.Detected(handle) == 1;
        return new(speech, segments.TryDequeue(out var ready) ? ready : null);
    }
    public void Dispose()
    {
        if (handle != 0) { Native.Destroy(handle); handle = 0; }
        preroll.Dispose(); Array.Clear(frame); frameCount = 0;
        while (segments.TryDequeue(out var segment)) Array.Clear(segment.Samples);
    }
    private static class Native
    {
        private const string Dll = "sherpa-onnx-c-api";
        [DllImport(Dll, EntryPoint = "SherpaOnnxCreateVoiceActivityDetector", CallingConvention = CallingConvention.Cdecl)] internal static extern nint Create(ref VadModelConfig config, float bufferSeconds);
        [DllImport(Dll, EntryPoint = "SherpaOnnxDestroyVoiceActivityDetector", CallingConvention = CallingConvention.Cdecl)] internal static extern void Destroy(nint handle);
        [DllImport(Dll, EntryPoint = "SherpaOnnxVoiceActivityDetectorAcceptWaveform", CallingConvention = CallingConvention.Cdecl)] internal static extern void Accept(nint handle, float[] samples, int count);
        [DllImport(Dll, EntryPoint = "SherpaOnnxVoiceActivityDetectorDetected", CallingConvention = CallingConvention.Cdecl)] internal static extern int Detected(nint handle);
        [DllImport(Dll, EntryPoint = "SherpaOnnxVoiceActivityDetectorEmpty", CallingConvention = CallingConvention.Cdecl)] internal static extern int Empty(nint handle);
        [DllImport(Dll, EntryPoint = "SherpaOnnxVoiceActivityDetectorFront", CallingConvention = CallingConvention.Cdecl)] internal static extern nint Front(nint handle);
        [DllImport(Dll, EntryPoint = "SherpaOnnxDestroySpeechSegment", CallingConvention = CallingConvention.Cdecl)] internal static extern void DestroySegment(nint segment);
        [DllImport(Dll, EntryPoint = "SherpaOnnxVoiceActivityDetectorPop", CallingConvention = CallingConvention.Cdecl)] internal static extern void Pop(nint handle);
    }
}
