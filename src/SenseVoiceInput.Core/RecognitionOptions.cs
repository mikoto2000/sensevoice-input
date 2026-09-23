using System.Text.Json;
using System.Text.RegularExpressions;
namespace SenseVoiceInput.Core;
public enum RecognitionBackend { Auto, CPU, CUDA, DirectML }
public static partial class RecognitionOptions
{
    public static string Provider(RecognitionBackend backend) => backend switch
    {
        RecognitionBackend.Auto or RecognitionBackend.CPU => "cpu",
        _ => throw new NotSupportedException("This MVP supports CPU and Auto only.")
    };
    public static SpeechRecognitionResult ParseResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string text = root.GetProperty("text").GetString() ?? "";
        string lang = root.TryGetProperty("lang", out var value) ? value.GetString() ?? "ja" : "ja";
        return new(Tags().Replace(text, "").Trim(), Tags().Replace(lang, "").Trim() is { Length: > 0 } clean ? clean : "ja");
    }
    [GeneratedRegex(@"<\|[^|]*\|>")]
    private static partial Regex Tags();
}
