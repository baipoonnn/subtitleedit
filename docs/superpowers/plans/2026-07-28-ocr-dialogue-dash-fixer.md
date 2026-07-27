# OCR Dialogue-Dash Fixer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Detect and fix OCR dialogue-dash irregularities (orphan `-` lines, asymmetric missing dashes) in subtitle text, expose them as a Tools dialog, a distinct grid highlight, and an optional OCR post-processing step.

**Architecture:** A pure-text `DialogueDashFixer` core class in `libse` does the detection/fix. A `FixDialogueDashesHelper` in the UI project wraps it per-subtitle-entry to build a checkbox-reviewable candidate list, following the exact pattern of the existing `MergeContinuationLines` tool. Grid highlighting and OCR-pipeline integration are thin call-sites into the same core class.

**Tech Stack:** C# / .NET, Avalonia UI (code-only, no XAML), CommunityToolkit.Mvvm, xUnit v3.

## Global Constraints

- Dash classification: a line is `DashOnly` if `line.Trim() == "-"`; `DashText` if `line.Trim()` starts with `"- "` (dash, space, content) and has length > 2; otherwise `Plain`. This is fixed by the approved spec — do not add support for the no-space dialog style (`-David Smith`) in this pass.
- Join subtitle lines with `Environment.NewLine`, matching the rest of the codebase (see `tests\libse\Forms\FixCommonErrors\FixDialogsOnOneLineTest.cs`).
- Use `Nikse.SubtitleEdit.Core.Common.StringExtensions.SplitToLines()` to split paragraph text into lines (handles `\r\n`, `\r`, `\n`, U+2028).
- No new NuGet dependencies. No mocking library is available in `tests\UI\UITests.csproj` — hand-write fakes for interfaces (e.g. `ILens`) directly in test files.
- Follow the existing `MergeContinuationLines` tool as the structural template for the new Tools dialog (Candidate class + static Helper class + ViewModel + Window), see `src\ui\Features\Tools\MergeContinuationLines\`.

---

### Task 1: `DialogueDashFixer` core algorithm

**Files:**
- Create: `src\libse\Forms\FixCommonErrors\DialogueDashFixer.cs`
- Test: `tests\libse\Forms\FixCommonErrors\DialogueDashFixerTests.cs`

**Interfaces:**
- Produces: `Nikse.SubtitleEdit.Core.Forms.FixCommonErrors.DialogueDashFixer.Analyze(string text) : DialogueDashFixResult`, and `DialogueDashFixResult` with `bool Changed` and `string FixedText` properties. Later tasks (2, 5, 6) call this exact method.

- [ ] **Step 1: Write the failing tests**

```csharp
using Nikse.SubtitleEdit.Core.Forms.FixCommonErrors;

namespace LibSETests.Forms.FixCommonErrors;

public class DialogueDashFixerTests
{
    [Fact]
    public void Analyze_PlainSingleLine_NotChanged()
    {
        var result = DialogueDashFixer.Analyze("Hello there");

        Assert.False(result.Changed);
        Assert.Equal("Hello there", result.FixedText);
    }

    [Fact]
    public void Analyze_PlainMultiLineNoDashEvidence_NotChanged()
    {
        // No line has a dash anywhere, so this is not dialogue - leave it for
        // the generic 3+ lines reflow (Fix3PlusLines), not this fixer.
        var input = "การ์เร็ต เจค็อบ ฮ็อบส์" + Environment.NewLine +
                    "ไม่ได้ฆ่าแคสซี่" + Environment.NewLine +
                    "บอยล์";

        var result = DialogueDashFixer.Analyze(input);

        Assert.False(result.Changed);
        Assert.Equal(input, result.FixedText);
    }

