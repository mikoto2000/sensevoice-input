using SenseVoiceInput.Core;
namespace SenseVoiceInput.Core.Tests;
public class KeyboardBindingTests
{
    [Theory]
    [InlineData(0x14, 0x3A, false, true)] // US Caps Lock
    [InlineData(0xF0, 0x3A, false, true)] // JIS Eisu / Caps, without Shift
    [InlineData(0xF0, 0x70, false, false)] // Different OEM key
    [InlineData(0x14, 0x3A, true, false)] // Different extended physical key
    [InlineData(0x14, 0, false, true)] // Explicit virtual Caps event from accessibility tools
    [InlineData(0x41, 0x1E, false, false)]
    public void CapsBindingUsesPhysicalKeyAcrossLayouts(int vk, int scan, bool extended, bool expected)
    {
        Assert.Equal(expected, new PushToTalkKeyBinding().Matches(vk, scan, extended));
    }
    [Fact] public void JisDownAndDifferentVirtualKeyOnUpFormOnePress()
    {
        var binding = new PushToTalkKeyBinding(); var gate = new PushToTalkKeyGate();
        Assert.True(binding.Matches(0xF0, 0x3A, false) && gate.Update(true));
        Assert.False(gate.Update(true));
        Assert.True(binding.Matches(0x14, 0x3A, false) && gate.Update(false));
    }
    [Fact] public void OtherConfiguredKeysStillUseTheirVirtualKey()
    {
        var binding = new PushToTalkKeyBinding(0x7C);
        Assert.True(binding.Matches(0x7C, 0x64, false));
        Assert.False(binding.Matches(0xF0, 0x3A, false));
    }
}
