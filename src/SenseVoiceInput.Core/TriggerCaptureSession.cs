namespace SenseVoiceInput.Core;
public sealed class TriggerCaptureSession(TriggerType type, int intervalMs = 350)
{
    private readonly HashSet<KeyCode> pressed = [], captured = [];
    public InputTrigger? Candidate { get; private set; }
    public bool Cancelled { get; private set; }
    public string? Error { get; private set; }
    public bool Complete => Cancelled || Candidate != null || Error != null;
    public void OnKeyEvent(GlobalKeyEvent e)
    {
        if (Complete) return;
        if (e.Key == KeyCode.ESCAPE) { Cancelled = true; return; }
        if (e.Key == KeyCode.UNKNOWN) { Error = "このキーは取得できません。JIS英数など解放通知が不確実なキーには対応していません。"; return; }
        if (e.Action == KeyAction.Down) { pressed.Add(e.Key); captured.Add(e.Key); return; }
        pressed.Remove(e.Key);
        if (pressed.Count != 0 || captured.Count == 0) return;
        var candidate = new InputTrigger { Type = type, Keys = captured.Order().ToArray(), IntervalMs = intervalMs };
        try { candidate.Validate(); Candidate = candidate; }
        catch (ArgumentException error) { Error = error.Message; }
    }
}
