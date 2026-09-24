using System.Windows.Automation;
using SenseVoiceInput.Windows;

namespace SenseVoiceInput.Windows.Tests;

public class TextInputFocusTests
{
    [Fact]
    public void TerminalTextSurfaceWithoutReadOnlyAttributeCanArmRecording()
    {
        Assert.True(TextInputFocusProbe.IsEditableText(ControlType.Text, "TermControl", AutomationElement.NotSupported));
        Assert.True(TextInputFocusProbe.IsEditableText(ControlType.Text, "TermControl", false));
        Assert.False(TextInputFocusProbe.IsEditableText(ControlType.Text, "TermControl", true));
        Assert.False(TextInputFocusProbe.IsEditableText(ControlType.Text, "TermControl", TextPattern.MixedAttributeValue));
    }

    [Fact]
    public void DisplayTextAndOtherTerminalControlsCannotArmRecording()
    {
        Assert.False(TextInputFocusProbe.IsEditableText(ControlType.Text, "TextBlock", false));
        Assert.False(TextInputFocusProbe.IsEditableText(ControlType.Text, "TextBlock", AutomationElement.NotSupported));
        Assert.False(TextInputFocusProbe.IsEditableText(ControlType.Button, "TermControl", AutomationElement.NotSupported));
    }

    [Fact]
    public void OrdinaryEditorsStillRequireExplicitWritableAttribute()
    {
        foreach (var type in new[] { ControlType.Edit, ControlType.Document })
        {
            Assert.True(TextInputFocusProbe.IsEditableText(type, "Editor", false));
            Assert.False(TextInputFocusProbe.IsEditableText(type, "Editor", true));
            Assert.False(TextInputFocusProbe.IsEditableText(type, "Editor", AutomationElement.NotSupported));
            Assert.False(TextInputFocusProbe.IsEditableText(type, "Editor", TextPattern.MixedAttributeValue));
        }
    }
}
