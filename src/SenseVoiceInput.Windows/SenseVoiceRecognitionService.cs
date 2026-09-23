using System.Runtime.InteropServices;
using SenseVoiceInput.Core;
using SherpaOnnx;

namespace SenseVoiceInput.Windows;

/// <summary>CPU ONNX Runtime through sherpa-onnx. No subprocess or network requests.</summary>
public sealed class SenseVoiceRecognitionService(Func<string> modelDirectory, Func<RecognitionBackend> backend) : ISpeechRecognitionService, IDisposable
{
    private readonly object sync = new();
    private nint recognizer;
    private string? loadedDirectory;
    private bool disposed;
    public Task<SpeechRecognitionResult> RecognizeAsync(AudioData audio, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (audio.SampleRate <= 0) throw new ArgumentException("Invalid sample rate.");
            if (audio.Samples.Length < audio.SampleRate / 10) return new SpeechRecognitionResult("", "ja");
            var directory = Path.GetFullPath(modelDirectory());
            var provider = RecognitionOptions.Provider(backend());
            if (recognizer == 0 || loadedDirectory != directory)
            {
                Release();
                var model = Path.Combine(directory, "model.int8.onnx");
                var tokens = Path.Combine(directory, "tokens.txt");
                if (!File.Exists(model) || !File.Exists(tokens)) throw new FileNotFoundException("SenseVoice model.int8.onnx / tokens.txt is missing.");
                // The upstream configuration uses LPStr. Fail explicitly instead of corrupting a non-ASCII path.
                if (directory.Any(c => c > 127)) throw new NotSupportedException("Use an ASCII-only model directory for this native build.");
                var config = new OfflineRecognizerConfig();
                config.FeatConfig.SampleRate = 16000;
                config.FeatConfig.FeatureDim = 80;
                config.ModelConfig.SenseVoice.Model = model;
                config.ModelConfig.SenseVoice.Language = "ja";
                config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
                config.ModelConfig.Tokens = tokens;
                config.ModelConfig.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
                config.ModelConfig.Provider = provider;
                recognizer = Native.Create(ref config);
                if (recognizer == 0) throw new InvalidOperationException("SenseVoice / ONNX Runtime initialization failed.");
                loadedDirectory = directory;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var stream = Native.CreateStream(recognizer);
            if (stream == 0) throw new InvalidOperationException("Could not allocate recognition stream.");
            try
            {
                Native.Accept(stream, audio.SampleRate, audio.Samples, audio.Samples.Length);
                Native.Decode(recognizer, stream);
                // Native Decode is not interruptible. Never inject after cancellation; wait before freeing handles.
                cancellationToken.ThrowIfCancellationRequested();
                var json = Native.Result(stream);
                if (json == 0) throw new InvalidOperationException("Recognition returned no result.");
                try { return RecognitionOptions.ParseResult(Marshal.PtrToStringUTF8(json)!); }
                finally { Native.DestroyJson(json); }
            }
            finally { Native.DestroyStream(stream); }
        }
    }, cancellationToken);
    private void Release() { if (recognizer != 0) { Native.Destroy(recognizer); recognizer = 0; } }
    public void Dispose() { lock (sync) { disposed = true; Release(); } }

    private static class Native
    {
        private const string Dll = "sherpa-onnx-c-api";
        [DllImport(Dll, EntryPoint = "SherpaOnnxCreateOfflineRecognizer", CallingConvention = CallingConvention.Cdecl)] internal static extern nint Create(ref OfflineRecognizerConfig config);
        [DllImport(Dll, EntryPoint = "SherpaOnnxDestroyOfflineRecognizer", CallingConvention = CallingConvention.Cdecl)] internal static extern void Destroy(nint handle);
        [DllImport(Dll, EntryPoint = "SherpaOnnxCreateOfflineStream", CallingConvention = CallingConvention.Cdecl)] internal static extern nint CreateStream(nint handle);
        [DllImport(Dll, EntryPoint = "SherpaOnnxDestroyOfflineStream", CallingConvention = CallingConvention.Cdecl)] internal static extern void DestroyStream(nint stream);
        [DllImport(Dll, EntryPoint = "SherpaOnnxAcceptWaveformOffline", CallingConvention = CallingConvention.Cdecl)] internal static extern void Accept(nint stream, int rate, [In] float[] samples, int count);
        [DllImport(Dll, EntryPoint = "SherpaOnnxDecodeOfflineStream", CallingConvention = CallingConvention.Cdecl)] internal static extern void Decode(nint handle, nint stream);
        [DllImport(Dll, EntryPoint = "SherpaOnnxGetOfflineStreamResultAsJson", CallingConvention = CallingConvention.Cdecl)] internal static extern nint Result(nint stream);
        [DllImport(Dll, EntryPoint = "SherpaOnnxDestroyOfflineStreamResultJson", CallingConvention = CallingConvention.Cdecl)] internal static extern void DestroyJson(nint json);
    }
}
