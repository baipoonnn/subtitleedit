namespace Nikse.SubtitleEdit.UiLogic.SpellCheck.Thai;

public class ThaiWordBreakDisplay
{
    public string Id { get; set; } = ThaiSegmenterKinds.None;
    public string Name { get; set; } = string.Empty;

    public override string ToString() => Name;

    public static List<ThaiWordBreakDisplay> GetAll()
    {
        return
        [
            new ThaiWordBreakDisplay { Id = ThaiSegmenterKinds.None, Name = "None (spaces only)" },
            new ThaiWordBreakDisplay { Id = ThaiSegmenterKinds.AttacutC, Name = "AttaCut (ONNX)" },
            new ThaiWordBreakDisplay { Id = ThaiSegmenterKinds.Nlpo3, Name = "nlpo3 (newmm)" },
        ];
    }
}
