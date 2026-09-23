using System.Diagnostics;
using SenseVoiceInput.Core;
namespace SenseVoiceInput.Windows;
public interface IWhisperEncoder { float[] Encode(float[] features, CancellationToken ct); }
public interface IWhisperDecoderContext : IWhisperDecoderStep, IDisposable { }
public interface IWhisperDecoderFactory { IWhisperDecoderContext Create(float[] hidden); }
public sealed class WhisperPipeline(IWhisperPreprocessor preprocessor, IWhisperEncoder encoder, IWhisperDecoderFactory decoder, IWhisperTokenizer tokenizer, WhisperGenerationConfig config)
{
    public SpeechRecognitionResult Recognize(AudioData audio, string provider, CancellationToken ct)
    {
        var total = Stopwatch.StartNew(); var stage = Stopwatch.StartNew();
        float[] features;
        try { features = preprocessor.Process(audio, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { throw new SpeechRecognitionException(RecognitionError.AudioPreprocessingFailed, "音声の前処理に失敗しました。", e); }
        var preprocess = stage.Elapsed; stage.Restart(); float[]? hidden = null;
        try
        {
            hidden = encoder.Encode(features, ct); var encode = stage.Elapsed; stage.Restart();
            using var context = decoder.Create(hidden);
            int[] tokens = WhisperTokenGenerator.Generate(context, config, ct); var decode = stage.Elapsed;
            string text;
            try { text = tokenizer.Decode(tokens); }
            catch (Exception e) { throw new SpeechRecognitionException(RecognitionError.TokenizerFailed, "認識結果の文字変換に失敗しました。", e); }
            ct.ThrowIfCancellationRequested();
            return new(text, "ja") { Engine = "whisper-onnx", Model = "whisper-large-v3-turbo-fp16", Provider = provider, AudioDuration = TimeSpan.FromSeconds((double)audio.Samples.Length / audio.SampleRate), Duration = total.Elapsed, PreprocessDuration = preprocess, EncoderDuration = encode, DecoderDuration = decode, GeneratedTokens = tokens.Length };
        }
        catch (OperationCanceledException) { throw; }
        catch (SpeechRecognitionException) { throw; }
        catch (Exception e) { throw new SpeechRecognitionException(RecognitionError.InferenceFailed, "Whisper の推論に失敗しました。", e); }
        finally { Array.Clear(features); if (hidden != null) Array.Clear(hidden); }
    }
}
