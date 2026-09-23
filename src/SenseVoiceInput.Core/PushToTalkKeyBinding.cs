namespace SenseVoiceInput.Core;
/// <summary>JIS Eisu/Caps and US Caps share physical scan 0x3A, but not always VK_CAPITAL.</summary>
public sealed class PushToTalkKeyBinding(int virtualKey = 0x14)
{
    public bool Matches(int eventVirtualKey, int scanCode, bool extended) => virtualKey == 0x14
        ? !extended && (scanCode == 0x3A || scanCode == 0 && eventVirtualKey == 0x14)
        : eventVirtualKey == virtualKey;
}
