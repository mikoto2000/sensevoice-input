using System.Windows.Automation;
namespace SenseVoiceInput.Windows;
public sealed record TextInputFocus(bool Editable, string Identity);
public static class TextInputFocusProbe
{
    // Call from a worker thread. Never inspect the field's text/value.
    public static TextInputFocus Read()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null) return new(false, "");
            var current = element.Current;
            if (current.ProcessId == Environment.ProcessId || current.IsPassword || !current.IsEnabled || !current.IsKeyboardFocusable) return new(false, "");
            bool editable;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value))
                editable = !((ValuePattern)value).Current.IsReadOnly;
            else
                editable = element.TryGetCurrentPattern(TextPattern.Pattern, out var text) &&
                    IsEditableText(current.ControlType, current.ClassName,
                        ((TextPattern)text).DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute));
            return new(editable, editable ? string.Join(".", element.GetRuntimeId()) : "");
        }
        catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        { return new(false, ""); }
    }

    internal static bool IsEditableText(ControlType type, string className, object readOnly)
    {
        // Windows Terminal exposes its keyboard input surface as Text/TermControl,
        // and its text provider does not implement IsReadOnly. Do not extend this
        // exception to arbitrary text controls or explicitly read-only providers.
        // https://github.com/microsoft/terminal/blob/main/src/cascadia/TerminalControl/TermControlAutomationPeer.cpp
        if (type == ControlType.Text && className == "TermControl")
            return readOnly is false || ReferenceEquals(readOnly, AutomationElement.NotSupported);
        return (type == ControlType.Edit || type == ControlType.Document) && readOnly is false;
    }
}
