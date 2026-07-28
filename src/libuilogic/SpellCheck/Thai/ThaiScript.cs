namespace Nikse.SubtitleEdit.UiLogic.SpellCheck.Thai;

public static class ThaiScript
{
    public static bool IsThaiChar(char c) => c is >= '\u0E00' and <= '\u0E7F';

    /// <summary>
    /// Thai upper/lower vowels and tone marks are Unicode NonspacingMarks, so
    /// <see cref="char.IsLetterOrDigit"/> is false for them. OCR/spell splitters must still
    /// keep them attached to the preceding base consonant or the word is shredded into
    /// single letters (e.g. "ทับ" → "ท" + "บ").
    /// </summary>
    public static bool IsThaiCombiningMark(char c) =>
        IsThaiChar(c) && char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark;

    public static bool IsWordChar(char c) =>
        (char.IsLetterOrDigit(c) && c != '"')
        || c == '\''
        || c == '-'
        || IsThaiCombiningMark(c);

    public static bool ContainsThai(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var c in text)
        {
            if (IsThaiChar(c))
            {
                return true;
            }
        }

        return false;
    }
}
