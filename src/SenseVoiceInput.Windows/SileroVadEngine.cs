using System.Runtime.InteropServices;
using SherpaOnnx;
using SenseVoiceInput.Core;
namespace SenseVoiceInput.Windows;
public readonly record struct VadFrameResult(bool Speech, AudioData? Segment);
/// <summary>Single-thread-owned, checked native handles. Bounded to 30 seconds per segment.</summary>
public sealed class SileroVadEngine : IDisposable
{
    private nint handle;
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
        Native.Accept(handle, samples, samples.Length);
        bool speech = Native.Detected(handle) == 1;
        if (Native.Empty(handle) == 1) return new(speech, null);
        var pointer = Native.Front(handle);
        if (pointer == 0) throw new InvalidOperationException("VAD音声区間を取得できません。");
        try { return new(speech, new(new SpeechSegment(pointer).Samples, 16000)); }
        finally { Native.DestroySegment(pointer); Native.Pop(handle); }
    }
    public void Dispose() { if (handle != 0) { Native.Destroy(handle); handle = 0; } }
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
