namespace Nikse.SubtitleEdit.UiLogic.SpellCheck.Thai;

public interface IThaiTokenizer
{
    /// <summary>Segment <paramref name="text"/> into tokens with indexes relative to <paramref name="text"/>.</summary>
    IReadOnlyList<ThaiTokenSpan> Segment(string text);

    bool IsReady { get; }
}
