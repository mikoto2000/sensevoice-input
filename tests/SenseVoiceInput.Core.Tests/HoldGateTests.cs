using SenseVoiceInput.Core;
namespace SenseVoiceInput.Core.Tests;
public class HoldGateTests
{
    [Fact] public void JisMissingFinalUpStopsWhenRepeatsEnd()
    {
        var gate = new PushToTalkHoldGate(500, 34);
        Assert.Equal(new[] { true }, gate.Update(true, 0, mayMissRelease: true));
        Assert.False(gate.FlushRelease(509));
        for (long time = 510; time <= 990; time += 30)
        {
            Assert.Empty(gate.Update(false, time, true));
            Assert.Empty(gate.Update(true, time + 1, true));
            Assert.False(gate.FlushRelease(time + 20));
        }
        // Observed trace ends with down; no final up is supplied.
        Assert.False(gate.FlushRelease(1108));
        Assert.True(gate.FlushRelease(1109));
        Assert.False(gate.FlushRelease(1200));
    }
    [Fact] public void ShortJisPressWithoutUpHasBoundedInitialGrace()
    {
        var gate = new PushToTalkHoldGate(500, 34);
        gate.Update(true, 0, true);
        Assert.False(gate.FlushRelease(617)); Assert.True(gate.FlushRelease(618));
    }
    [Fact] public void SlowWindowsRepeatSettingsDoNotSplitHeldKey()
    {
        var gate = new PushToTalkHoldGate(1000, 400);
        gate.Update(true, 0, true);
        Assert.False(gate.FlushRelease(1000));
        gate.Update(true, 1100, true);
        Assert.False(gate.FlushRelease(1500));
        gate.Update(true, 1500, true);
        Assert.False(gate.FlushRelease(2349)); Assert.True(gate.FlushRelease(2350));
    }
    [Fact] public void OrdinaryCapsDoesNotExpireFromMissingRepeats()
    {
        var gate = new PushToTalkHoldGate(); gate.Update(true, 0);
        Assert.False(gate.FlushRelease(10000));
        gate.Update(false, 10001); Assert.True(gate.FlushRelease(10051));
    }
    [Fact] public void ObservedJisRepeatPairsRemainOneRecording()
    {
        var gate = new PushToTalkHoldGate(); var transitions = new List<bool>();
        transitions.AddRange(gate.Update(false, 0)); // Observed initial VK_F2 release
        transitions.AddRange(gate.Update(true, 1));
        for (long time = 510; time < 3500; time += 31)
        {
            transitions.AddRange(gate.Update(false, time));
            transitions.AddRange(gate.Update(true, time + 1));
            Assert.False(gate.FlushRelease(time + 20));
        }
        Assert.Equal(new[] { true }, transitions);
        Assert.Empty(gate.Update(false, 3600));
        Assert.False(gate.FlushRelease(3649));
        Assert.True(gate.FlushRelease(3650));
        Assert.False(gate.FlushRelease(3700));
    }
    [Fact] public void OrdinaryDownRepeatAndShortTapProduceOnePair()
    {
        var gate = new PushToTalkHoldGate();
        Assert.Equal(new[] { true }, gate.Update(true, 0));
        Assert.Empty(gate.Update(true, 10));
        Assert.Empty(gate.Update(false, 20));
        Assert.True(gate.FlushRelease(70));
    }
    [Fact] public void NewPressAfterDeadlineFlushesOldReleaseEvenIfTimerWasDelayed()
    {
        var gate = new PushToTalkHoldGate();
        gate.Update(true, 0); gate.Update(false, 100);
        Assert.Equal(new[] { false, true }, gate.Update(true, 200));
    }
    [Fact] public void UnmatchedOrRepeatedUpsDoNotExtendDeadline()
    {
        var gate = new PushToTalkHoldGate();
        Assert.Empty(gate.Update(false, 0)); Assert.False(gate.FlushRelease(100));
        gate.Update(true, 200); gate.Update(false, 300); gate.Update(false, 325);
        Assert.True(gate.FlushRelease(350));
    }
}
