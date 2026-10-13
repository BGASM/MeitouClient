namespace Meitou.Ui;

public enum UiEditKey { Left, Right, Home, End, Backspace, Delete, SelectAll }

/// <summary>Selection and caret editing independent of windowing or rendering APIs.</summary>
public sealed class UiTextBuffer(string text = "")
{
    public string Text { get; private set; } = text;
    public int Caret { get; private set; } = text.Length;
    int anchor = text.Length;
    public int SelectionStart => Math.Min(anchor, Caret);
    public int SelectionLength => Math.Abs(anchor - Caret);
    int Previous(int at) => at <= 0 ? 0 : at > 1 && char.IsLowSurrogate(Text[at - 1]) && char.IsHighSurrogate(Text[at - 2]) ? at - 2 : at - 1;
    int Next(int at) => at >= Text.Length ? Text.Length : at + 1 < Text.Length && char.IsHighSurrogate(Text[at]) && char.IsLowSurrogate(Text[at + 1]) ? at + 2 : at + 1;
    public void Set(string value) { Text = value; Caret = anchor = value.Length; }
    public void Insert(string value)
    {
        int start = SelectionStart, available = 512 - (Text.Length - SelectionLength);
        if (available <= 0) return;
        if (value.Length > available) value = value[..available];
        Text = Text[..start] + value + Text[(start + SelectionLength)..]; Caret = anchor = start + value.Length;
    }
    public void Key(UiEditKey key, bool extend = false)
    {
        if (key == UiEditKey.SelectAll) { anchor = 0; Caret = Text.Length; return; }
        if (key is UiEditKey.Backspace or UiEditKey.Delete)
        {
            int start = SelectionStart, end = start + SelectionLength;
            if (start == end) { if (key == UiEditKey.Backspace) start = Previous(Caret); else end = Next(Caret); }
            Text = Text[..start] + Text[end..]; Caret = anchor = start; return;
        }
        Caret = key switch
        {
            UiEditKey.Home => 0, UiEditKey.End => Text.Length,
            UiEditKey.Left => !extend && SelectionLength > 0 ? SelectionStart : Previous(Caret),
            UiEditKey.Right => !extend && SelectionLength > 0 ? SelectionStart + SelectionLength : Next(Caret), _ => Caret,
        };
        if (!extend) anchor = Caret;
    }
}
