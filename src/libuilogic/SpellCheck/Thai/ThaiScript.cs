namespace Nikse.SubtitleEdit.UiLogic.SpellCheck.Thai;

public static class ThaiScript
{
    public static bool IsThaiChar(char c) => c is >= '\u0E00' and <= '\u0E7F';

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
