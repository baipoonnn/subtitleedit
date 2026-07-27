using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Nikse.SubtitleEdit.Features.Main;
using Nikse.SubtitleEdit.Logic.Config;

namespace UITests.Features.Main;

public class SubtitleLineViewModelDialogueDashTests
{
    private static Color ColorOf(IBrush brush) => Assert.IsType<SolidColorBrush>(brush).Color;

    [AvaloniaFact]
    public void TextBackgroundBrush_DialogueDashIssue_UsesDialogueDashColor_WhenNotAlsoTooManyLines()
    {
        var originalSettings = Se.Settings;
        var originalColor = SubtitleLineViewModel.DialogueDashErrorColor;
        try
        {
            Se.Settings = new Se();
            Se.Settings.General.ColorTextDialogueDashError = true;
            Se.Settings.General.ColorTextTooManyLines = false;
            SubtitleLineViewModel.DialogueDashErrorColor = Colors.Orange;

            var vm = new SubtitleLineViewModel
            {
                Text = "ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine + "- ฉันมีน้องชายน่า",
            };

            Assert.Equal(Colors.Orange, ColorOf(vm.TextBackgroundBrush));
        }
        finally
        {
            SubtitleLineViewModel.DialogueDashErrorColor = originalColor;
            Se.Settings = originalSettings;
        }
    }

    [AvaloniaFact]
    public void TextBackgroundBrush_NoDialogueDashIssue_StaysTransparent()
    {
        var originalSettings = Se.Settings;
        try
        {
            Se.Settings = new Se();
            Se.Settings.General.ColorTextDialogueDashError = true;

            var vm = new SubtitleLineViewModel
            {
                Text = "- Hi" + Environment.NewLine + "- There",
            };

            Assert.Equal(Colors.Transparent, ColorOf(vm.TextBackgroundBrush));
        }
        finally
        {
            Se.Settings = originalSettings;
        }
    }

    [AvaloniaFact]
    public void TextBackgroundBrush_SettingOff_IgnoresDialogueDashIssue()
    {
        var originalSettings = Se.Settings;
        try
        {
            Se.Settings = new Se();
            Se.Settings.General.ColorTextDialogueDashError = false;
            Se.Settings.General.ColorTextTooManyLines = false;

            var vm = new SubtitleLineViewModel
            {
                Text = "ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine + "- ฉันมีน้องชายน่า",
            };

            Assert.Equal(Colors.Transparent, ColorOf(vm.TextBackgroundBrush));
        }
        finally
        {
            Se.Settings = originalSettings;
        }
    }
}
