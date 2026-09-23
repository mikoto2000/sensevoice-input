namespace SenseVoiceInput.Core;
public sealed class PushToTalkKeyGate
{
    private bool pressed;
    public bool Update(bool down)
    {
        if (pressed == down) return false;
        pressed = down;
        return true;
    }
}
