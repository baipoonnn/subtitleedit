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

    [AvaloniaFact]
    public void TextBackgroundBrush_TooManyLinesAndDialogueDashIssue_TooManyLinesColorWins()
    {
        var originalSettings = Se.Settings;
        var originalErrorColor = SubtitleLineViewModel.ErrorColor;
        var originalDialogueDashColor = SubtitleLineViewModel.DialogueDashErrorColor;
        try
        {
            Se.Settings = new Se();
            Se.Settings.General.ColorTextTooManyLines = true;
            Se.Settings.General.MaxNumberOfLines = 2;
            Se.Settings.General.ColorTextDialogueDashError = true;
            SubtitleLineViewModel.ErrorColor = Colors.Red;
            SubtitleLineViewModel.DialogueDashErrorColor = Colors.Orange;

            // 3 plain-text lines (over the 2-line max) where only one line carries
            // a dash - this is both a "too many lines" error and a dialogue dash
            // mismatch. The pre-existing too-many-lines red must win over the newer
            // dash-error color. No tags here, since finding #1's fix makes tagged
            // paragraphs a no-op for the dash fixer.
            var vm = new SubtitleLineViewModel
            {
                Text = "- Line one" + Environment.NewLine +
                       "Line two" + Environment.NewLine +
                       "Line three",
            };

            Assert.Equal(Colors.Red, ColorOf(vm.TextBackgroundBrush));
        }
        finally
        {
            SubtitleLineViewModel.ErrorColor = originalErrorColor;
            SubtitleLineViewModel.DialogueDashErrorColor = originalDialogueDashColor;
            Se.Settings = originalSettings;
        }
    }
}
