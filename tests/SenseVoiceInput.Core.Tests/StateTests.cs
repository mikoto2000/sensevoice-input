using SenseVoiceInput.Core;
namespace SenseVoiceInput.Core.Tests;
public class StateTests
{
    [Fact] public void StartsIdleAndAcceptsRecording()
    {
        var state = new InputStateMachine();
        Assert.Equal(InputState.Idle, state.State);
        state.MoveTo(InputState.Recording);
        Assert.Equal(InputState.Recording, state.State);
    }
    [Fact] public void RejectsIdleToInjecting()
    {
        var state = new InputStateMachine();
        Assert.Throws<InvalidOperationException>(() => state.MoveTo(InputState.Injecting));
    }
}
