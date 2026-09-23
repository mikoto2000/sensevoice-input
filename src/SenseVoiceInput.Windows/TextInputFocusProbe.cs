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
            bool editable = element.TryGetCurrentPattern(ValuePattern.Pattern, out var value)
                ? !((ValuePattern)value).Current.IsReadOnly
                : (current.ControlType == ControlType.Edit || current.ControlType == ControlType.Document) && element.TryGetCurrentPattern(TextPattern.Pattern, out var text) &&
                  ((TextPattern)text).DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute) is false;
            return new(editable, editable ? string.Join(".", element.GetRuntimeId()) : "");
        }
        catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        { return new(false, ""); }
    }
}
