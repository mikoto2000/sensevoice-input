using System.Text;
using System.Text.Json;
namespace SenseVoiceInput.Windows;
public interface IWhisperTokenizer { string Decode(IEnumerable<int> ids); }
/// <summary>Decode-only ByteLevel BPE. No merge/encoding algorithm is needed for model output IDs.</summary>
public sealed class WhisperTokenizer : IWhisperTokenizer
{
    private readonly Dictionary<int, byte[]> vocabulary = [];
    private readonly HashSet<int> special = [];
    public WhisperTokenizer(string json)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        var bytes = Enumerable.Range(33,94).Concat(Enumerable.Range(161,12)).Concat(Enumerable.Range(174,82)).ToList();
        var chars = bytes.ToList(); int n = 0;
        for (int b = 0; b < 256; b++) if (!bytes.Contains(b)) { bytes.Add(b); chars.Add(256 + n++); }
        var inverse = chars.Select((c,i) => (c, b: (byte)bytes[i])).ToDictionary(x => (char)x.c, x => x.b);
        foreach (var token in root.GetProperty("model").GetProperty("vocab").EnumerateObject())
        {
            if (token.Name.StartsWith("<|", StringComparison.Ordinal) && token.Name.EndsWith("|>", StringComparison.Ordinal)) { special.Add(token.Value.GetInt32()); continue; }
            vocabulary[token.Value.GetInt32()] = token.Name.Select(c => inverse[c]).ToArray();
        }
        foreach (var token in root.GetProperty("added_tokens").EnumerateArray()) if (token.GetProperty("special").GetBoolean()) special.Add(token.GetProperty("id").GetInt32());
    }
    public string Decode(IEnumerable<int> ids)
    {
        List<byte> bytes = [];
        foreach (int id in ids)
        {
            if (special.Contains(id)) continue;
            if (!vocabulary.TryGetValue(id, out var token)) throw new InvalidOperationException($"TokenizerFailed: 未知のトークン ID {id}");
            bytes.AddRange(token);
        }
        return Encoding.UTF8.GetString(bytes.ToArray()).Trim();
    }
}
