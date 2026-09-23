using SenseVoiceInput.Core;
using SenseVoiceInput.Windows;
namespace SenseVoiceInput.Windows.Tests;
public class KeyboardMapTests
{
    [Theory]
    [InlineData(0x11, 0x1D, false, KeyCode.LEFT_CTRL)]
    [InlineData(0x11, 0x1D, true, KeyCode.RIGHT_CTRL)]
    [InlineData(0x10, 0x2A, false, KeyCode.LEFT_SHIFT)]
    [InlineData(0x10, 0x36, false, KeyCode.RIGHT_SHIFT)]
    [InlineData(0x12, 0x38, false, KeyCode.LEFT_ALT)]
    [InlineData(0x12, 0x38, true, KeyCode.RIGHT_ALT)]
    [InlineData(0x7B, 0, false, KeyCode.F12)]
    [InlineData(0x87, 0, false, KeyCode.F24)]
    [InlineData(0x14, 0x3A, false, KeyCode.CAPS_LOCK)]
    [InlineData(0xF0, 0x3A, false, KeyCode.UNKNOWN)]
    public void MapsNativeKeysAtBoundary(int vk, int scan, bool extended, KeyCode expected) => Assert.Equal(expected, WindowsKeyMap.Translate(vk, scan, extended));
}
