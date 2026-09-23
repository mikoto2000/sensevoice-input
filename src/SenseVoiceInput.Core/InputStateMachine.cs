namespace SenseVoiceInput.Core;

public enum InputState { Idle, Recording, Recognizing, Injecting, Error }

public sealed class InputStateMachine
{
    public InputState State { get; private set; } = InputState.Idle;
    public event Action<InputState>? Changed;
    public void MoveTo(InputState next)
    {
        bool valid = (State, next) switch
        {
            (InputState.Idle, InputState.Recording) => true,
            (InputState.Recording, InputState.Recognizing) => true,
            (InputState.Recognizing, InputState.Injecting) => true,
            (InputState.Recording or InputState.Recognizing or InputState.Injecting, InputState.Error) => true,
            (InputState.Recording or InputState.Recognizing or InputState.Injecting or InputState.Error, InputState.Idle) => true,
            _ => false
        };
        if (!valid) throw new InvalidOperationException($"Invalid transition: {State} -> {next}");
        State = next;
        Changed?.Invoke(next);
    }
}
