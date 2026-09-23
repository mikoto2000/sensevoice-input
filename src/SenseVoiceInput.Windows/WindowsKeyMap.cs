using SenseVoiceInput.Core;
namespace SenseVoiceInput.Windows;
public static class WindowsKeyMap
{
    public static KeyCode Translate(int vk, int scan, bool extended)
    {
        if (vk is >= 0x70 and <= 0x87) return KeyCode.F1 + (vk - 0x70);
        if (vk is >= 0x41 and <= 0x5A) return KeyCode.A + (vk - 0x41);
        if (vk is >= 0x30 and <= 0x39) return KeyCode.D0 + (vk - 0x30);
        if (vk is >= 0x60 and <= 0x69) return KeyCode.NUMPAD0 + (vk - 0x60);
        return vk switch
        {
            0x11 => extended ? KeyCode.RIGHT_CTRL : KeyCode.LEFT_CTRL,
            0x10 => scan == 0x36 ? KeyCode.RIGHT_SHIFT : KeyCode.LEFT_SHIFT,
            0x12 => extended ? KeyCode.RIGHT_ALT : KeyCode.LEFT_ALT,
            0xA0 => KeyCode.LEFT_SHIFT, 0xA1 => KeyCode.RIGHT_SHIFT,
            0xA2 => KeyCode.LEFT_CTRL, 0xA3 => KeyCode.RIGHT_CTRL,
            0xA4 => KeyCode.LEFT_ALT, 0xA5 => KeyCode.RIGHT_ALT,
            0x5B => KeyCode.LEFT_WINDOWS, 0x5C => KeyCode.RIGHT_WINDOWS,
            0x14 => KeyCode.CAPS_LOCK, 0x1B => KeyCode.ESCAPE, 0x20 => KeyCode.SPACE,
            0xC0 => KeyCode.GRAVE, 0x09 => KeyCode.TAB, 0x0D => KeyCode.ENTER, 0x08 => KeyCode.BACKSPACE,
            0x2E => KeyCode.DELETE, 0x2D => KeyCode.INSERT, 0x24 => KeyCode.HOME, 0x23 => KeyCode.END,
            0x21 => KeyCode.PAGE_UP, 0x22 => KeyCode.PAGE_DOWN, 0x25 => KeyCode.LEFT, 0x27 => KeyCode.RIGHT, 0x26 => KeyCode.UP, 0x28 => KeyCode.DOWN,
            0xBD => KeyCode.MINUS, 0xBB => KeyCode.EQUALS, 0xDB => KeyCode.LEFT_BRACKET, 0xDD => KeyCode.RIGHT_BRACKET,
            0xDC => KeyCode.BACKSLASH, 0xBA => KeyCode.SEMICOLON, 0xDE => KeyCode.APOSTROPHE, 0xBC => KeyCode.COMMA, 0xBE => KeyCode.PERIOD, 0xBF => KeyCode.SLASH,
            0x6A => KeyCode.MULTIPLY, 0x6B => KeyCode.ADD, 0x6D => KeyCode.SUBTRACT, 0x6E => KeyCode.DECIMAL, 0x6F => KeyCode.DIVIDE,
            _ => KeyCode.UNKNOWN
        };
    }
}
