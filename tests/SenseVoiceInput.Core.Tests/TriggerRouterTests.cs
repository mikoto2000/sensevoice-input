using SenseVoiceInput.Core;
namespace SenseVoiceInput.Core.Tests;
public class TriggerRouterTests
{
    [Fact] public void UnsupportedKeyWithoutReleaseDoesNotBlockSettingsForever()
    {
        var router = new InputTriggerRouter(new());
        Assert.False(router.OnKeyEvent(new(KeyCode.UNKNOWN, KeyAction.Down, 0)));
        router.BeginCapture(TriggerType.SINGLE_KEY);
        router.OnKeyEvent(new(KeyCode.UNKNOWN, KeyAction.Down, 1));
        router.Apply(new());
    }
    [Fact] public void EscapeDuringChordDrainsRemainingKeysWithoutActions()
    {
        var router = new InputTriggerRouter(new()); var actions = new List<TriggerAction>(); router.Triggered += actions.Add;
        router.BeginCapture(TriggerType.KEY_COMBINATION);
        router.OnKeyEvent(new(KeyCode.LEFT_CTRL, KeyAction.Down, 0));
        router.OnKeyEvent(new(KeyCode.ESCAPE, KeyAction.Down, 1));
        router.OnKeyEvent(new(KeyCode.ESCAPE, KeyAction.Up, 2));
        router.OnKeyEvent(new(KeyCode.LEFT_CTRL, KeyAction.Up, 3));
        Assert.Empty(actions); router.Apply(new());
    }
    [Fact] public void CaptureConsumesKeysWithoutFiringAndAppliesNewTrigger()
    {
        var router = new InputTriggerRouter(new()); var actions = new List<TriggerAction>(); router.Triggered += actions.Add;
        router.BeginCapture(TriggerType.SINGLE_KEY);
        Assert.True(router.OnKeyEvent(new(KeyCode.F12, KeyAction.Down, 0)));
        Assert.True(router.OnKeyEvent(new(KeyCode.F12, KeyAction.Up, 1))); Assert.Empty(actions);
        router.Apply(new() { PushToTalk = new() { Trigger = InputTrigger.Single(KeyCode.F13) } });
        Assert.False(router.OnKeyEvent(new(KeyCode.F12, KeyAction.Down, 2)));
        router.OnKeyEvent(new(KeyCode.F12, KeyAction.Up, 3));
        Assert.True(router.OnKeyEvent(new(KeyCode.F13, KeyAction.Down, 4)));
        Assert.Equal(new[] { TriggerAction.PushToTalkDown }, actions);
    }
    [Fact] public void ModifierOfChordPassesThroughWithBalancedRelease()
    {
        var router = new InputTriggerRouter(new() { PushToTalk = new() { Trigger = InputTrigger.Combination(KeyCode.LEFT_CTRL, KeyCode.GRAVE) } });
        Assert.False(router.OnKeyEvent(new(KeyCode.LEFT_CTRL, KeyAction.Down, 0)));
        Assert.True(router.OnKeyEvent(new(KeyCode.GRAVE, KeyAction.Down, 1)));
        Assert.False(router.OnKeyEvent(new(KeyCode.LEFT_CTRL, KeyAction.Up, 2)));
        Assert.True(router.OnKeyEvent(new(KeyCode.GRAVE, KeyAction.Up, 3)));
    }
    [Fact] public void DisabledTriggersDoNotConsumeKeys()
    {
        var router = new InputTriggerRouter(new() { PushToTalk = new() { Enabled = false } });
        Assert.False(router.OnKeyEvent(new(KeyCode.F12, KeyAction.Down, 0)));
    }
}
