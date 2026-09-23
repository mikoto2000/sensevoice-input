using System.Text.Json;
using SenseVoiceInput.Core;
namespace SenseVoiceInput.Core.Tests;
public class TriggerTests
{
    private static GlobalKeyEvent E(KeyCode k, bool down, long t = 0) => new(k, down ? KeyAction.Down : KeyAction.Up, t);
    [Fact] public void SingleIgnoresOtherKeysAndRepeat()
    {
        var m = new InputTriggerMatcher(InputTrigger.Single(KeyCode.F12));
        Assert.Equal(TriggerTransition.None, m.OnKeyEvent(E(KeyCode.F13, true)));
        Assert.Equal(TriggerTransition.Activated, m.OnKeyEvent(E(KeyCode.F12, true)));
        Assert.Equal(TriggerTransition.None, m.OnKeyEvent(E(KeyCode.F12, true)));
        Assert.Equal(TriggerTransition.Released, m.OnKeyEvent(E(KeyCode.F12, false)));
    }
    [Fact] public void CombinationRequiresBothAndReleasesWhenEitherIsReleased()
    {
        var m = new InputTriggerMatcher(InputTrigger.Combination(KeyCode.LEFT_CTRL, KeyCode.GRAVE));
        Assert.Equal(TriggerTransition.None, m.OnKeyEvent(E(KeyCode.LEFT_CTRL, true)));
        Assert.Equal(TriggerTransition.Activated, m.OnKeyEvent(E(KeyCode.GRAVE, true)));
        Assert.Equal(TriggerTransition.Released, m.OnKeyEvent(E(KeyCode.LEFT_CTRL, false)));
    }
    [Fact] public void CombinationRejectsExtraModifierAndWrongSide()
    {
        var m = new InputTriggerMatcher(InputTrigger.Combination(KeyCode.LEFT_CTRL, KeyCode.GRAVE));
        m.OnKeyEvent(E(KeyCode.RIGHT_CTRL, true));
        Assert.Equal(TriggerTransition.None, m.OnKeyEvent(E(KeyCode.GRAVE, true)));
        m.Reset(); m.OnKeyEvent(E(KeyCode.LEFT_SHIFT, true)); m.OnKeyEvent(E(KeyCode.LEFT_CTRL, true));
        Assert.Equal(TriggerTransition.None, m.OnKeyEvent(E(KeyCode.GRAVE, true)));
    }
    [Theory] [InlineData(349, true)] [InlineData(351, false)]
    public void DoubleTapRequiresTwoCompletedTaps(int elapsed, bool expected)
    {
        var m = new InputTriggerMatcher(InputTrigger.DoubleTap(KeyCode.LEFT_CTRL));
        m.OnKeyEvent(E(KeyCode.LEFT_CTRL, true)); m.OnKeyEvent(E(KeyCode.LEFT_CTRL, false, 10));
        Assert.Equal(TriggerTransition.None, m.OnKeyEvent(E(KeyCode.LEFT_CTRL, true, elapsed)));
        Assert.Equal(expected ? TriggerTransition.Toggled : TriggerTransition.None, m.OnKeyEvent(E(KeyCode.LEFT_CTRL, false, 10 + elapsed)));
    }
    [Fact] public void RepeatAndInterveningKeysInvalidateDoubleTap()
    {
        var m = new InputTriggerMatcher(InputTrigger.DoubleTap(KeyCode.LEFT_CTRL));
        m.OnKeyEvent(E(KeyCode.LEFT_CTRL, true)); m.OnKeyEvent(E(KeyCode.LEFT_CTRL, true, 20));
        Assert.Equal(TriggerTransition.None, m.OnKeyEvent(E(KeyCode.LEFT_CTRL, false, 40)));
        m.OnKeyEvent(E(KeyCode.GRAVE, true, 50)); m.OnKeyEvent(E(KeyCode.GRAVE, false, 60));
        m.OnKeyEvent(E(KeyCode.LEFT_CTRL, true, 80));
        Assert.Equal(TriggerTransition.None, m.OnKeyEvent(E(KeyCode.LEFT_CTRL, false, 90)));
    }
    [Fact] public void SemanticConflictsIgnoreCombinationOrder()
    {
        Assert.True(TriggerValidation.Conflicts(InputTrigger.Single(KeyCode.F12), InputTrigger.Single(KeyCode.F12)));
        Assert.True(TriggerValidation.Conflicts(InputTrigger.Combination(KeyCode.GRAVE, KeyCode.LEFT_CTRL), InputTrigger.Combination(KeyCode.LEFT_CTRL, KeyCode.GRAVE)));
        Assert.False(TriggerValidation.Conflicts(InputTrigger.Combination(KeyCode.LEFT_CTRL, KeyCode.GRAVE), InputTrigger.DoubleTap(KeyCode.LEFT_CTRL)));
    }
    [Fact] public void DoubleTapNotAllowedForPttAndReservedKeysRejected()
    {
        Assert.Throws<ArgumentException>(() => InputTrigger.DoubleTap(KeyCode.LEFT_CTRL).Validate(true));
        Assert.Throws<ArgumentException>(() => InputTrigger.Single(KeyCode.ESCAPE).Validate());
        Assert.Throws<ArgumentException>(() => InputTrigger.Combination(KeyCode.LEFT_ALT, KeyCode.TAB).Validate());
    }
    [Theory] [InlineData(TriggerType.SINGLE_KEY)] [InlineData(TriggerType.KEY_COMBINATION)] [InlineData(TriggerType.DOUBLE_TAP)]
    public void StructuredRoundTrip(TriggerType type)
    {
        var t = type switch { TriggerType.SINGLE_KEY => InputTrigger.Single(KeyCode.F12), TriggerType.KEY_COMBINATION => InputTrigger.Combination(KeyCode.LEFT_CTRL, KeyCode.GRAVE), _ => InputTrigger.DoubleTap(KeyCode.LEFT_CTRL) };
        var json = JsonSerializer.Serialize(t);
        var loaded = JsonSerializer.Deserialize<InputTrigger>(json)!;
        Assert.Equal(t.Type, loaded.Type); Assert.Equal(t.Keys, loaded.Keys); Assert.Equal(t.IntervalMs, loaded.IntervalMs);
        Assert.Contains("LEFT_CTRL", type == TriggerType.SINGLE_KEY ? JsonSerializer.Serialize(InputTrigger.Single(KeyCode.LEFT_CTRL)) : json);
    }
}
