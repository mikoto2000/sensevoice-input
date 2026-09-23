using System.IO;
using System.Windows;
using SenseVoiceInput.Core;
using SenseVoiceInput.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

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
    private readonly SenseVoiceRecognitionService recognition;
    private readonly PushToTalkCoordinator coordinator;
    private readonly GlobalKeyboardService keyboard;
    private readonly Forms.NotifyIcon tray;
    private readonly bool openSettings;
    private readonly bool keyboardDiagnostics;
    private int diagnosticEvents;
    private bool exiting, disposed;
    private InputState previous = InputState.Idle;
    public ApplicationSession(Application app, string[] args)
    {
        this.app = app;
        string? Option(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        string directory = Option("--settings-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SenseVoiceInput");
        openSettings = args.Contains("--settings");
        keyboardDiagnostics = args.Contains("--keyboard-diagnostics");
        store = new(Path.Combine(directory, "settings.json")); log = new(Path.Combine(directory, "diagnostic.log"));
        Exception? initialError = null;
        try { settings = store.Load(); }
        catch (Exception e) { settings = new(); initialError = e; }
        if (Option("--model-dir") is { } model) settings = settings with { ModelDirectory = Path.GetFullPath(model) };
        viewModel = new(settings, value => { store.Save(value); settings = value; }, Report);
        window = new() { DataContext = viewModel };
        audio = new(() => settings.MicrophoneDeviceId);
        recognition = new(() => settings.ModelDirectory, () => settings.Backend);
        coordinator = new(audio, recognition, new TextInjectionService(new ClipboardDesktop(() => settings.PasteRestoreDelayMs), () => settings.TextInputMode), new ForegroundWindowService());
        coordinator.StateChanged += OnState; coordinator.Failed += Report;
        keyboard = new(app.Dispatcher, diagnostic: keyboardDiagnostics ? (message, vk, scan, flags) =>
        {
            if (diagnosticEvents++ < 2000) log.KeyboardDiagnostic(message, vk, scan, flags);
        } : null);
        keyboard.KeyChanged += OnKey;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Settings", null, (_, _) => OpenSettings());
        menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());
        tray = new() { Text = "SenseVoice Input · Ready", Icon = Drawing.SystemIcons.Application, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => OpenSettings();
        if (initialError != null) Report(initialError);
    }
    public void Start()
    {
        log.Write(DiagnosticEvent.ApplicationStarted);
        try { viewModel.Microphones = AudioCaptureService.GetDevices(); } catch (Exception e) { Report(e); }
        keyboard.Start(); tray.Visible = true;
        if (keyboardDiagnostics) { viewModel.Status = "キー診断中（録音・入力はしません）"; tray.Text = "SenseVoice Input · Key diagnostics"; }
        if (!File.Exists(Path.Combine(settings.ModelDirectory, "model.int8.onnx")))
            Report(new FileNotFoundException("モデルがありません。設定画面で model.int8.onnx と tokens.txt のフォルダーを指定してください。"));
        if (openSettings) OpenSettings();
        else tray.ShowBalloonTip(3000, "SenseVoice Input", keyboardDiagnostics ? "キー診断中です。録音・文字入力は行いません。" : "Caps Lock を押して話します。設定はトレイをダブルクリック。", Forms.ToolTipIcon.Info);
    }
    private async void OnKey(bool down)
    {
        if (exiting || keyboardDiagnostics) return;
        try { if (down) await coordinator.KeyDownAsync(); else await coordinator.KeyUpAsync(); }
        catch (Exception e) { Report(e); }
    }
    private void OnState(InputState state)
    {
        viewModel.CanEdit = state == InputState.Idle && !exiting;
        viewModel.Status = state switch { InputState.Recording => "録音中 — Caps Lock を離して確定", InputState.Recognizing => "認識中…", InputState.Injecting => "入力中…", InputState.Error => "エラー", _ => "Ready" };
        tray.Text = $"SenseVoice Input · {state}";
        tray.Icon = state switch { InputState.Recording => Drawing.SystemIcons.Shield, InputState.Recognizing or InputState.Injecting => Drawing.SystemIcons.Information, InputState.Error => Drawing.SystemIcons.Error, _ => Drawing.SystemIcons.Application };
        if (state == InputState.Recording) { viewModel.Error = ""; log.Write(DiagnosticEvent.AudioCaptureStarted); }
        if (state == InputState.Recognizing) { log.Write(DiagnosticEvent.AudioCaptureStopped); log.Write(DiagnosticEvent.RecognitionStarted); }
        if (state == InputState.Injecting) { log.Write(DiagnosticEvent.RecognitionCompleted); log.Write(DiagnosticEvent.TextInjectionStarted); }
        if (state == InputState.Idle && previous == InputState.Injecting) log.Write(DiagnosticEvent.TextInjectionCompleted);
        if (state == InputState.Idle && previous == InputState.Recognizing) log.Write(DiagnosticEvent.RecognitionCompleted);
        previous = state;
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
        try { keyboard.Dispose(); await coordinator.DisposeAsync(); }
        catch (Exception e) { Report(e); }
        finally { log.Write(DiagnosticEvent.ApplicationStopped); Dispose(); app.Shutdown(); }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        keyboard.Dispose(); audio.Dispose(); recognition.Dispose(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose();
    }
}
