namespace Nikse.SubtitleEdit.Logic.Config.Language;

public class LanguageFixDialogueDashes
{
    public string Title { get; set; }
    public string CandidatesFoundX { get; set; }
    public string NoCandidatesFound { get; set; }
    public string ColumnApply { get; set; }
    public string ColumnOriginal { get; set; }
    public string ColumnFixed { get; set; }
    public string SelectAll { get; set; }
    public string InverseSelection { get; set; }

    public LanguageFixDialogueDashes()
    {
        Title = "Fix dialogue dashes";
        CandidatesFoundX = "Dialogue dash issues found: {0}";
        NoCandidatesFound = "No dialogue dash issues found";
        ColumnApply = "Apply";
        ColumnOriginal = "Original";
        ColumnFixed = "Fixed";
        SelectAll = "Select all";
        InverseSelection = "Inverse selection";
    }
}
