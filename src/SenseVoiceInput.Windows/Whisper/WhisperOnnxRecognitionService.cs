using System.Diagnostics;
using System.IO;
using Microsoft.ML.OnnxRuntime;
using SenseVoiceInput.Core;
namespace SenseVoiceInput.Windows;
public sealed class WhisperOnnxRecognitionService(string directory, RecognitionBackend backend, Action<string>? diagnostic = null) : ISpeechRecognitionService, IDisposable
{
    private readonly SemaphoreSlim gate = new(1,1);
    private InferenceSession? encoderSession, decoderSession;
    private WhisperPipeline? pipeline;
    private bool disposed;
    public async Task<SpeechRecognitionResult> RecognizeAsync(AudioData audio,CancellationToken cancellationToken=default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            return await Task.Run(()=>Recognize(audio,cancellationToken),cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    private SpeechRecognitionResult Recognize(AudioData audio,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(audio.SampleRate < 8000) throw new SpeechRecognitionException(RecognitionError.AudioPreprocessingFailed,"サンプルレートが不正です。");
        if(audio.Samples.Length < audio.SampleRate / 10) return new("","ja") { Engine="whisper-onnx",Model="whisper-large-v3-turbo-fp16",Provider=backend.ToString() };
        EnsureLoaded(ct);
        // Microphone permits 60 s; process all audio in Whisper's 30 s windows, never silently truncate.
        List<SpeechRecognitionResult> chunks=[];
        for(int offset=0;offset<audio.Samples.Length;offset+=audio.SampleRate*30)
        {
            ct.ThrowIfCancellationRequested();
            var samples=audio.Samples.AsSpan(offset,Math.Min(audio.SampleRate*30,audio.Samples.Length-offset)).ToArray();
            try { chunks.Add(pipeline!.Recognize(new(samples,audio.SampleRate),backend.ToString(),ct)); }
            finally { Array.Clear(samples); }
        }
        var result=chunks[0] with { Text=string.Concat(chunks.Select(x=>x.Text)), AudioDuration=TimeSpan.FromSeconds((double)audio.Samples.Length/audio.SampleRate), Duration=TimeSpan.FromTicks(chunks.Sum(x=>x.Duration.Ticks)), PreprocessDuration=TimeSpan.FromTicks(chunks.Sum(x=>x.PreprocessDuration.Ticks)), EncoderDuration=TimeSpan.FromTicks(chunks.Sum(x=>x.EncoderDuration.Ticks)), DecoderDuration=TimeSpan.FromTicks(chunks.Sum(x=>x.DecoderDuration.Ticks)), GeneratedTokens=chunks.Sum(x=>x.GeneratedTokens) };
        diagnostic?.Invoke(FormattableString.Invariant($"Whisper provider={backend} model=whisper-large-v3-turbo-fp16 audio_s={result.AudioDuration.TotalSeconds:F3} preprocess_s={result.PreprocessDuration.TotalSeconds:F3} encoder_s={result.EncoderDuration.TotalSeconds:F3} decoder_s={result.DecoderDuration.TotalSeconds:F3} total_s={result.Duration.TotalSeconds:F3} tokens={result.GeneratedTokens} RTF={result.RealTimeFactor:F3}"));
        return result;
    }
    private void EnsureLoaded(CancellationToken ct)
    {
        if(pipeline!=null) return;
        WhisperModelSessionFactory.CheckFiles(directory);
        var watch=Stopwatch.StartNew(); diagnostic?.Invoke($"Whisper model load start provider={backend} model=whisper-large-v3-turbo-fp16");
        try
        {
            WhisperModelSessionFactory.ValidateConfig(directory);
            var preprocess=new WhisperAudioPreprocessor(WhisperPreprocessorConfig.Load(directory));
            var generation=WhisperGenerationConfig.Load(directory);
            WhisperTokenizer tokenizer;
            try { tokenizer=new(File.ReadAllText(Path.Combine(directory,"tokenizer.json"))); }
            catch(Exception e) { throw new SpeechRecognitionException(RecognitionError.TokenizerFailed,"tokenizer.json を読めません。",e); }
            ct.ThrowIfCancellationRequested();
            encoderSession=WhisperModelSessionFactory.Create(Path.Combine(directory,"encoder_model_fp16.onnx"),backend);
            ct.ThrowIfCancellationRequested();
            decoderSession=WhisperModelSessionFactory.Create(Path.Combine(directory,"decoder_model_merged_fp16.onnx"),backend);
            ct.ThrowIfCancellationRequested();
            pipeline=new(preprocess,new WhisperEncoder(encoderSession),new WhisperDecoder(decoderSession),tokenizer,generation);
            diagnostic?.Invoke(FormattableString.Invariant($"Whisper model load end provider={backend} elapsed_s={watch.Elapsed.TotalSeconds:F3}"));
        }
        catch(Exception e)
        {
            decoderSession?.Dispose(); encoderSession?.Dispose(); decoderSession=null; encoderSession=null; pipeline=null;
            if(e is OperationCanceledException or SpeechRecognitionException) throw;
            throw new SpeechRecognitionException(RecognitionError.ModelLoadFailed,"Whisper のモデル設定を確認してください。",e);
        }
    }
    public void Dispose()
    {
        gate.Wait();
        try { if(disposed) return; disposed=true; decoderSession?.Dispose(); encoderSession?.Dispose(); pipeline=null; }
        finally { gate.Release(); }
    }
}
