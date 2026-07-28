namespace Nikse.SubtitleEdit.UiLogic.SpellCheck.Thai;

/// <summary>Caches the active Thai tokenizer based on SpellCheckConfig.</summary>
public static class ThaiTokenizerService
{
    private static readonly object Sync = new();
    private static IThaiTokenizer? _tokenizer;
    private static string _kind = ThaiSegmenterKinds.None;
    private static string _provider = ThaiOnnxProviders.Cpu;
    private static string _wordsPath = string.Empty;

    public static void Reset()
    {
        lock (Sync)
        {
            if (_tokenizer is IDisposable d)
            {
                d.Dispose();
            }

            _tokenizer = null;
            _kind = ThaiSegmenterKinds.None;
            _provider = ThaiOnnxProviders.Cpu;
            _wordsPath = string.Empty;
        }
    }

    public static IThaiTokenizer? GetActiveTokenizer()
    {
        var kind = SpellCheckConfig.ThaiSegmenter() ?? ThaiSegmenterKinds.None;
        if (string.Equals(kind, ThaiSegmenterKinds.None, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var provider = SpellCheckConfig.ThaiOnnxProvider() ?? ThaiOnnxProviders.Cpu;
        var wordsPath = ThaiSpellPaths.GetNlpo3WordsPath();

        lock (Sync)
        {
            if (_tokenizer != null
                && string.Equals(_kind, kind, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_provider, provider, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_wordsPath, wordsPath, StringComparison.Ordinal))
            {
                return _tokenizer.IsReady ? _tokenizer : null;
            }

            if (_tokenizer is IDisposable d)
            {
                d.Dispose();
            }

            _tokenizer = Create(kind, provider);
            _kind = kind;
            _provider = provider;
            _wordsPath = wordsPath;
            return _tokenizer is { IsReady: true } ? _tokenizer : null;
        }
    }

    private static IThaiTokenizer? Create(string kind, string provider)
    {
        if (string.Equals(kind, ThaiSegmenterKinds.AttacutC, StringComparison.OrdinalIgnoreCase))
        {
            return AttacutCOnnxTokenizer.TryCreate(provider);
        }

        if (string.Equals(kind, ThaiSegmenterKinds.Nlpo3, StringComparison.OrdinalIgnoreCase))
        {
            return NewMmThaiTokenizer.TryLoadFromWordsFile(ThaiSpellPaths.GetNlpo3WordsPath());
        }

        return null;
    }
}
