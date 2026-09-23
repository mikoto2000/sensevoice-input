using System.IO;
using System.Text.Json;
namespace SenseVoiceInput.Windows;
public sealed record WhisperGenerationConfig(long[] InitialTokens, int EosToken, int TimestampBegin, int[] SuppressTokens, int[] BeginSuppressTokens, int MaxNewTokens)
{
    public static WhisperGenerationConfig Load(string directory)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "generation_config.json")));
        var r = doc.RootElement;
        int Int(string n) => r.GetProperty(n).GetInt32();
        int[] Array(string n) => r.GetProperty(n).EnumerateArray().Select(x => x.GetInt32()).ToArray();
        long[] prefix = [Int("decoder_start_token_id"), r.GetProperty("lang_to_id").GetProperty("<|ja|>").GetInt32(), r.GetProperty("task_to_id").GetProperty("transcribe").GetInt32(), Int("no_timestamps_token_id")];
        return new(prefix, Int("eos_token_id"), Int("no_timestamps_token_id") + 1, Array("suppress_tokens"), Array("begin_suppress_tokens"), Int("max_length") - prefix.Length);
    }
}
public interface IWhisperDecoderStep { float[] Run(long[] input, CancellationToken ct); }
public static class WhisperTokenGenerator
{
    public static int[] Generate(IWhisperDecoderStep decoder, WhisperGenerationConfig config, CancellationToken ct)
    {
        if (config.MaxNewTokens is < 1 or > 448) throw new ArgumentOutOfRangeException(nameof(config));
        List<int> result = []; long[] input = config.InitialTokens;
        for (int i = 0; i < config.MaxNewTokens; i++)
        {
            ct.ThrowIfCancellationRequested();
            float[] logits = decoder.Run(input, ct);
            ct.ThrowIfCancellationRequested();
            foreach (int token in config.SuppressTokens) if (token < logits.Length) logits[token] = float.NegativeInfinity;
            if (i == 0) foreach (int token in config.BeginSuppressTokens) if (token < logits.Length) logits[token] = float.NegativeInfinity;
            for (int token = config.TimestampBegin; token < logits.Length; token++) logits[token] = float.NegativeInfinity;
            int next = -1; float best = float.NegativeInfinity;
            for (int token = 0; token < logits.Length; token++) if (logits[token] > best) { best = logits[token]; next = token; }
            if (next < 0) throw new InvalidOperationException("InferenceFailed: 有効なトークンがありません。");
            if (next == config.EosToken) break;
            result.Add(next); input = [next];
        }
        return result.ToArray();
    }
}
