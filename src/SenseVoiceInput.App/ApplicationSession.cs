using System.IO;
using System.Windows;
using SenseVoiceInput.Core;
using SenseVoiceInput.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using TriggerAction = SenseVoiceInput.Core.TriggerAction;

namespace SenseVoiceInput.App;
/// <summary>Composition root: constructor injection, presentation and orderly shutdown.</summary>
public sealed class ApplicationSession : IDisposable
{
    private readonly Application app;
    private readonly SettingsStore store;
    private readonly DiagnosticLog log;
    private AppSettings settings;
    private readonly SettingsViewModel viewModel;
    private readonly MainWindow window;
    private readonly AudioCaptureService audio;
    private readonly ISpeechRecognitionService recognition;
    private readonly PushToTalkCoordinator coordinator;
    private readonly GlobalKeyboardService keyboard;
    private readonly Forms.NotifyIcon tray;
    private readonly bool openSettings;
    private readonly InputTriggerRouter triggers;
    private readonly AutoVoiceInputController autoVoice;
    private readonly WasapiVadService vad;
    private readonly System.Windows.Threading.DispatcherTimer focusTimer;
    private bool capturePtt, probing;
    private string focusIdentity = "";
    private nint autoTarget;
    private string autoFieldIdentity = "";
    private string VadModelPath => string.IsNullOrWhiteSpace(settings.AutoVoiceInput.Vad.ModelPath)
        ? Path.GetFullPath(Path.Combine(settings.ModelDirectory, "..", "silero_vad.onnx")) : Path.GetFullPath(settings.AutoVoiceInput.Vad.ModelPath);
    private bool exiting, disposed;
    private InputState previous = InputState.Idle;
    public ApplicationSession(Application app, string[] args)
    {
        this.app = app;
        string? Option(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        string directory = Option("--settings-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SenseVoiceInput");
        openSettings = args.Contains("--settings");
        store = new(Path.Combine(directory, "settings.json")); log = new(Path.Combine(directory, "diagnostic.log"));
        Exception? initialError = null;
        try { settings = store.Load(); }
        catch (Exception e) { settings = new() { PushToTalk = new() { Enabled = false } }; initialError = e; }
        if (Option("--model-dir") is { } model) settings = settings with { ModelDirectory = Path.GetFullPath(model) };
        if (Option("--engine") is { } engine) settings = settings with { Engine = Enum.Parse<RecognitionEngine>(engine, true) };
        if (Option("--backend") is { } backend) settings = settings with { Backend = Enum.Parse<RecognitionBackend>(backend, true) };
        settings.Validate();
        triggers = new(settings);
        viewModel = new(settings, SaveSettings, Report);
        window = new() { DataContext = viewModel };
        audio = new(() => settings.MicrophoneDeviceId);
        recognition = new ConfigurableRecognitionService(() => settings, log.RecognitionInfo);
        var injector = new TextInjectionService(new ClipboardDesktop(() => settings.PasteRestoreDelayMs), () => settings.TextInputMode);
        vad = new(() => settings.MicrophoneDeviceId, () => VadModelPath, action => app.Dispatcher.BeginInvoke(action));
        autoVoice = new(vad, async (data, ct) =>
        {
            var target = autoTarget; var identity = autoFieldIdentity;
            async Task CheckTarget()
            {
                ct.ThrowIfCancellationRequested();
                var focused = await Task.Run(TextInputFocusProbe.Read, ct);
                ct.ThrowIfCancellationRequested();
                if (new ForegroundWindowService().GetForegroundWindow() != target ||
                    settings.AutoVoiceInput.OnlyWhenTextInputFocused && (!focused.Editable || focused.Identity != identity))
                    throw new InvalidOperationException("入力欄が変わったため自動入力を中止しました。");
            }
            await CheckTarget();
            var result = await recognition.RecognizeAsync(data, ct); ct.ThrowIfCancellationRequested();
            await CheckTarget();
            await injector.InjectAsync(result.Text, target, ct);
        }) { OnlyWhenTextInputFocused = settings.AutoVoiceInput.OnlyWhenTextInputFocused, VadEnabled = settings.AutoVoiceInput.Vad.Enabled, SilenceTimeoutMs = settings.AutoVoiceInput.Vad.SilenceTimeoutMs };
        vad.SpeechStarted += () => autoVoice.SpeechDetected();
        vad.SegmentReady += async data => await autoVoice.SilenceDetectedAsync(data);
        vad.Failed += error => autoVoice.ReportFailure(error);
        autoVoice.StateChanged += OnAutoState; autoVoice.Failed += Report;
        coordinator = new(audio, recognition, injector, new ForegroundWindowService());
        coordinator.StateChanged += OnState; coordinator.Failed += Report;
        keyboard = new(triggers.OnKeyEvent);
        triggers.Triggered += action => app.Dispatcher.BeginInvoke(() => OnTrigger(action));
        triggers.CaptureCompleted += capture => app.Dispatcher.BeginInvoke(() => { viewModel.CompleteCapture(capturePtt, capture); autoVoice.SetSuspended(false); });
        viewModel.CaptureRequested += (ptt, type, interval) => { triggers.BeginCapture(type, interval); capturePtt = ptt; autoVoice.SetSuspended(true); };
        window.IsVisibleChanged += (_, _) => { if (!window.IsVisible) { triggers.CancelCapture(); autoVoice.SetSuspended(coordinator.State != InputState.Idle); } };
        focusTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
        focusTimer.Tick += ProbeFocus;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Settings", null, (_, _) => OpenSettings());
        menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());
        tray = new() { Text = "SenseVoice Input · Ready", Icon = Drawing.SystemIcons.Application, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => OpenSettings();
        if (initialError != null) Report(initialError);
        if (store.Warnings.Count != 0) viewModel.Error = string.Join("\n", store.Warnings);
    }
    public void Start()
    {
        log.Write(DiagnosticEvent.ApplicationStarted);
        try { viewModel.Microphones = AudioCaptureService.GetDevices(); } catch (Exception e) { Report(e); }
        keyboard.Start(); tray.Visible = true; focusTimer.Start(); UpdateStatus();
        if (settings.Engine == RecognitionEngine.WhisperOnnx)
        {
            try { WhisperModelSessionFactory.CheckFiles(settings.ModelDirectory); } catch (Exception e) { Report(e); }
        }
        else if (!File.Exists(Path.Combine(settings.ModelDirectory, "model.int8.onnx")))
            Report(new FileNotFoundException("モデルがありません。設定画面で model.int8.onnx と tokens.txt のフォルダーを指定してください。"));
        if (openSettings || store.Warnings.Count != 0) OpenSettings();
        else tray.ShowBalloonTip(3000, "SenseVoice Input", "設定したトリガーで音声入力。設定はトレイをダブルクリック。", Forms.ToolTipIcon.Info);
    }
    private async void OnTrigger(TriggerAction action)
    {
        if (exiting || triggers.IsCapturing) return;
        try
        {
            if (action == TriggerAction.AutoVoiceToggle)
            {
                if (settings.AutoVoiceInput.Enabled)
                {
                    if (!autoVoice.IsOn && !vad.IsAvailable) throw new FileNotFoundException("Silero VADモデルがありません。設定のモデルパスを確認してください。");
                    if (!autoVoice.IsOn) autoVoice.SetTextFocus(false);
                    autoVoice.Toggle();
                }
                return;
            }
            if (!settings.PushToTalk.Enabled) return;
            if (action == TriggerAction.PushToTalkDown) { autoVoice.SetSuspended(true); await coordinator.KeyDownAsync(); }
            else await coordinator.KeyUpAsync();
            if (coordinator.State == InputState.Idle) autoVoice.SetSuspended(false);
        }
        catch (Exception e) { Report(e); }
    }
    private void SaveSettings(AppSettings value)
    {
        if (autoVoice.IsProcessing || coordinator.State != InputState.Idle) throw new InvalidOperationException("音声処理の完了後に保存してください。");
        if (triggers.IsCapturing) throw new InvalidOperationException("キー設定を完了またはキャンセルしてください。");
        triggers.Apply(value);
        try { store.Save(value); } catch { triggers.Apply(settings); throw; }
        settings = value; autoVoice.TurnOff();
        autoVoice.OnlyWhenTextInputFocused = value.AutoVoiceInput.OnlyWhenTextInputFocused;
        autoVoice.VadEnabled = value.AutoVoiceInput.Vad.Enabled;
        autoVoice.SilenceTimeoutMs = value.AutoVoiceInput.Vad.SilenceTimeoutMs;
        UpdateStatus();
    }
    private void OnAutoState(AutoVoiceState state)
    {
        if (state == AutoVoiceState.Listening) { autoTarget = new ForegroundWindowService().GetForegroundWindow(); autoFieldIdentity = focusIdentity; log.Write(DiagnosticEvent.AutoSpeechStarted); }
        if (state == AutoVoiceState.Processing) log.Write(DiagnosticEvent.AutoProcessingStarted);
        UpdateStatus();
    }
    private void UpdateStatus()
    {
        viewModel.CanEdit = !exiting && coordinator.State == InputState.Idle && !autoVoice.IsProcessing && autoVoice.State != AutoVoiceState.Listening;
        viewModel.AutoStatus = $"AUTO {(autoVoice.IsOn ? autoVoice.State.ToString().ToUpperInvariant() : "OFF")} · {(vad.IsAvailable ? "Silero VAD" : "VADモデル未配置")}";
        if (coordinator.State == InputState.Idle) viewModel.Status = settings.PushToTalk.Enabled ? "PTT READY · " + settings.PushToTalk.Trigger.Display : "PTT DISABLED";
        tray.Text = $"SenseVoice · PTT {coordinator.State} · AUTO {(autoVoice.IsOn ? autoVoice.State.ToString() : "OFF")}";
    }
    private async void ProbeFocus(object? sender, EventArgs e)
    {
        if (probing || exiting || !autoVoice.IsOn) return;
        probing = true;
        try
        {
            var focus = await Task.Run(TextInputFocusProbe.Read);
            if (exiting) return;
            if (focus.Identity != focusIdentity) autoVoice.SetTextFocus(false);
            focusIdentity = focus.Identity; autoVoice.SetTextFocus(focus.Editable);
        }
        catch (Exception error) { autoVoice.SetTextFocus(false); Report(error); }
        finally { probing = false; }
    }
    private void OnState(InputState state)
    {
        viewModel.CanEdit = state == InputState.Idle && !exiting;
        viewModel.Status = state switch { InputState.Recording => "PTT LISTENING — トリガーを離して確定", InputState.Recognizing => "PTT PROCESSING…", InputState.Injecting => "入力中…", InputState.Error => "エラー", _ => "PTT READY" };
        tray.Text = $"SenseVoice Input · {state}";
        tray.Icon = state switch { InputState.Recording => Drawing.SystemIcons.Shield, InputState.Recognizing or InputState.Injecting => Drawing.SystemIcons.Information, InputState.Error => Drawing.SystemIcons.Error, _ => Drawing.SystemIcons.Application };
        if (state == InputState.Recording) { viewModel.Error = ""; log.Write(DiagnosticEvent.AudioCaptureStarted); }
        if (state == InputState.Recognizing) { log.Write(DiagnosticEvent.AudioCaptureStopped); log.Write(DiagnosticEvent.RecognitionStarted); }
        if (state == InputState.Injecting) { log.Write(DiagnosticEvent.RecognitionCompleted); log.Write(DiagnosticEvent.TextInjectionStarted); }
        if (state == InputState.Idle && previous == InputState.Injecting) log.Write(DiagnosticEvent.TextInjectionCompleted);
        if (state == InputState.Idle && previous == InputState.Recognizing) log.Write(DiagnosticEvent.RecognitionCompleted);
        previous = state;
        UpdateStatus();
        if (log.LastWriteError != null) viewModel.Error = "診断ログを保存できません。設定フォルダーのアクセス権を確認してください。";
    }
    private void Report(Exception error)
    {
        log.Error(error);
        viewModel.Error = $"{error.Message}\n詳細: diagnostic.log（発話内容は記録しません）";
        tray?.ShowBalloonTip(5000, "SenseVoice Input", error.Message, Forms.ToolTipIcon.Error);
    }
    private void OpenSettings() { window.Show(); window.WindowState = WindowState.Normal; window.Activate(); }
    private async Task ExitAsync()
    {
        if (exiting) return;
        exiting = true; viewModel.CanEdit = false;
        try { focusTimer.Stop(); keyboard.Dispose(); await autoVoice.StopAsync(); await coordinator.DisposeAsync(); }
        catch (Exception e) { Report(e); }
        finally { log.Write(DiagnosticEvent.ApplicationStopped); Dispose(); app.Shutdown(); }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        focusTimer.Stop(); autoVoice.Dispose(); vad.Dispose(); keyboard.Dispose(); audio.Dispose(); (recognition as IDisposable)?.Dispose(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose();
    }
}
