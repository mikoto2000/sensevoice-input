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
    public RecognitionBackend[] Backends { get; } = [RecognitionBackend.CUDA, RecognitionBackend.CPU, RecognitionBackend.Auto];
    public RecognitionEngine[] Engines { get; } = Enum.GetValues<RecognitionEngine>();
    public RecognitionEngine Engine { get; set; }
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
    public bool PttEnabled { get; set; }
    public bool AutoEnabled { get; set; }
    public bool OnlyTextInput { get; set; }
    public bool VadEnabled { get; set; }
    public string SilenceTimeout { get; set; }
    public string VadModelPath { get; set; }
    public string DoubleTapInterval { get; set; } = "350";
    public TriggerType[] PttTypes { get; } = [TriggerType.SINGLE_KEY, TriggerType.KEY_COMBINATION];
    public TriggerType[] AutoTypes { get; } = Enum.GetValues<TriggerType>();
    public TriggerType PttType { get; set; }
    public TriggerType AutoType { get; set; }
    private InputTrigger pttTrigger, autoTrigger;
    public string PttTriggerDisplay => pttTrigger.Display;
    public string AutoTriggerDisplay => autoTrigger.Display;
    private string autoStatus = "AUTO OFF", captureHint = "";
    public string AutoStatus { get => autoStatus; set { autoStatus = value; Notify(); } }
    public string CaptureHint { get => captureHint; set { captureHint = value; Notify(); } }
    public ICommand CapturePttCommand { get; }
    public ICommand CaptureAutoCommand { get; }
    public event Action<bool, TriggerType, int>? CaptureRequested;
    public void CompleteCapture(bool ptt, TriggerCaptureSession capture)
    {
        CaptureHint = capture.Cancelled ? "キー設定をキャンセルしました" : capture.Error ?? "候補を更新しました。保存すると適用されます。";
        if (capture.Candidate is not { } candidate) return;
        if (ptt) { pttTrigger = candidate; Notify(nameof(PttTriggerDisplay)); }
        else { autoTrigger = candidate; Notify(nameof(AutoTriggerDisplay)); }
    }
    public SettingsViewModel(AppSettings settings, Action<AppSettings> save, Action<Exception> report)
    {
        Engine = settings.Engine; MicrophoneDeviceId = settings.MicrophoneDeviceId; Backend = settings.Backend;
        TextInputMode = settings.TextInputMode;
        PttEnabled = settings.PushToTalk.Enabled; AutoEnabled = settings.AutoVoiceInput.Enabled;
        pttTrigger = settings.PushToTalk.Trigger; autoTrigger = settings.AutoVoiceInput.ToggleTrigger;
        PttType = pttTrigger.Type; AutoType = autoTrigger.Type;
        OnlyTextInput = settings.AutoVoiceInput.OnlyWhenTextInputFocused; VadEnabled = settings.AutoVoiceInput.Vad.Enabled;
        SilenceTimeout = settings.AutoVoiceInput.Vad.SilenceTimeoutMs.ToString(); DoubleTapInterval = autoTrigger.IntervalMs.ToString();
        VadModelPath = settings.AutoVoiceInput.Vad.ModelPath;
        void Capture(bool ptt)
        {
            if (!CanEdit) return;
            try
            {
                if (!int.TryParse(DoubleTapInterval, out int interval) || interval is < 100 or > 1000) throw new ArgumentException("2回押し間隔は100～1000msです。");
                CaptureRequested?.Invoke(ptt, ptt ? PttType : AutoType, interval);
                CaptureHint = "設定したいキー（組み合わせは同時押し）を押して離してください。2回押しもキー指定は1回です。Escでキャンセル。";
            }
            catch (Exception e) { report(e); }
        }
        CapturePttCommand = new ActionCommand(() => Capture(true)); CaptureAutoCommand = new ActionCommand(() => Capture(false));
        ModelDirectory = settings.ModelDirectory; PasteRestoreDelay = settings.PasteRestoreDelayMs.ToString();
        SaveCommand = new ActionCommand(() =>
        {
            try
            {
                if (!CanEdit) return;
                if (!int.TryParse(PasteRestoreDelay, out int delay)) throw new ArgumentException("復元待機時間は整数で入力してください。");
                if (!int.TryParse(SilenceTimeout, out int silence) || !int.TryParse(DoubleTapInterval, out int interval)) throw new ArgumentException("待機時間は整数で入力してください。");
                if (PttType != pttTrigger.Type || AutoType != autoTrigger.Type) throw new ArgumentException("方式を変更したら「キーを設定」で候補を指定してください。");
                var updated = settings with { Engine = Engine, MicrophoneDeviceId = MicrophoneDeviceId, Backend = Backend, ModelDirectory = ModelDirectory.Trim(), PasteRestoreDelayMs = delay, TextInputMode = TextInputMode,
                    PushToTalk = new() { Enabled = PttEnabled, Trigger = pttTrigger },
                    AutoVoiceInput = new() { Enabled = AutoEnabled, ToggleTrigger = autoTrigger with { IntervalMs = interval }, OnlyWhenTextInputFocused = OnlyTextInput, Vad = new() { Enabled = VadEnabled, SilenceTimeoutMs = silence, ModelPath = VadModelPath.Trim() } } };
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
