namespace Nikse.SubtitleEdit.UiLogic.SpellCheck.Thai;

/// <summary>
/// Applies Thai segmentation to an already punctuation-split token list when the active
/// dictionary language is Thai and a segmenter is configured.
/// </summary>
public static class ThaiSegmentation
{
    public static bool IsThaiLanguageActive()
    {
        var lang = SpellCheckConfig.ActiveTwoLetterLanguage() ?? string.Empty;
        return string.Equals(lang, "th", StringComparison.OrdinalIgnoreCase);
    }

    public static List<SpellCheckWord> ApplyToSplitWords(List<SpellCheckWord> words)
    {
        if (words.Count == 0 || !IsThaiLanguageActive())
        {
            return words;
        }

        var kind = SpellCheckConfig.ThaiSegmenter() ?? ThaiSegmenterKinds.None;
        if (string.Equals(kind, ThaiSegmenterKinds.None, StringComparison.OrdinalIgnoreCase))
        {
            return words;
        }

        var tokenizer = ThaiTokenizerService.GetActiveTokenizer();
        if (tokenizer == null)
        {
            return words;
        }

        var result = new List<SpellCheckWord>(words.Count * 2);
        foreach (var word in words)
        {
            if (!ThaiScript.ContainsThai(word.Text) || word.Text.Length < 2)
            {
                result.Add(word);
                continue;
            }

            var spans = tokenizer.Segment(word.Text);
            if (spans.Count <= 1)
            {
                result.Add(word);
                continue;
            }

            foreach (var span in spans)
            {
                if (string.IsNullOrEmpty(span.Text))
                {
                    continue;
                }

                result.Add(new SpellCheckWord
                {
                    Text = span.Text,
                    Index = word.Index + span.Index,
                });
            }
        }

        return result;
    }

    /// <summary>Segment a single OCR/token string; returns original if not Thai or no tokenizer.</summary>
    public static IReadOnlyList<ThaiTokenSpan> SegmentWord(string text, int absoluteIndex)
    {
        if (string.IsNullOrEmpty(text) || !IsThaiLanguageActive() || !ThaiScript.ContainsThai(text))
        {
            return new[] { new ThaiTokenSpan(absoluteIndex, text ?? string.Empty) };
        }

        var kind = SpellCheckConfig.ThaiSegmenter() ?? ThaiSegmenterKinds.None;
        if (string.Equals(kind, ThaiSegmenterKinds.None, StringComparison.OrdinalIgnoreCase))
        {
            return new[] { new ThaiTokenSpan(absoluteIndex, text) };
        }

        var tokenizer = ThaiTokenizerService.GetActiveTokenizer();
        if (tokenizer == null)
        {
            return new[] { new ThaiTokenSpan(absoluteIndex, text) };
        }

        var spans = tokenizer.Segment(text);
        if (spans.Count == 0)
        {
            return new[] { new ThaiTokenSpan(absoluteIndex, text) };
        }

        var result = new List<ThaiTokenSpan>(spans.Count);
        foreach (var span in spans)
        {
            result.Add(new ThaiTokenSpan(absoluteIndex + span.Index, span.Text));
        }

        return result;
    }
}
