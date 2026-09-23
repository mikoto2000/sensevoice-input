namespace SenseVoiceInput.Core;
public enum TriggerAction { PushToTalkDown, PushToTalkUp, AutoVoiceToggle }
/// <summary>Pure, synchronous routing. Subscribers queue actions outside the OS hook.</summary>
public sealed class InputTriggerRouter
{
    private AppSettings settings;
    private InputTriggerMatcher ptt, auto;
    private readonly HashSet<KeyCode> pressed = [], suppressed = [];
    private bool draining;
    public TriggerCaptureSession? Capture { get; private set; }
    public bool IsCapturing => Capture != null;
    public event Action<TriggerAction>? Triggered;
    public event Action<TriggerCaptureSession>? CaptureCompleted;
    public InputTriggerRouter(AppSettings settings) { this.settings = settings; ptt = new(settings.PushToTalk.Trigger); auto = new(settings.AutoVoiceInput.ToggleTrigger); }
    public void Apply(AppSettings value)
    {
        value.Validate();
        if (pressed.Count != 0 || Capture != null) throw new InvalidOperationException("キーをすべて離し、キー設定を終えてから保存してください。");
        settings = value; ptt = new(value.PushToTalk.Trigger); auto = new(value.AutoVoiceInput.ToggleTrigger);
    }
    public void BeginCapture(TriggerType type, int intervalMs = 350)
    {
        if (pressed.Count != 0) throw new InvalidOperationException("キーをすべて離してから設定を開始してください。");
        ptt.Reset(); auto.Reset(); Capture = new(type, intervalMs);
    }
    public void CancelCapture() { Capture = null; draining = pressed.Count != 0; ptt.Reset(); auto.Reset(); }
    public bool OnKeyEvent(GlobalKeyEvent e)
    {
        bool down = e.Action == KeyAction.Down;
        bool wasPressed = pressed.Contains(e.Key);
        if (e.Key != KeyCode.UNKNOWN) { if (down) pressed.Add(e.Key); else pressed.Remove(e.Key); }
        bool swallow = suppressed.Contains(e.Key);
        if (draining)
        {
            if (down) suppressed.Add(e.Key); else suppressed.Remove(e.Key);
            draining = pressed.Count != 0; return true;
        }
        if (Capture is { } capture)
        {
            if (down && e.Key != KeyCode.UNKNOWN) suppressed.Add(e.Key); else suppressed.Remove(e.Key);
            capture.OnKeyEvent(e);
            if (capture.Complete) { Capture = null; draining = pressed.Count != 0; ptt.Reset(); auto.Reset(); CaptureCompleted?.Invoke(capture); }
            return true;
        }
        // Drain keys swallowed during capture, including Escape's later key-up.
        if (!down && swallow) suppressed.Remove(e.Key);
        var p = settings.PushToTalk.Enabled ? ptt.OnKeyEvent(e) : TriggerTransition.None;
        var a = settings.AutoVoiceInput.Enabled ? auto.OnKeyEvent(e) : TriggerTransition.None;
        if (p == TriggerTransition.Activated) Triggered?.Invoke(TriggerAction.PushToTalkDown);
        if (p == TriggerTransition.Released) Triggered?.Invoke(TriggerAction.PushToTalkUp);
        if (a is TriggerTransition.Activated or TriggerTransition.Toggled) Triggered?.Invoke(TriggerAction.AutoVoiceToggle);
        // Do not suppress a modifier release whose down was delivered to Windows.
        // Double tap remains transparent so the modifier can still form ordinary shortcuts.
        if (down && !wasPressed && (p == TriggerTransition.Activated || a == TriggerTransition.Activated)) { suppressed.Add(e.Key); swallow = true; }
        return swallow;
    }
}
