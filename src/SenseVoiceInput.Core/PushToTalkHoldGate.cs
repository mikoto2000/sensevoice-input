namespace SenseVoiceInput.Core;

/// <summary>
/// Some JIS layouts emit a synthetic up immediately before each repeated down.
/// Wait briefly for its paired down before treating an up as a physical release.
/// For OEM IME mode keys that omit the final up, expire a lease renewed by repeats.
/// Call Update and FlushRelease from the same message-loop thread, using a monotonic clock.
/// </summary>
public sealed class PushToTalkHoldGate(int initialRepeatDelayMs = 500, int repeatIntervalMs = 34)
{
    public const int ReleaseDelayMs = 50;
    private bool pressed;
    private long? releaseAt;
    private long? missingUpAt;
    private readonly int repeatGraceMs = Math.Max(100, 2 * repeatIntervalMs + 50);
    public bool HasPendingRelease => releaseAt.HasValue || missingUpAt.HasValue;

    public bool[] Update(bool down, long nowMs, bool mayMissRelease = false)
    {
        bool released = FlushRelease(nowMs);
        if (down)
        {
            releaseAt = null;
            if (!pressed)
            {
                pressed = true;
                if (mayMissRelease) missingUpAt = nowMs + initialRepeatDelayMs + repeatGraceMs;
                return released ? [false, true] : [true];
            }
            if (missingUpAt.HasValue || mayMissRelease) missingUpAt = nowMs + repeatGraceMs;
        }
        else if (pressed) releaseAt ??= nowMs + ReleaseDelayMs;
        return released ? [false] : [];
    }
    public bool FlushRelease(long nowMs)
    {
        bool explicitRelease = releaseAt is { } deadline && nowMs >= deadline;
        bool missingRelease = missingUpAt is { } lease && nowMs >= lease;
        if (!explicitRelease && !missingRelease) return false;
        releaseAt = null; missingUpAt = null; pressed = false; return true;
    }
}
