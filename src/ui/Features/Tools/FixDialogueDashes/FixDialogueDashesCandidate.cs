using CommunityToolkit.Mvvm.ComponentModel;

namespace Nikse.SubtitleEdit.Features.Tools.FixDialogueDashes;

public partial class FixDialogueDashesCandidate : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public int Index { get; set; }
    public int Number { get; set; }
    public string OriginalText { get; set; } = string.Empty;
    public string FixedText { get; set; } = string.Empty;
    public string OriginalTextDisplay => OriginalText.Replace("\r\n", " ⏎ ").Replace("\n", " ⏎ ");
    public string FixedTextDisplay => FixedText.Replace("\r\n", " ⏎ ").Replace("\n", " ⏎ ");
}
