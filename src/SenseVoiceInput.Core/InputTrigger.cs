using System.Text.Json.Serialization;
namespace SenseVoiceInput.Core;

[JsonConverter(typeof(JsonStringEnumConverter<KeyCode>))]
public enum KeyCode
{
    UNKNOWN, LEFT_CTRL, RIGHT_CTRL, LEFT_SHIFT, RIGHT_SHIFT, LEFT_ALT, RIGHT_ALT,
    LEFT_WINDOWS, RIGHT_WINDOWS, ESCAPE, CAPS_LOCK, SPACE, GRAVE, TAB, ENTER, BACKSPACE, DELETE,
    INSERT, HOME, END, PAGE_UP, PAGE_DOWN, LEFT, RIGHT, UP, DOWN,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    MINUS, EQUALS, LEFT_BRACKET, RIGHT_BRACKET, BACKSLASH, SEMICOLON, APOSTROPHE, COMMA, PERIOD, SLASH,
    NUMPAD0, NUMPAD1, NUMPAD2, NUMPAD3, NUMPAD4, NUMPAD5, NUMPAD6, NUMPAD7, NUMPAD8, NUMPAD9, MULTIPLY, ADD, SUBTRACT, DECIMAL, DIVIDE
}
[JsonConverter(typeof(JsonStringEnumConverter<TriggerType>))]
public enum TriggerType { SINGLE_KEY, KEY_COMBINATION, DOUBLE_TAP }
public enum KeyAction { Down, Up }
public readonly record struct GlobalKeyEvent(KeyCode Key, KeyAction Action, long OccurredAtMs);
public enum TriggerTransition { None, Activated, Released, Toggled }

public sealed record InputTrigger
{
    public TriggerType Type { get; init; }
    public KeyCode[] Keys { get; init; } = [];
    public int IntervalMs { get; init; } = 350;
    public static InputTrigger Single(KeyCode key) => new() { Type = TriggerType.SINGLE_KEY, Keys = [key] };
    public static InputTrigger Combination(params KeyCode[] keys) => new() { Type = TriggerType.KEY_COMBINATION, Keys = keys.Order().ToArray() };
    public static InputTrigger DoubleTap(KeyCode key, int intervalMs = 350) => new() { Type = TriggerType.DOUBLE_TAP, Keys = [key], IntervalMs = intervalMs };
    public void Validate(bool pushToTalk = false)
    {
        if (!Enum.IsDefined(Type) || Keys == null || Keys.Length == 0 || Keys.Distinct().Count() != Keys.Length || Keys.Any(k => !Enum.IsDefined(k) || k == KeyCode.UNKNOWN)) throw new ArgumentException("不明なキーまたはトリガー形式です。");
        if (Type == TriggerType.KEY_COMBINATION ? Keys.Length is < 2 or > 4 : Keys.Length != 1) throw new ArgumentException("単一・2回押しは1キー、組み合わせは2～4キーです。");
        if (pushToTalk && Type == TriggerType.DOUBLE_TAP) throw new ArgumentException("Push-to-Talk に2回押しは使用できません。");
        if (IntervalMs is < 100 or > 1000) throw new ArgumentException("2回押しの間隔は100～1000msです。");
        if (Keys.Any(k => k is KeyCode.ESCAPE or KeyCode.LEFT_WINDOWS or KeyCode.RIGHT_WINDOWS) ||
            (Keys.Contains(KeyCode.TAB) && Keys.Any(IsAlt)) ||
            (Keys.Contains(KeyCode.DELETE) && Keys.Any(IsAlt) && Keys.Any(IsCtrl))) throw new ArgumentException("Esc、Windowsキー、Alt+Tab、Ctrl+Alt+Delete は予約されています。");
    }
    public static bool IsModifier(KeyCode k) => k is >= KeyCode.LEFT_CTRL and <= KeyCode.RIGHT_WINDOWS;
    private static bool IsAlt(KeyCode k) => k is KeyCode.LEFT_ALT or KeyCode.RIGHT_ALT;
    private static bool IsCtrl(KeyCode k) => k is KeyCode.LEFT_CTRL or KeyCode.RIGHT_CTRL;
    [JsonIgnore] public string Display => string.Join(" + ", Keys.Select(k => k.ToString().Replace('_', ' '))) + (Type == TriggerType.DOUBLE_TAP ? " ×2" : "");
}
public static class TriggerValidation
{
    public static bool Conflicts(InputTrigger a, InputTrigger b)
    {
        var left = a.Keys.ToHashSet(); var right = b.Keys.ToHashSet();
        // Double-tap of a chord modifier is allowed; a held single key on the same key is not.
        if (a.Type == TriggerType.DOUBLE_TAP || b.Type == TriggerType.DOUBLE_TAP) return left.SetEquals(right);
        return left.IsSubsetOf(right) || right.IsSubsetOf(left);
    }
}
