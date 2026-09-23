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
    private readonly Drawing.Icon trayIcon;
    private readonly bool openSettings;
    private readonly InputTriggerRouter triggers;
    private readonly AutoVoiceInputController autoVoice;
    private readonly WasapiVadService vad;
    private readonly System.Windows.Threading.DispatcherTimer focusTimer;
    private bool capturePtt, probing;
    private CancellationTokenSource? downloadCancellation;
    private Task? downloadTask;
    private bool modelsReady;
    private string focusIdentity = "";
    private nint autoTarget;
    private string autoFieldIdentity = "";
    private string VadModelPath => ModelProvisioner.ResolveVadPath(settings);
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
        viewModel.RetryDownloadRequested += BeginModelPreparation;
        viewModel.CancelDownloadRequested += () => downloadCancellation?.Cancel();
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
        using (var stream = typeof(ApplicationSession).Assembly.GetManifestResourceStream("SenseVoiceInput.App.Assets.tray.ico")
            ?? throw new InvalidOperationException("トレイアイコンが見つかりません。"))
        using (var icon = new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize))
            trayIcon = (Drawing.Icon)icon.Clone();
        tray = new() { Text = "SenseVoice Input · Ready", Icon = trayIcon, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => OpenSettings();
        if (initialError != null) Report(initialError);
        if (store.Warnings.Count != 0) viewModel.Error = string.Join("\n", store.Warnings);
    }
    public void Start()
    {
        log.Write(DiagnosticEvent.ApplicationStarted);
        try { viewModel.Microphones = AudioCaptureService.GetDevices(); } catch (Exception e) { Report(e); }
        keyboard.Start(); tray.Visible = true; focusTimer.Start(); UpdateStatus();
        BeginModelPreparation();
        if (openSettings || store.Warnings.Count != 0) OpenSettings();
        else tray.ShowBalloonTip(3000, "SenseVoice Input", "設定したトリガーで音声入力。設定はトレイをダブルクリック。", Forms.ToolTipIcon.Info);
    }
    private async void OnTrigger(TriggerAction action)
    {
        if (exiting || !modelsReady || triggers.IsCapturing) return;
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
        if (downloadCancellation != null) throw new InvalidOperationException("モデルのダウンロード完了後に保存してください。");
        if (autoVoice.IsProcessing || coordinator.State != InputState.Idle) throw new InvalidOperationException("音声処理の完了後に保存してください。");
        if (triggers.IsCapturing) throw new InvalidOperationException("キー設定を完了またはキャンセルしてください。");
        triggers.Apply(value);
        try { store.Save(value); } catch { triggers.Apply(settings); throw; }
        settings = value; autoVoice.TurnOff();
        autoVoice.OnlyWhenTextInputFocused = value.AutoVoiceInput.OnlyWhenTextInputFocused;
        autoVoice.VadEnabled = value.AutoVoiceInput.Vad.Enabled;
        autoVoice.SilenceTimeoutMs = value.AutoVoiceInput.Vad.SilenceTimeoutMs;
        UpdateStatus();
        BeginModelPreparation();
    }
    private void BeginModelPreparation()
    {
        if (exiting || disposed || downloadCancellation != null || coordinator.State != InputState.Idle || autoVoice.IsProcessing || triggers.IsCapturing) return;
        autoVoice.TurnOff();
        downloadTask = PrepareModelsAsync();
    }
    private async Task PrepareModelsAsync()
    {
        modelsReady = false;
        using var cancellation = new CancellationTokenSource();
        downloadCancellation = cancellation;
        viewModel.Downloading = true;
        viewModel.DownloadIndeterminate = true;
        viewModel.DownloadPercent = 0;
        viewModel.DownloadStatus = "モデルを確認しています…";
        UpdateStatus();
        try
        {
            if (!ModelProvisioner.IsReady(settings)) OpenSettings();
            var progress = new Progress<ModelDownloadProgress>(p =>
            {
                if (disposed || cancellation.IsCancellationRequested || !ReferenceEquals(downloadCancellation, cancellation)) return;
                viewModel.DownloadIndeterminate = p.TotalBytes is not > 0;
                viewModel.DownloadPercent = p.TotalBytes is > 0 ? 100.0 * p.BytesReceived / p.TotalBytes.Value : 0;
                string total = p.TotalBytes is > 0 ? $" / {p.TotalBytes.Value / 1048576.0:F1} MB" : "";
                viewModel.DownloadStatus = $"{p.FileName}  {p.BytesReceived / 1048576.0:F1} MB{total}";
            });
            var prepared = await new ModelProvisioner().EnsureAsync(settings, progress, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (disposed || exiting) return;
            // Do not overwrite a settings file recovered from an error until the user explicitly saves.
            if (prepared != settings && store.Warnings.Count == 0) store.Save(prepared);
            settings = prepared;
            viewModel.ModelDirectory = settings.ModelDirectory;
            viewModel.VadModelPath = settings.AutoVoiceInput.Vad.ModelPath;
            modelsReady = true;
            viewModel.DownloadStatus = "モデルの準備ができました。";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!disposed) viewModel.DownloadStatus = "ダウンロードを中止しました。再試行できます。";
        }
        catch (Exception error)
        {
            if (!disposed && !exiting)
            {
                viewModel.DownloadStatus = $"モデルを準備できませんでした: {error.Message} 接続・空き容量を確認し、再試行してください。";
                log.Error(error);
                OpenSettings();
            }
        }
        finally
        {
            downloadCancellation = null;
            if (!disposed) { viewModel.Downloading = false; viewModel.DownloadIndeterminate = false; UpdateStatus(); }
        }
    }
    private void OnAutoState(AutoVoiceState state)
    {
        if (state == AutoVoiceState.Listening) { autoTarget = new ForegroundWindowService().GetForegroundWindow(); autoFieldIdentity = focusIdentity; log.Write(DiagnosticEvent.AutoSpeechStarted); }
        if (state == AutoVoiceState.Processing) log.Write(DiagnosticEvent.AutoProcessingStarted);
        UpdateStatus();
    }
    private void UpdateStatus()
    {
        viewModel.CanEdit = !exiting && downloadCancellation == null && coordinator.State == InputState.Idle && !autoVoice.IsProcessing && autoVoice.State != AutoVoiceState.Listening;
        viewModel.AutoStatus = $"AUTO {(autoVoice.IsOn ? autoVoice.State.ToString().ToUpperInvariant() : "OFF")} · {(vad.IsAvailable ? "Silero VAD" : "VADモデル未配置")}";
        if (coordinator.State == InputState.Idle) viewModel.Status = settings.PushToTalk.Enabled ? "PTT READY · " + settings.PushToTalk.Trigger.Display : "PTT DISABLED";
        tray.Text = $"SenseVoice · PTT {coordinator.State} · AUTO {(autoVoice.IsOn ? autoVoice.State.ToString() : "OFF")}";
        if (!modelsReady)
        {
            viewModel.Status = downloadCancellation != null ? "モデルを準備中…" : "モデルの準備が必要です";
            tray.Text = "SenseVoice Input · モデルの準備が必要です";
        }
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
        downloadCancellation?.Cancel();
        if (downloadTask != null) await downloadTask;
        try { focusTimer.Stop(); keyboard.Dispose(); await autoVoice.StopAsync(); await coordinator.DisposeAsync(); }
        catch (Exception e) { Report(e); }
        finally { log.Write(DiagnosticEvent.ApplicationStopped); Dispose(); app.Shutdown(); }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        downloadCancellation?.Cancel();
        focusTimer.Stop(); autoVoice.Dispose(); vad.Dispose(); keyboard.Dispose(); audio.Dispose(); (recognition as IDisposable)?.Dispose(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); trayIcon.Dispose();
    }
}
