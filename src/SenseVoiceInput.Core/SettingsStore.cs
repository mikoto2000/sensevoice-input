using System.Text.Json;
using System.Text.Json.Serialization;
namespace SenseVoiceInput.Core;
public sealed record AppSettings
{
    public string? MicrophoneDeviceId { get; init; }
    public string PushToTalkKey { get; init; } = "CapsLock";
    public RecognitionBackend Backend { get; init; } = RecognitionBackend.Auto;
    public string ModelDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "models", "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17");
    public int PasteRestoreDelayMs { get; init; } = 1500;
    public TextInputMode TextInputMode { get; init; } = TextInputMode.Unicode;
    public void Validate()
    {
        if (PushToTalkKey != "CapsLock") throw new NotSupportedException("MVP supports CapsLock only.");
        _ = RecognitionOptions.Provider(Backend);
        if (!Enum.IsDefined(TextInputMode)) throw new ArgumentOutOfRangeException(nameof(TextInputMode));
        if (string.IsNullOrWhiteSpace(ModelDirectory)) throw new ArgumentException("Model directory is required.");
        if (PasteRestoreDelayMs is < 500 or > 10000) throw new ArgumentOutOfRangeException(nameof(PasteRestoreDelayMs));
    }
}
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public AppSettings Load()
    {
        var settings = File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? throw new JsonException("Settings are null.") : new();
        settings.Validate(); return settings;
    }
    public void Save(AppSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
        File.Move(temporary, path, true);
    }
}
