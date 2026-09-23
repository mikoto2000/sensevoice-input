using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SenseVoiceInput.Core;
using SenseVoiceInput.Windows;
namespace SenseVoiceInput.App;
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private IReadOnlyList<MicrophoneDevice> microphones = [new(null, "Default microphone")];
    public IReadOnlyList<MicrophoneDevice> Microphones { get => microphones; set { microphones = value; Notify(); } }
    public RecognitionBackend[] Backends { get; } = [RecognitionBackend.Auto, RecognitionBackend.CPU];
    public string? MicrophoneDeviceId { get; set; }
    public RecognitionBackend Backend { get; set; }
    public sealed record InputModeOption(TextInputMode Value, string Name);
    public InputModeOption[] InputModes { get; } = [new(TextInputMode.Unicode, "直接入力（標準）"), new(TextInputMode.Clipboard, "クリップボードで貼り付け")];
    private TextInputMode textInputMode;
    public TextInputMode TextInputMode { get => textInputMode; set { textInputMode = value; Notify(); Notify(nameof(UsesClipboard)); } }
    public bool UsesClipboard => TextInputMode == TextInputMode.Clipboard;
    public string ModelDirectory { get; set; }
    public string PasteRestoreDelay { get; set; }
    private string status = "Ready", error = "";
    private bool canEdit = true;
    public string Status { get => status; set { status = value; Notify(); } }
    public string Error { get => error; set { error = value; Notify(); } }
    public bool CanEdit { get => canEdit; set { canEdit = value; Notify(); } }
    public ICommand SaveCommand { get; }
    public SettingsViewModel(AppSettings settings, Action<AppSettings> save, Action<Exception> report)
    {
        MicrophoneDeviceId = settings.MicrophoneDeviceId; Backend = settings.Backend;
        TextInputMode = settings.TextInputMode;
        ModelDirectory = settings.ModelDirectory; PasteRestoreDelay = settings.PasteRestoreDelayMs.ToString();
        SaveCommand = new ActionCommand(() =>
        {
            try
            {
                if (!CanEdit) return;
                if (!int.TryParse(PasteRestoreDelay, out int delay)) throw new ArgumentException("復元待機時間は整数で入力してください。");
                var updated = settings with { MicrophoneDeviceId = MicrophoneDeviceId, Backend = Backend, ModelDirectory = ModelDirectory.Trim(), PasteRestoreDelayMs = delay, TextInputMode = TextInputMode };
                updated.Validate(); save(updated); Error = ""; Status = "設定を保存しました";
            }
            catch (Exception e) { report(e); }
        });
    }
    private void Notify([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
    private sealed class ActionCommand(Action action) : ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
