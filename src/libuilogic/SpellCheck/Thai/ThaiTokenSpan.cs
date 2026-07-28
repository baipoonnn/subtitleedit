namespace Nikse.SubtitleEdit.UiLogic.SpellCheck.Thai;

/// <summary>A token span into the original input string (for underline / replace).</summary>
public readonly struct ThaiTokenSpan
{
    public ThaiTokenSpan(int index, string text)
    {
        Index = index;
        Text = text ?? string.Empty;
    }

    public int Index { get; }
    public string Text { get; }
    public int Length => Text.Length;
}
