namespace Nikse.SubtitleEdit.UiLogic.SpellCheck.Thai;

/// <summary>AppData paths for Thai segmenter packs under Se.DataFolder/ThaiSpell.</summary>
public static class ThaiSpellPaths
{
    public const string AttacutOnnxFileName = "attacut-c.onnx";
    public const string AttacutCharactersFileName = "attacut-c-characters.json";
    public const string Nlpo3WordsFileName = "words.txt";

    public const string AttacutOnnxUrl =
        "https://raw.githubusercontent.com/PyThaiNLP/LEKCut/main/lekcut/model/attacut-c.onnx";

    public const string AttacutCharactersUrl =
        "https://raw.githubusercontent.com/PyThaiNLP/LEKCut/main/lekcut/model/attacut-c-characters.json";

    /// <summary>PyThaiNLP-style word list used by dictionary maximal matching (nlpo3/newmm family).</summary>
    public const string Nlpo3WordsUrl =
        "https://raw.githubusercontent.com/PyThaiNLP/LEKCut/main/lekcut/model/oskut-words.txt";

    public static string GetRootFolder()
    {
        var root = SpellCheckConfig.ThaiSpellFolder();
        if (string.IsNullOrWhiteSpace(root))
        {
            return string.Empty;
        }

        return root;
    }

    public static string GetAttacutFolder() => Path.Combine(GetRootFolder(), "attacut-c");

    public static string GetNlpo3Folder() => Path.Combine(GetRootFolder(), "nlpo3");

    public static string GetAttacutOnnxPath() => Path.Combine(GetAttacutFolder(), AttacutOnnxFileName);

    public static string GetAttacutCharactersPath() => Path.Combine(GetAttacutFolder(), AttacutCharactersFileName);

    public static string GetNlpo3WordsPath() => Path.Combine(GetNlpo3Folder(), Nlpo3WordsFileName);

    public static bool IsAttacutInstalled()
    {
        var onnx = GetAttacutOnnxPath();
        var chars = GetAttacutCharactersPath();
        return File.Exists(onnx) && new FileInfo(onnx).Length > 10_000
               && File.Exists(chars) && new FileInfo(chars).Length > 100;
    }

    public static bool IsNlpo3Installed()
    {
        var words = GetNlpo3WordsPath();
        return File.Exists(words) && new FileInfo(words).Length > 1_000;
    }
}