    [Fact]
    public void Analyze_OrphanDashBeforePlainLine_MergesForward()
    {
        var input = "-" + Environment.NewLine +
                    "ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine +
                    "- ฉันมีน้องชายน่า";

        var result = DialogueDashFixer.Analyze(input);

        Assert.True(result.Changed);
        Assert.Equal("- ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine + "- ฉันมีน้องชายน่า", result.FixedText);
    }

    [Fact]
    public void Analyze_AsymmetricMissingDash_AddsDashToBareLine()
    {
        var input = "ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine +
                    "- ฉันมีน้องชายน่า";

        var result = DialogueDashFixer.Analyze(input);

        Assert.True(result.Changed);
        Assert.Equal("- ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine + "- ฉันมีน้องชายน่า", result.FixedText);
    }

    [Fact]
    public void Analyze_OrphanDashAfterPlainLine_MergesBackward()
    {
        var input = "- ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine +
                    "ฉันมีน้องชายน่า" + Environment.NewLine +
                    "-";

        var result = DialogueDashFixer.Analyze(input);

        Assert.True(result.Changed);
        Assert.Equal("- ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine + "- ฉันมีน้องชายน่า", result.FixedText);
    }

    [Fact]
    public void Analyze_FourLineAlternatingOrphans_MergesBoth()
    {
        var input = "-" + Environment.NewLine +
                    "ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine +
                    "ฉันมีน้องชายน่า" + Environment.NewLine +
                    "-";

        var result = DialogueDashFixer.Analyze(input);

        Assert.True(result.Changed);
        Assert.Equal("- ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine + "- ฉันมีน้องชายน่า", result.FixedText);
    }

    [Fact]
    public void Analyze_OrphanDashSandwichedBetweenDashedLines_IsDropped()
    {
        var input = "- ว่าไง" + Environment.NewLine +
                    "-" + Environment.NewLine +
                    "- ไม่ได้มีแค่ซีเจ ลินคอล์น";

        var result = DialogueDashFixer.Analyze(input);

        Assert.True(result.Changed);
        Assert.Equal("- ว่าไง" + Environment.NewLine + "- ไม่ได้มีแค่ซีเจ ลินคอล์น", result.FixedText);
    }

    [Fact]
    public void Analyze_TrailingOrphanDashWithNoPlainNeighbor_IsDropped()
    {
        var input = "- นั่นคือที่อยู่ในชาเหรอคะ" + Environment.NewLine +
                    "- ใช่" + Environment.NewLine +
                    "-";

        var result = DialogueDashFixer.Analyze(input);

        Assert.True(result.Changed);
        Assert.Equal("- นั่นคือที่อยู่ในชาเหรอคะ" + Environment.NewLine + "- ใช่", result.FixedText);
    }

    [Fact]
    public void Analyze_TwoLeadingOrphansBeforeTwoPlainLines_MergesBothViaInvariant()
    {
        var input = "-" + Environment.NewLine +
                    "-" + Environment.NewLine +
                    "พ่อแม่ของเด็กนี่อยู่ไหน" + Environment.NewLine +
                    "ฟาแยตวิล นอร์ธแคโรไลนา";

        var result = DialogueDashFixer.Analyze(input);

        Assert.True(result.Changed);
        Assert.Equal("- พ่อแม่ของเด็กนี่อยู่ไหน" + Environment.NewLine + "- ฟาแยตวิล นอร์ธแคโรไลนา", result.FixedText);
    }

    [Fact]
    public void Analyze_EmptyText_ReturnsNotChanged()
    {
        var result = DialogueDashFixer.Analyze(string.Empty);

        Assert.False(result.Changed);
        Assert.Equal(string.Empty, result.FixedText);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests\libse\LibSETests.csproj --filter DialogueDashFixerTests`
Expected: FAIL — `DialogueDashFixer` does not exist yet (compile error).

- [ ] **Step 3: Implement `DialogueDashFixer`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Core.Forms.FixCommonErrors
{
    public sealed class DialogueDashFixResult
    {
        public bool Changed { get; }
        public string FixedText { get; }

        public DialogueDashFixResult(bool changed, string fixedText)
        {
            Changed = changed;
            FixedText = fixedText;
        }
    }

    /// <summary>
    /// Detects and fixes OCR mis-splits of dash-prefixed dialogue lines: orphan "-" lines
    /// separated from the text they belong to, and dialogue entries where only some lines
    /// carry the leading dash. Subtitle convention: if any line in an entry uses
    /// dash-dialogue formatting, every line in that entry must have the dash.
    /// </summary>
    public static class DialogueDashFixer
    {
        public static DialogueDashFixResult Analyze(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new DialogueDashFixResult(false, text ?? string.Empty);
            }

            var lines = text.SplitToLines();
            var original = new List<string>(lines);

            MergeOrphanDashes(lines);
            EnforceDashInvariant(lines);

            if (LinesEqual(original, lines))
            {
                return new DialogueDashFixResult(false, text);
            }

            return new DialogueDashFixResult(true, string.Join(Environment.NewLine, lines));
        }

        private static void MergeOrphanDashes(List<string> lines)
        {
            var i = 0;
            while (i < lines.Count)
            {
                if (!IsDashOnly(lines[i]))
                {
                    i++;
                    continue;
                }

                if (i + 1 < lines.Count && IsPlain(lines[i + 1]))
                {
                    lines[i + 1] = "- " + lines[i + 1].Trim();
                }
                else if (i - 1 >= 0 && IsPlain(lines[i - 1]))
                {
                    lines[i - 1] = "- " + lines[i - 1].Trim();
                }
                // else: no plain neighbor to attach to - the remaining lines are already
                // all dashed, so dropping this orphan cannot break the dash invariant.

                lines.RemoveAt(i);
            }
        }

        private static void EnforceDashInvariant(List<string> lines)
        {
            var anyDashText = lines.Any(IsDashText);
            var anyPlain = lines.Any(IsPlain);
            if (!anyDashText || !anyPlain)
            {
                return;
            }

            for (var i = 0; i < lines.Count; i++)
            {
                if (IsPlain(lines[i]))
                {
                    lines[i] = "- " + lines[i].Trim();
                }
            }
        }

        private static bool IsDashOnly(string line) => line.Trim() == "-";

        private static bool IsDashText(string line)
        {
            var trimmed = line.Trim();
            return trimmed.StartsWith("- ", StringComparison.Ordinal) && trimmed.Length > 2;
        }

        private static bool IsPlain(string line)
        {
            var trimmed = line.Trim();
            return trimmed.Length > 0 && !IsDashOnly(line) && !IsDashText(line);
        }

        private static bool LinesEqual(List<string> a, List<string> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (var i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests\libse\LibSETests.csproj --filter DialogueDashFixerTests`
Expected: PASS (all 9 tests)

- [ ] **Step 5: Commit**

```bash
git add src/libse/Forms/FixCommonErrors/DialogueDashFixer.cs tests/libse/Forms/FixCommonErrors/DialogueDashFixerTests.cs
git commit -m "Add DialogueDashFixer for OCR dash-dialogue line mis-splits"
```

---

### Task 2: `FixDialogueDashesCandidate` + `FixDialogueDashesHelper`

**Files:**
- Create: `src\ui\Features\Tools\FixDialogueDashes\FixDialogueDashesCandidate.cs`
- Create: `src\ui\Features\Tools\FixDialogueDashes\FixDialogueDashesHelper.cs`
- Test: `tests\UI\Features\Tools\FixDialogueDashes\FixDialogueDashesHelperTests.cs`

**Interfaces:**
- Consumes: `DialogueDashFixer.Analyze(string) : DialogueDashFixResult` (Task 1). `SubtitleLineViewModel` with `Text`, `Number`, and constructor `SubtitleLineViewModel(SubtitleLineViewModel)` copy-constructor (already exists, used identically in `MergeContinuationLinesHelper.Apply`).
- Produces: `FixDialogueDashesHelper.Detect(IReadOnlyList<SubtitleLineViewModel> subtitles) : List<FixDialogueDashesCandidate>` and `FixDialogueDashesHelper.Apply(IReadOnlyList<SubtitleLineViewModel> subtitles, IReadOnlyList<FixDialogueDashesCandidate> candidates) : List<SubtitleLineViewModel>`. Task 3 (ViewModel) calls both.

- [ ] **Step 1: Write the failing test**

```csharp
using Nikse.SubtitleEdit.Features.Main;
using Nikse.SubtitleEdit.Features.Tools.FixDialogueDashes;

namespace UITests.Features.Tools.FixDialogueDashes;

public class FixDialogueDashesHelperTests
{
    private static SubtitleLineViewModel MakeSubtitle(int number, string text) =>
        new() { Number = number, Text = text };

    [Fact]
    public void Detect_AsymmetricDashEntry_ReturnsOneCandidateWithFixedText()
    {
        var subtitles = new List<SubtitleLineViewModel>
        {
            MakeSubtitle(1, "ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine + "- ฉันมีน้องชายน่า"),
            MakeSubtitle(2, "Normal single line"),
        };

        var candidates = FixDialogueDashesHelper.Detect(subtitles);

        Assert.Single(candidates);
        Assert.Equal(1, candidates[0].Number);
        Assert.Equal("- ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine + "- ฉันมีน้องชายน่า", candidates[0].FixedText);
        Assert.True(candidates[0].IsSelected);
    }

    [Fact]
    public void Detect_NoIrregularEntries_ReturnsEmpty()
    {
        var subtitles = new List<SubtitleLineViewModel>
        {
            MakeSubtitle(1, "- Hi" + Environment.NewLine + "- There"),
            MakeSubtitle(2, "Just one plain line"),
        };

        var candidates = FixDialogueDashesHelper.Detect(subtitles);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Apply_OnlyAppliesSelectedCandidates()
    {
        var subtitles = new List<SubtitleLineViewModel>
        {
            MakeSubtitle(1, "ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine + "- ฉันมีน้องชายน่า"),
            MakeSubtitle(2, "เอื้อมเด็ดยากมาก" + Environment.NewLine + "- แม่ผมก็เหมือนกัน"),
        };

        var candidates = FixDialogueDashesHelper.Detect(subtitles);
        Assert.Equal(2, candidates.Count);
        candidates[1].IsSelected = false; // deselect the second candidate

        var result = FixDialogueDashesHelper.Apply(subtitles, candidates);

        Assert.Equal("- ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine + "- ฉันมีน้องชายน่า", result[0].Text);
        Assert.Equal("เอื้อมเด็ดยากมาก" + Environment.NewLine + "- แม่ผมก็เหมือนกัน", result[1].Text); // unchanged
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests\UI\UITests.csproj --filter FixDialogueDashesHelperTests`
Expected: FAIL — `FixDialogueDashesCandidate`/`FixDialogueDashesHelper` do not exist yet (compile error).

- [ ] **Step 3: Implement `FixDialogueDashesCandidate`**

```csharp
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
```

- [ ] **Step 4: Implement `FixDialogueDashesHelper`**

```csharp
using Nikse.SubtitleEdit.Core.Forms.FixCommonErrors;
using Nikse.SubtitleEdit.Features.Main;
using System.Collections.Generic;

namespace Nikse.SubtitleEdit.Features.Tools.FixDialogueDashes;

public static class FixDialogueDashesHelper
{
    public static List<FixDialogueDashesCandidate> Detect(IReadOnlyList<SubtitleLineViewModel> subtitles)
    {
        var result = new List<FixDialogueDashesCandidate>();
        if (subtitles == null)
        {
            return result;
        }

        for (var i = 0; i < subtitles.Count; i++)
        {
            var text = subtitles[i].Text ?? string.Empty;
            var analysis = DialogueDashFixer.Analyze(text);
            if (!analysis.Changed)
            {
                continue;
            }

            result.Add(new FixDialogueDashesCandidate
            {
                Index = i,
                Number = subtitles[i].Number,
                OriginalText = text,
                FixedText = analysis.FixedText,
                IsSelected = true,
            });
        }

        return result;
    }

    public static List<SubtitleLineViewModel> Apply(
        IReadOnlyList<SubtitleLineViewModel> subtitles,
        IReadOnlyList<FixDialogueDashesCandidate> candidates)
    {
        var fixedByIndex = new Dictionary<int, string>();
        foreach (var c in candidates)
        {
            if (c.IsSelected)
            {
                fixedByIndex[c.Index] = c.FixedText;
            }
        }

        var result = new List<SubtitleLineViewModel>(subtitles.Count);
        for (var i = 0; i < subtitles.Count; i++)
        {
            var current = new SubtitleLineViewModel(subtitles[i]);
            if (fixedByIndex.TryGetValue(i, out var fixedText))
            {
                current.Text = fixedText;
            }

            result.Add(current);
        }

        return result;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests\UI\UITests.csproj --filter FixDialogueDashesHelperTests`
Expected: PASS (all 3 tests)

- [ ] **Step 6: Commit**

```bash
git add src/ui/Features/Tools/FixDialogueDashes/FixDialogueDashesCandidate.cs src/ui/Features/Tools/FixDialogueDashes/FixDialogueDashesHelper.cs tests/UI/Features/Tools/FixDialogueDashes/FixDialogueDashesHelperTests.cs
git commit -m "Add FixDialogueDashesHelper to detect and apply dialogue-dash fixes across a subtitle"
```

---

### Task 3: `FixDialogueDashesViewModel` + `FixDialogueDashesWindow`

**Files:**
- Create: `src\ui\Features\Tools\FixDialogueDashes\FixDialogueDashesViewModel.cs`
- Create: `src\ui\Features\Tools\FixDialogueDashes\FixDialogueDashesWindow.cs`
- Modify: `src\ui\Logic\Config\Language\Tools\LanguageTools.cs` (register new language class)

**Interfaces:**
- Consumes: `FixDialogueDashesHelper.Detect`/`Apply` (Task 2). `Se.Language.Tools.FixDialogueDashes.*` strings (created in this task). `UiUtil.InitializeWindow`, `UiUtil.MakeButtonOk`, `UiUtil.MakeButtonCancel`, `UiUtil.MakeButtonBar`, `UiUtil.MakeLabel`, `UiUtil.MakeButton`, `UiUtil.MakeBorderForControlNoPadding`, `UiUtil.SaveWindowPosition`, `UiUtil.RestoreWindowPosition`, `UiUtil.DataGridNoBorderNoPaddingCellTheme`, `UiUtil.DataGridNoBorderCellTheme`, `DataGridCheckboxMultiSelect<T>` — all already used identically by `MergeContinuationLinesWindow`/`ViewModel`.
- Produces: `FixDialogueDashesViewModel` with `Window`, `OkPressed`, `AllSubtitlesFixed`, `Initialize(List<SubtitleLineViewModel>)`, `OkCommand`, `CancelCommand`, `SelectAllCommand`, `InverseSelectionCommand`, `KeyDown`. `FixDialogueDashesWindow(FixDialogueDashesViewModel)`. Task 4 (`MainViewModel`) constructs and shows this window via `ShowDialogAsync<FixDialogueDashesWindow, FixDialogueDashesViewModel>`.

No new unit tests in this task — following the existing `MergeContinuationLines` precedent, only the detection/apply Helper is unit-tested; the Window/ViewModel pairing has no dedicated headless UI test in this codebase (confirmed: `tests\UI\Features\Tools\MergeContinuationLines\` contains only the Helper test). Verify this task by running the app and exercising the dialog manually (see Task 4's manual verification step, once the menu entry exists).

- [ ] **Step 1: Add the language class and register it**

Create `src\ui\Logic\Config\Language\Tools\LanguageFixDialogueDashes.cs`:

```csharp
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
```

In `src\ui\Logic\Config\Language\Tools\LanguageTools.cs`, add the property next to `MergeContinuationLines` (line 25):

```csharp
    public LanguageMergeContinuationLines MergeContinuationLines { get; set; } = new();
    public LanguageFixDialogueDashes FixDialogueDashes { get; set; } = new();
```

- [ ] **Step 2: Implement `FixDialogueDashesViewModel`**

```csharp
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Features.Main;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nikse.SubtitleEdit.Features.Tools.FixDialogueDashes;

public partial class FixDialogueDashesViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<FixDialogueDashesCandidate> _candidates;
    [ObservableProperty] private FixDialogueDashesCandidate? _selectedCandidate;
    [ObservableProperty] private string _candidatesInfo;

    public Window? Window { get; set; }
    public bool OkPressed { get; private set; }
    public List<SubtitleLineViewModel> AllSubtitlesFixed { get; private set; }

    private List<SubtitleLineViewModel> _allSubtitles;

    public FixDialogueDashesViewModel()
    {
        Candidates = new ObservableCollection<FixDialogueDashesCandidate>();
        _allSubtitles = new List<SubtitleLineViewModel>();
        AllSubtitlesFixed = new List<SubtitleLineViewModel>();
        CandidatesInfo = string.Empty;
    }

    public void Initialize(List<SubtitleLineViewModel> subtitles)
    {
        _allSubtitles = subtitles;

        Dispatcher.UIThread.Post(() =>
        {
            Candidates.Clear();
            var detected = FixDialogueDashesHelper.Detect(_allSubtitles);
            foreach (var c in detected)
            {
                Candidates.Add(c);
            }

            CandidatesInfo = detected.Count == 0
                ? Se.Language.Tools.FixDialogueDashes.NoCandidatesFound
                : string.Format(Se.Language.Tools.FixDialogueDashes.CandidatesFoundX, detected.Count);
        });
    }

    [RelayCommand]
    private void Ok()
    {
        AllSubtitlesFixed = FixDialogueDashesHelper.Apply(_allSubtitles, Candidates);
        OkPressed = true;
        Window?.Close();
    }

    [RelayCommand]
    private void Cancel()
    {
        Window?.Close();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var c in Candidates)
        {
            c.IsSelected = true;
        }
    }

    [RelayCommand]
    private void InverseSelection()
    {
        foreach (var c in Candidates)
        {
            c.IsSelected = !c.IsSelected;
        }
    }

    internal void KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Window?.Close();
        }
    }
}
```

- [ ] **Step 3: Implement `FixDialogueDashesWindow`**

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;

namespace Nikse.SubtitleEdit.Features.Tools.FixDialogueDashes;

public class FixDialogueDashesWindow : Window
{
    public FixDialogueDashesWindow(FixDialogueDashesViewModel vm)
    {
        UiUtil.InitializeWindow(this, GetType().Name);
        Title = Se.Language.Tools.FixDialogueDashes.Title;
        CanResize = true;
        Width = 1000;
        Height = 700;
        MinWidth = 700;
        MinHeight = 400;
        vm.Window = this;
        DataContext = vm;

        var buttonOk = UiUtil.MakeButtonOk(vm.OkCommand);
        var buttonCancel = UiUtil.MakeButtonCancel(vm.CancelCommand);
        var panelButtons = UiUtil.MakeButtonBar(buttonOk, buttonCancel);

        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) },
            },
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            Margin = UiUtil.MakeWindowMargin(),
            ColumnSpacing = 10,
            RowSpacing = 10,
            Width = double.NaN,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        grid.Add(MakeCandidatesView(vm), 0);
        grid.Add(MakeSelectionButtonsView(vm), 1);
        grid.Add(panelButtons, 2);

        Content = grid;

        Activated += delegate { buttonOk.Focus(); };
        KeyDown += vm.KeyDown;

        Closing += delegate { UiUtil.SaveWindowPosition(this); };
        Loaded += delegate { UiUtil.RestoreWindowPosition(this); };
    }

    private static Grid MakeCandidatesView(FixDialogueDashesViewModel vm)
    {
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
            },
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            ColumnSpacing = 10,
            RowSpacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var labelInfo = UiUtil.MakeLabel()
            .WithBindText(vm, nameof(vm.CandidatesInfo))
            .WithMarginTop(10)
            .WithMarginLeft(10);

        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            SelectionMode = DataGridSelectionMode.Single,
            CanUserResizeColumns = true,
            CanUserSortColumns = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Width = double.NaN,
            Height = double.NaN,
            DataContext = vm,
            ItemsSource = vm.Candidates,
            Columns =
            {
                new DataGridTemplateColumn
                {
                    Header = Se.Language.Tools.FixDialogueDashes.ColumnApply,
                    CellTheme = UiUtil.DataGridNoBorderNoPaddingCellTheme,
                    CellTemplate = new FuncDataTemplate<FixDialogueDashesCandidate>((_, _) =>
                    {
                        return new Border
                        {
                            Background = Brushes.Transparent,
                            Padding = new Thickness(4),
                            Child = new CheckBox
                            {
                                Focusable = false,
                                [!ToggleButton.IsCheckedProperty] = new Binding(nameof(FixDialogueDashesCandidate.IsSelected))
                                {
                                    Mode = BindingMode.TwoWay,
                                },
                                HorizontalAlignment = HorizontalAlignment.Center,
                            },
                        };
                    }),
                    Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                },
                new DataGridTextColumn
                {
                    Header = Se.Language.General.NumberSymbol,
                    Binding = new Binding(nameof(FixDialogueDashesCandidate.Number)),
                    CellTheme = UiUtil.DataGridNoBorderNoPaddingCellTheme,
                    IsReadOnly = true,
                },
                new DataGridTextColumn
                {
                    Header = Se.Language.Tools.FixDialogueDashes.ColumnOriginal,
                    Binding = new Binding(nameof(FixDialogueDashesCandidate.OriginalTextDisplay)),
                    CellTheme = UiUtil.DataGridNoBorderCellTheme,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    IsReadOnly = true,
                },
                new DataGridTextColumn
                {
                    Header = Se.Language.Tools.FixDialogueDashes.ColumnFixed,
                    Binding = new Binding(nameof(FixDialogueDashesCandidate.FixedTextDisplay)),
                    CellTheme = UiUtil.DataGridNoBorderCellTheme,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    IsReadOnly = true,
                },
            },
        };
        _ = new DataGridCheckboxMultiSelect<FixDialogueDashesCandidate>(dataGrid,
            item => item.IsSelected, (item, v) => item.IsSelected = v);

        grid.Add(labelInfo, 0);
        grid.Add(UiUtil.MakeBorderForControlNoPadding(dataGrid), 1);

        return grid;
    }

    private static StackPanel MakeSelectionButtonsView(FixDialogueDashesViewModel vm)
    {
        return UiUtil.MakeButtonBar(
            UiUtil.MakeButton(Se.Language.Tools.FixDialogueDashes.SelectAll, vm.SelectAllCommand),
            UiUtil.MakeButton(Se.Language.Tools.FixDialogueDashes.InverseSelection, vm.InverseSelectionCommand));
    }
}
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build src\ui\SubtitleEdit.csproj`
Expected: Build succeeds with no errors.

- [ ] **Step 5: Commit**

```bash
git add src/ui/Features/Tools/FixDialogueDashes/FixDialogueDashesViewModel.cs src/ui/Features/Tools/FixDialogueDashes/FixDialogueDashesWindow.cs src/ui/Logic/Config/Language/Tools/LanguageFixDialogueDashes.cs src/ui/Logic/Config/Language/Tools/LanguageTools.cs
git commit -m "Add FixDialogueDashesViewModel and Window dialog"
```

---

### Task 4: Tools menu wiring

**Files:**
- Modify: `src\ui\Features\Main\Layout\InitMenu.cs:499-503` (add menu item after `MergeContinuationLines`)
- Modify: `src\ui\Features\Main\MainViewModel.cs:5745-5773` (add command after `ShowToolsMergeContinuationLines`)
- Modify: `src\ui\Logic\Config\Language\Main\LanguageMainMenu.cs:62` (add menu label)

**Interfaces:**
- Consumes: `FixDialogueDashesWindow`, `FixDialogueDashesViewModel` (Task 3). `MainViewModel.ShowDialogAsync<TWindow, TViewModel>`, `MainViewModel.ReplaceSubtitles`, `MainViewModel.SelectAndScrollToRow`, `MainViewModel.RefreshSubtitlePreview`, `MainViewModel.IsEmpty`, `MainViewModel.ShowSubtitleNotLoadedMessage` — all pre-existing, used identically by `ShowToolsMergeContinuationLines`.
- Produces: `MainViewModel.ShowToolsFixDialogueDashesCommand` — a working end-to-end manual entry point for the whole feature.

- [ ] **Step 1: Add the menu label string**

In `src\ui\Logic\Config\Language\Main\LanguageMainMenu.cs`, add next to `MergeContinuationLines` (around line 62):

```csharp
    public string MergeContinuationLines { get; set; }
    public string FixDialogueDashes { get; set; }
```

And in the constructor, next to line 195:

```csharp
        MergeContinuationLines = "Merge continuation lines...";
        FixDialogueDashes = "Fix dialogue dashes...";
```

- [ ] **Step 2: Add the menu item**

In `src\ui\Features\Main\Layout\InitMenu.cs`, after the `MergeContinuationLines` entry (line 499-503):

```csharp
            new MenuItem
            {
                Header = l.MergeContinuationLines,
                Command = vm.ShowToolsMergeContinuationLinesCommand,
            },
            new MenuItem
            {
                Header = l.FixDialogueDashes,
                Command = vm.ShowToolsFixDialogueDashesCommand,
            },
```

- [ ] **Step 3: Add the command**

In `src\ui\Features\Main\MainViewModel.cs`, after `ShowToolsMergeContinuationLines` (after line 5772, before `ShowToolsRemoveTextForHearingImpaired`):

```csharp
    [RelayCommand]
    private async Task ShowToolsFixDialogueDashes()
    {
        if (Window == null)
        {
            return;
        }

        if (IsEmpty)
        {
            ShowSubtitleNotLoadedMessage();
            return;
        }

        var result = await ShowDialogAsync<FixDialogueDashesWindow, FixDialogueDashesViewModel>(
            vm => { vm.Initialize(Subtitles.ToList()); });

        if (result.OkPressed)
        {
            ReplaceSubtitles(result.AllSubtitlesFixed);
            SelectAndScrollToRow(0);
            _updateAudioVisualizer = true;
            RefreshSubtitlePreview();
        }
    }
```

Add the using directive near the top of `MainViewModel.cs` (alongside the existing `using Nikse.SubtitleEdit.Features.Tools.MergeContinuationLines;`):

```csharp
using Nikse.SubtitleEdit.Features.Tools.FixDialogueDashes;
```

- [ ] **Step 4: Build and manually verify**

Run: `dotnet build src\ui\SubtitleEdit.csproj`
Expected: Build succeeds.

Manual verification (per this codebase's precedent of not headless-UI-testing dialogs):
1. Run the app, load an `.srt` containing an entry like `"ผมอยากหาอะไรมาปิดตัว\n- ฉันมีน้องชายน่า"`.
2. Open Tools → Fix dialogue dashes...
3. Confirm the entry appears in the list with the corrected "Fixed" preview showing both lines dashed.
4. Uncheck it, click OK, confirm the entry in the grid is unchanged.
5. Reopen the dialog, leave it checked, click OK, confirm the entry in the grid now has both lines dashed.

- [ ] **Step 5: Commit**

```bash
git add src/ui/Features/Main/Layout/InitMenu.cs src/ui/Features/Main/MainViewModel.cs src/ui/Logic/Config/Language/Main/LanguageMainMenu.cs
git commit -m "Wire Fix Dialogue Dashes into the Tools menu"
```

---

### Task 5: Grid highlighting for dialogue-dash irregularities

**Files:**
- Modify: `src\ui\Logic\Config\SeGeneral.cs:82-87` (new `ColorTextDialogueDashError` bool + `DialogueDashErrorColor` string properties, plus constructor defaults)
- Modify: `src\ui\Logic\Config\Language\Options\LanguageSettings.cs:129-133` (new language strings)
- Modify: `src\ui\Features\Options\Settings\SettingsViewModel.cs` (new `ColorTextDialogueDashError` bool + `DialogueDashErrorColor` Color observable properties, load/save wiring)
- Modify: `src\ui\Features\Options\Settings\SettingsPage.cs:385-396` (new checkbox + color picker row)
- Modify: `src\ui\Features\Main\SubtitleLineViewModel.cs` (new static brush, `HasDialogueDashError`, `TextBackgroundBrush` composition, `GetErrors` message)
- Modify: `src\ui\Features\Main\MainViewModel.cs:9163` (push the new static color on settings apply)
- Test: `tests\UI\Features\Main\SubtitleLineViewModelDialogueDashTests.cs`

**Interfaces:**
- Consumes: `DialogueDashFixer.Analyze` (Task 1).
- Produces: `SubtitleLineViewModel.DialogueDashErrorColor` (static `Color` property, mirrors existing `ErrorColor`), and `Se.Settings.General.ColorTextDialogueDashError` / `Se.Settings.General.DialogueDashErrorColor`.

- [ ] **Step 1: Write the failing test**

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests\UI\UITests.csproj --filter SubtitleLineViewModelDialogueDashTests`
Expected: FAIL — `ColorTextDialogueDashError`/`DialogueDashErrorColor` do not exist yet (compile error).

- [ ] **Step 3: Add settings backing fields**

In `src\ui\Logic\Config\SeGeneral.cs`, add next to `ColorTextTooManyLines` (line 82):

```csharp
    public bool ColorTextTooManyLines { get; set; }
    public bool ColorTextDialogueDashError { get; set; }
```

And add a color property next to `ErrorColor` (line 87):

```csharp
    public string ErrorColor { get; set; }
    public string DialogueDashErrorColor { get; set; }
```

In the constructor, next to line 197 and line 202:

```csharp
        ColorTextTooManyLines = true;
        ColorTextDialogueDashError = true;
```

```csharp
        ErrorColor = Color.FromArgb(50, 255, 0, 0).FromColorToHex();
        DialogueDashErrorColor = Color.FromArgb(50, 255, 165, 0).FromColorToHex();
```

- [ ] **Step 4: Add the language strings**

In `src\ui\Logic\Config\Language\Options\LanguageSettings.cs`, next to line 130:

```csharp
    public string ColorTextTooManyLinesX { get; set; }
    public string ColorTextDialogueDashError { get; set; }
```

And next to line 462:

```csharp
        ColorTextTooManyLinesX = "Color text if more than {0} lines";
        ColorTextDialogueDashError = "Color text if dialogue dash mismatch";
```

- [ ] **Step 5: Add ViewModel observable properties and load/save wiring**

In `src\ui\Features\Options\Settings\SettingsViewModel.cs`, next to line 223:

```csharp
    [ObservableProperty] private bool _colorTextTooManyLines;
    [ObservableProperty] private bool _colorTextDialogueDashError;
```

Add a `Color` property next to `_errorColor` (`src\ui\Features\Options\Settings\SettingsViewModel.cs:228`):

```csharp
    [ObservableProperty] private Color _errorColor;
    [ObservableProperty] private Color _dialogueDashErrorColor;
```

Load, next to line 944-949:

```csharp
        ColorTextTooManyLines = general.ColorTextTooManyLines;
        ColorTextDialogueDashError = general.ColorTextDialogueDashError;
        ColorCharactersPerSecond = general.ColorCharactersPerSecond;
        ColorWordsPerMinute = general.ColorWordsPerMinute;
        ColorOverlap = general.ColorTimeCodeOverlap;
        ColorGapTooShort = general.ColorGapTooShort;
        ErrorColor = general.ErrorColor.FromHexToColor();
        DialogueDashErrorColor = general.DialogueDashErrorColor.FromHexToColor();
```

Save, next to line 1665-1670:

```csharp
        general.ColorTextTooManyLines = ColorTextTooManyLines;
        general.ColorTextDialogueDashError = ColorTextDialogueDashError;
        general.ColorCharactersPerSecond = ColorCharactersPerSecond;
        general.ColorWordsPerMinute = ColorWordsPerMinute;
        general.ColorTimeCodeOverlap = ColorOverlap;
        general.ColorGapTooShort = ColorGapTooShort;
        general.ErrorColor = ErrorColor.FromColorToHex();
        general.DialogueDashErrorColor = DialogueDashErrorColor.FromColorToHex();
```

Cancel/revert path, next to line 2422-2427:

```csharp
                Se.Settings.General.ColorTextTooManyLines = g.ColorTextTooManyLines;
                Se.Settings.General.ColorTextDialogueDashError = g.ColorTextDialogueDashError;
                Se.Settings.General.ColorCharactersPerSecond = g.ColorCharactersPerSecond;
                Se.Settings.General.ColorWordsPerMinute = g.ColorWordsPerMinute;
                Se.Settings.General.ColorTimeCodeOverlap = g.ColorTimeCodeOverlap;
                Se.Settings.General.ColorGapTooShort = g.ColorGapTooShort;
                Se.Settings.General.ErrorColor = g.ErrorColor;
                Se.Settings.General.DialogueDashErrorColor = g.DialogueDashErrorColor;
```

- [ ] **Step 6: Add the Settings page checkbox and color picker**

In `src\ui\Features\Options\Settings\SettingsPage.cs`, next to line 385-396:

```csharp
            MakeCheckboxSetting(Se.Language.Options.Settings.ColorTextTooManyLines, nameof(_vm.ColorTextTooManyLines),
                labelBindingPath: nameof(_vm.ColorTextTooManyLinesLabel)),
            MakeCheckboxSetting(Se.Language.Options.Settings.ColorTextDialogueDashError, nameof(_vm.ColorTextDialogueDashError)),
            MakeSeparator(),
            MakeCheckboxSetting(Se.Language.Options.Settings.ColorCharactersPerSecond, nameof(_vm.ColorCharactersPerSecond)),
            MakeCheckboxSetting(Se.Language.Options.Settings.ColorWordsPerMinute, nameof(_vm.ColorWordsPerMinute)),
            MakeSeparator(),
            MakeCheckboxSetting(Se.Language.Options.Settings.ColorOverlap, nameof(_vm.ColorOverlap)),
            MakeSeparator(),
            MakeCheckboxSetting(Se.Language.Options.Settings.ColorGapTooShort, nameof(_vm.ColorGapTooShort)),
            MakeSeparator(),
            new SettingsItem(Se.Language.Options.Settings.ErrorBackgroundColor, () => UiUtil.MakeColorPickerButton(_vm, nameof(_vm.ErrorColor))),
            new SettingsItem(Se.Language.Options.Settings.ColorTextDialogueDashError, () => UiUtil.MakeColorPickerButton(_vm, nameof(_vm.DialogueDashErrorColor))),
```

- [ ] **Step 7: Add the static brush, detection, and composition in `SubtitleLineViewModel`**

Next to line 85-94 (the existing `_errorBrush`/`ErrorColor` static plumbing), add:

```csharp
    private static SolidColorBrush _dialogueDashErrorBrush = new SolidColorBrush(Se.Settings.General.DialogueDashErrorColor.FromHexToColor());
    public static Color DialogueDashErrorColor
    {
        get => field;
        set
        {
            field = value;
            _dialogueDashErrorBrush = new SolidColorBrush(value);
        }
    }
```

Extend `TextErrorSettings` (line 358-384) with the new flag:

```csharp
    private readonly record struct TextErrorSettings(
        bool ColorTextTooLong,
        int MaxLineLength,
        bool ColorTextTooWide,
        int MaxPixelWidth,
        string FontName,
        int FontSize,
        bool ColorTextTooManyLines,
        int MaxNumberOfLines,
        string? LengthStrategy,
        bool ColorTextDialogueDashError)
    {
        public static TextErrorSettings Current()
        {
            var general = Se.Settings.General;
            return new TextErrorSettings(
                general.ColorTextTooLong,
                general.SubtitleLineMaximumLength,
                general.ColorTextTooWide,
                general.ColorTextTooWidePixels,
                general.ColorTextTooWideFontName,
                general.ColorTextTooWideFontSize,
                general.ColorTextTooManyLines,
                general.MaxNumberOfLines,
                Configuration.Settings.General.CpsLineLengthStrategy,
                general.ColorTextDialogueDashError);
        }
    }
```

Replace the `TextBackgroundBrush` getter and cache (line 386-451) to compute and combine both checks:

```csharp
    private string? _textErrorCacheText;
    private TextErrorSettings _textErrorCacheSettings;
    private bool _textErrorCacheValue;
    private bool _dialogueDashErrorCacheValue;

    public IBrush TextBackgroundBrush
    {
        get
        {
            if (string.IsNullOrEmpty(Text))
            {
                return _transparentBrush;
            }

            var settings = TextErrorSettings.Current();
            if (!ReferenceEquals(_textErrorCacheText, Text) || !_textErrorCacheSettings.Equals(settings))
            {
                _textErrorCacheText = Text;
                _textErrorCacheSettings = settings;
                _textErrorCacheValue = HasTextError(Text, settings);
                _dialogueDashErrorCacheValue = settings.ColorTextDialogueDashError && HasDialogueDashError(Text);
            }

            if (_textErrorCacheValue)
            {
                return _errorBrush;
            }

            if (_dialogueDashErrorCacheValue)
            {
                return _dialogueDashErrorBrush;
            }

            return _transparentBrush;
        }
    }

    private static bool HasDialogueDashError(string text)
    {
        return DialogueDashFixer.Analyze(text).Changed;
    }
```

(Leave `HasTextError` itself unchanged — it still only covers too-long/too-wide/too-many-lines, as before.)

Add the using directive near the top of `SubtitleLineViewModel.cs`:

```csharp
using Nikse.SubtitleEdit.Core.Forms.FixCommonErrors;
```

Add the tooltip message in `GetErrors` (line 864-867):

```csharp
        if (lineCount > general.MaxNumberOfLines && Se.Settings.General.ColorTextTooManyLines)
        {
            errors.AppendLine("Max #lines: " + lineCount + " >" + general.MaxNumberOfLines);
        }

        if (Se.Settings.General.ColorTextDialogueDashError && DialogueDashFixer.Analyze(Text).Changed)
        {
            errors.AppendLine("Dialogue dash mismatch");
        }
```

- [ ] **Step 8: Push the new static color on settings apply**

In `src\ui\Features\Main\MainViewModel.cs`, next to line 9163:

```csharp
        SubtitleLineViewModel.ErrorColor = Se.Settings.General.ErrorColor.FromHexToColor();
        SubtitleLineViewModel.DialogueDashErrorColor = Se.Settings.General.DialogueDashErrorColor.FromHexToColor();
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test tests\UI\UITests.csproj --filter SubtitleLineViewModelDialogueDashTests`
Expected: PASS (all 3 tests)

- [ ] **Step 10: Build the full UI project**

Run: `dotnet build src\ui\SubtitleEdit.csproj`
Expected: Build succeeds (confirms all the scattered Settings/MainViewModel edits compile together).

- [ ] **Step 11: Commit**

```bash
git add src/ui/Logic/Config/SeGeneral.cs src/ui/Logic/Config/Language/Options/LanguageSettings.cs src/ui/Features/Options/Settings/SettingsViewModel.cs src/ui/Features/Options/Settings/SettingsPage.cs src/ui/Features/Main/SubtitleLineViewModel.cs src/ui/Features/Main/MainViewModel.cs tests/UI/Features/Main/SubtitleLineViewModelDialogueDashTests.cs
git commit -m "Add distinct grid highlight for dialogue-dash irregularities"
```

---

### Task 6: OCR pipeline integration

**Files:**
- Modify: `src\ui\Logic\Config\SeOcr.cs:46-48` (new `DoFixDialogueDashes` bool)
- Modify: `src\ui\Logic\Config\Language\Ocr\LanguageOcr.cs:45-46,158-159` (new checkbox label)
- Modify: `src\ui\Features\Ocr\OcrViewModel.cs` (new `DoFixDialogueDashes` observable property, load/save, and calls in `OcrFixLine`/`OcrFixLineAndSetText`)
- Modify: `src\ui\Features\Ocr\OcrWindow.cs:746-767` (new checkbox in the options panel)

**Interfaces:**
- Consumes: `DialogueDashFixer.Analyze` (Task 1).
- Produces: `OcrViewModel.DoFixDialogueDashes` (bool, persisted setting), applied automatically inside the existing `OcrFixLine`/`OcrFixLineAndSetText` per-item post-processing.

No new unit test in this task: `OcrFixLine`/`OcrFixLineAndSetText` are private methods on `OcrViewModel` with heavy dependencies (dictionary engine, dispatcher, alignment) not covered by any existing test, so adding one here would require a disproportionate amount of unrelated test scaffolding. `DialogueDashFixer` itself is already fully tested (Task 1); this task is a thin, visually-verifiable call-site wiring. Verify manually per Step 4 below.

- [ ] **Step 1: Add the persisted setting**

In `src\ui\Logic\Config\SeOcr.cs`, next to line 46:

```csharp
    public bool DoAutoBreak { get; set; }
    public bool DoFixDialogueDashes { get; set; }
```

In the constructor, next to line 63-64:

```csharp
        DoFixOcrErrors = true;
        DoTryToGuessUnknownWords = true;
        DoFixDialogueDashes = true;
```

- [ ] **Step 2: Add the checkbox label string**

In `src\ui\Logic\Config\Language\Ocr\LanguageOcr.cs`, next to line 45:

```csharp
    public string AutoBreakIfMoreThanXLines { get; set; }
    public string FixDialogueDashesAfterOcr { get; set; }
```

Next to line 158:

```csharp
        AutoBreakIfMoreThanXLines = "Auto break if more than {0} lines";
        FixDialogueDashesAfterOcr = "Auto-fix dialogue dashes after OCR";
```

- [ ] **Step 3: Add the ViewModel property, load/save, and fix call-sites**

In `src\ui\Features\Ocr\OcrViewModel.cs`, next to line 138:

```csharp
    [ObservableProperty] private bool _doAutoBreak;
    [ObservableProperty] private bool _doFixDialogueDashes;
```

Load, next to line 320:

```csharp
            DoAutoBreak = ocr.DoAutoBreak;
            DoFixDialogueDashes = ocr.DoFixDialogueDashes;
```

Save, next to line 355:

```csharp
        ocr.DoAutoBreak = DoAutoBreak;
        ocr.DoFixDialogueDashes = DoFixDialogueDashes;
```

Apply in `OcrFixLine`, next to line 3639-3642:

```csharp
        if (DoAutoBreak)
        {
            item.Text = Utilities.AutoBreakLine(item.Text);
        }

        if (DoFixDialogueDashes)
        {
            item.Text = DialogueDashFixer.Analyze(item.Text).FixedText;
        }
```

Apply in `OcrFixLineAndSetText`, next to line 3730-3733:

```csharp
        if (DoAutoBreak)
        {
            item.Text = Utilities.AutoBreakLine(item.Text);
        }

        if (DoFixDialogueDashes)
        {
            item.Text = DialogueDashFixer.Analyze(item.Text).FixedText;
        }
```

Add the using directive near the top of `OcrViewModel.cs`:

```csharp
using Nikse.SubtitleEdit.Core.Forms.FixCommonErrors;
```

- [ ] **Step 4: Add the checkbox to the OCR options panel**

In `src\ui\Features\Ocr\OcrWindow.cs`, next to line 752-753:

```csharp
        var checkBoxAutoBreak = UiUtil.MakeCheckBox(string.Format(Se.Language.Ocr.AutoBreakIfMoreThanXLines, Se.Settings.General.MaxNumberOfLines), vm, nameof(vm.DoAutoBreak))
            .WithBindIsVisible(nameof(vm.IsDictionaryLoaded));
        var checkBoxFixDialogueDashes = UiUtil.MakeCheckBox(Se.Language.Ocr.FixDialogueDashesAfterOcr, vm, nameof(vm.DoFixDialogueDashes));
```

Next to line 763-767:

```csharp
            Children =
            {
                panelDictionary,
                checkBoxFixOcrErrors,
                checkBoxPromptForUnknownWords,
                checkBoxTryToGuessUnknownWords,
                checkBoxAutoBreak,
                checkBoxFixDialogueDashes,
            }
```

Note: `checkBoxFixDialogueDashes` is not gated on `.WithBindIsVisible(nameof(vm.IsDictionaryLoaded))` — unlike the dictionary-dependent checkboxes above it, this fix works without a loaded spell-check dictionary, so it should always be visible.

- [ ] **Step 5: Build and manually verify**

Run: `dotnet build src\ui\SubtitleEdit.csproj`
Expected: Build succeeds.

Manual verification:
1. Run the app, open the OCR dialog on a subtitle stream that includes a two-speaker dash line.
2. Confirm the new "Auto-fix dialogue dashes after OCR" checkbox is visible and checked by default.
3. Run OCR; for an entry whose OCR'd text has an orphan dash or asymmetric dash, confirm the result text shown already has both lines dashed correctly.
4. Uncheck the box, re-run OCR on the same source, confirm the raw (unfixed) OCR text is shown instead.

- [ ] **Step 6: Commit**

```bash
git add src/ui/Logic/Config/SeOcr.cs src/ui/Logic/Config/Language/Ocr/LanguageOcr.cs src/ui/Features/Ocr/OcrViewModel.cs src/ui/Features/Ocr/OcrWindow.cs
git commit -m "Auto-fix dialogue dashes after OCR (opt-out checkbox)"
```
