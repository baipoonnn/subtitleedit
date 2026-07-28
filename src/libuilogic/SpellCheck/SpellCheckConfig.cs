namespace Nikse.SubtitleEdit.UiLogic.SpellCheck;

/// <summary>
/// Config seam for the shared spell-check / OCR-fix engine. The UI wires these to its live
/// <c>Se</c> settings once at startup; seconv wires them to CLI options. Func-based so reads always
/// reflect the current value (no caching/sync bugs). (#11744)
/// </summary>
public static class SpellCheckConfig
{
    /// <summary>Folder holding the Hunspell dictionaries and *_OCRFixReplaceList.xml / word-split lists.</summary>
    public static Func<string> DictionariesFolder { get; set; } = () => string.Empty;

    /// <summary>Whether the OCR-fix engine may use the word-split list to guess glued words.</summary>
    public static Func<bool> UseWordSplitList { get; set; } = () => false;

    /// <summary>English-only: treat words ending in "in'" as "ing" during spell check.</summary>
    public static Func<bool> TreatInApostropheAsIng { get; set; } = () => false;

    /// <summary>Error sink (the UI routes this to Se.LogError; seconv can ignore or print).</summary>
    public static Action<string> LogError { get; set; } = _ => { };

    /// <summary>Folder for Thai segmenter packs (AttaCut ONNX, nlpo3 word list), under AppData/ThaiSpell.</summary>
    public static Func<string> ThaiSpellFolder { get; set; } = () => string.Empty;

    /// <summary>Active Thai segmenter: none | attacut-c | nlpo3.</summary>
    public static Func<string> ThaiSegmenter { get; set; } = () => Thai.ThaiSegmenterKinds.None;

    /// <summary>ONNX EP preference: cpu | directml | cuda.</summary>
    public static Func<string> ThaiOnnxProvider { get; set; } = () => Thai.ThaiOnnxProviders.Cpu;

    /// <summary>Two-letter language of the active Hunspell dictionary (e.g. "th").</summary>
    public static Func<string> ActiveTwoLetterLanguage { get; set; } = () => string.Empty;
}
