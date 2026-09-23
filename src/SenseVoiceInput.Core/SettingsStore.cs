using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
namespace SenseVoiceInput.Core;
public sealed record PushToTalkSettings
{
    public bool Enabled { get; init; } = true;
    public InputTrigger Trigger { get; init; } = InputTrigger.Single(KeyCode.F12);
    public void Validate() { if (Trigger == null) throw new ArgumentException("PTT トリガーがありません。"); Trigger.Validate(true); }
}
public sealed record VadSettings
{
    public bool Enabled { get; init; } = true;
    public int SilenceTimeoutMs { get; init; } = 800;
}
public sealed record AutoVoiceInputSettings
{
    public bool Enabled { get; init; }
    public InputTrigger ToggleTrigger { get; init; } = InputTrigger.DoubleTap(KeyCode.LEFT_CTRL);
    public bool OnlyWhenTextInputFocused { get; init; } = true;
    public VadSettings Vad { get; init; } = new();
    public void Validate()
    {
        if (ToggleTrigger == null || Vad == null) throw new ArgumentException("自動入力の設定が不正です。");
        ToggleTrigger.Validate();
        if (Vad.SilenceTimeoutMs is < 200 or > 5000) throw new ArgumentException("無音判定は200～5000msです。");
    }
}
public sealed record AppSettings
{
    public string? MicrophoneDeviceId { get; init; }
    public PushToTalkSettings PushToTalk { get; init; } = new();
    public AutoVoiceInputSettings AutoVoiceInput { get; init; } = new();
    public RecognitionBackend Backend { get; init; } = RecognitionBackend.Auto;
    public string ModelDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "models", "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17");
    public int PasteRestoreDelayMs { get; init; } = 1500;
    public TextInputMode TextInputMode { get; init; } = TextInputMode.Unicode;
    public void Validate()
    {
        PushToTalk.Validate(); AutoVoiceInput.Validate();
        if (PushToTalk.Enabled && AutoVoiceInput.Enabled && TriggerValidation.Conflicts(PushToTalk.Trigger, AutoVoiceInput.ToggleTrigger)) throw new ArgumentException("PTT と自動入力のトリガーが競合しています。");
        _ = RecognitionOptions.Provider(Backend);
        if (!Enum.IsDefined(TextInputMode)) throw new ArgumentOutOfRangeException(nameof(TextInputMode));
        if (string.IsNullOrWhiteSpace(ModelDirectory)) throw new ArgumentException("Model directory is required.");
        if (PasteRestoreDelayMs is < 500 or > 10000) throw new ArgumentOutOfRangeException(nameof(PasteRestoreDelayMs));
    }
}
public sealed class SettingsStore(string path)
{
    public List<string> Warnings { get; } = [];
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public AppSettings Load()
    {
        Warnings.Clear();
        if (!File.Exists(path)) return new();
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new JsonException();
            T ReadTrigger<T>(string name, T fallback, Action<T> validate) where T : class
            {
                if (!root.ContainsKey(name)) return fallback;
                try { var value = root[name]?.Deserialize<T>(Options) ?? throw new JsonException(); validate(value); return value; }
                catch (Exception e) when (e is JsonException or ArgumentException)
                { Warnings.Add($"{name} の設定が不正なため、この機能を無効化しました。"); return fallback; }
            }
            var ptt = root.ContainsKey("pushToTalk") ? ReadTrigger("pushToTalk", new PushToTalkSettings { Enabled = false }, s => s.Validate()) : new PushToTalkSettings();
            var auto = ReadTrigger("autoVoiceInput", new AutoVoiceInputSettings { Enabled = false }, s => s.Validate());
            if (root.ContainsKey("pushToTalkKey") && !root.ContainsKey("pushToTalk"))
            { ptt = ptt with { Enabled = false }; Warnings.Add("旧キー設定を無効化しました。Push-to-Talk のキーを設定して有効にしてください。"); }
            root.Remove("pushToTalk"); root.Remove("autoVoiceInput"); root.Remove("pushToTalkKey");
            var settings = root.Deserialize<AppSettings>(Options)! with { PushToTalk = ptt, AutoVoiceInput = auto };
            if (ptt.Enabled && auto.Enabled && TriggerValidation.Conflicts(ptt.Trigger, auto.ToggleTrigger))
            { settings = settings with { AutoVoiceInput = auto with { Enabled = false } }; Warnings.Add("トリガー競合のため自動音声入力だけを無効化しました。"); }
            settings.Validate(); return settings;
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            Warnings.Add("設定を読み込めません。トリガーを無効化しました。元のファイルは変更していません。");
            return new() { PushToTalk = new() { Enabled = false }, AutoVoiceInput = new() { Enabled = false } };
        }
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
