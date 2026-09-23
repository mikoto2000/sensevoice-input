namespace SenseVoiceInput.Core;

public sealed class InputTriggerMatcher(InputTrigger trigger)
{
    private readonly HashSet<KeyCode> pressed = [];
    private bool active, tapEligible;
    private long? firstUp, tapDown;
    public void Reset() { pressed.Clear(); active = tapEligible = false; firstUp = tapDown = null; }
    public TriggerTransition OnKeyEvent(GlobalKeyEvent e)
    {
        if (e.Key == KeyCode.UNKNOWN) { firstUp = null; tapEligible = false; return TriggerTransition.None; }
        bool down = e.Action == KeyAction.Down;
        bool changed = down ? pressed.Add(e.Key) : pressed.Remove(e.Key);
        if (!changed) return TriggerTransition.None;
        if (trigger.Type == TriggerType.DOUBLE_TAP)
        {
            if (e.Key != trigger.Keys[0]) { firstUp = null; tapEligible = false; return TriggerTransition.None; }
            if (down) { tapEligible = pressed.Count == 1; tapDown = e.OccurredAtMs; return TriggerTransition.None; }
            if (!tapEligible || tapDown == null || e.OccurredAtMs - tapDown > trigger.IntervalMs) { firstUp = null; return TriggerTransition.None; }
            tapEligible = false;
            if (firstUp is long previous && e.OccurredAtMs - previous is >= 0 && e.OccurredAtMs - previous <= trigger.IntervalMs)
            { firstUp = null; return TriggerTransition.Toggled; }
            firstUp = e.OccurredAtMs;
            return TriggerTransition.None;
        }
        bool matches = trigger.Keys.All(pressed.Contains) && !pressed.Any(k => InputTrigger.IsModifier(k) && !trigger.Keys.Contains(k));
        if (active && !matches) { active = false; return TriggerTransition.Released; }
        // Do not activate on releasing an unrelated modifier.
        if (!active && matches && down && trigger.Keys.Contains(e.Key)) { active = true; return TriggerTransition.Activated; }
        return TriggerTransition.None;
    }
}
