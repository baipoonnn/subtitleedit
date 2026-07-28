# OCR Refresh Spell-Check After Name / User Dictionary Add — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** After adding an unknown word to the names list or user dictionary, re-spell-check already OCR’d lines that listed that word as unknown so highlights and the unknown-words list update without re-OCR.

**Architecture:** Add a private refresh helper on `OcrViewModel` that reloads name lists once, finds lines via existing `UnknownWords` entries matching the added word(s), re-runs `FixOcrErrors` with guessing off on those lines only, and rebuilds their unknown-word entries. Pure selection/normalization helpers are `internal static` so they can be unit-tested without constructing the full DI-backed view model. Wire sidebar and in-OCR prompt call sites.

**Tech Stack:** C# / .NET, Avalonia UI, CommunityToolkit.Mvvm, xUnit.

## Global Constraints

- Scope: names list + user dictionary only. Do **not** change `AddUnknownWordToOcrPair`.
- Match unknown words with `StringComparison.Ordinal` on `UnknownWordItem.Word.Word` (same as `UnknownWordsRemoveCurrent`).
- For user-dictionary adds, pass the **original OCR unknown word text** (as shown in `UnknownWords`) into the refresh helper for matching — not the lowercased dialog `Word` (`AddToUserDictionaryViewModel.Initialize` lowercases for storage).
- Re-fix with `doTryToGuessUnknownWords: false` (same posture as `RefreshOcrAfterThaiWordBreakChange`).
- Helper owns `_ocrFixEngine.ReloadNames()`; call sites must not double-call reload after invoking the helper.
- Line index for `FixOcrErrors`: use `_allOcrSubtitleItems.IndexOf(item)` (stable when forced-only filter is on). Skip items with index `< 0`.
- No new NuGet dependencies. No changes to `IOcrFixEngine` unless unavoidable.
- Spec: `docs/superpowers/specs/2026-07-29-ocr-refresh-spellcheck-after-name-added-design.md`.

## File structure

| File | Responsibility |
| --- | --- |
| `src/ui/Features/Ocr/OcrViewModel.cs` | Static word-list helpers; `RefreshSpellCheckAfterDictionaryWordAdded`; sidebar + prompt call sites |
| `tests/UI/Features/Ocr/OcrRefreshSpellCheckAfterDictionaryTests.cs` | Unit tests for static selection/normalization helpers |

---

### Task 1: Testable word-selection helpers

**Files:**
- Modify: `src/ui/Features/Ocr/OcrViewModel.cs` (add static helpers near other OCR static helpers / after `GetUnknownWordItems`)
- Create: `tests/UI/Features/Ocr/OcrRefreshSpellCheckAfterDictionaryTests.cs`

**Interfaces:**
- Produces:
  - `OcrViewModel.NormalizeWordsForSpellCheckRefresh(IEnumerable<string> words) : List<string>` — trim, drop empty, distinct with `Ordinal` comparer, preserve first-seen order.
  - `OcrViewModel.CollectItemsWithMatchingUnknownWords(IEnumerable<UnknownWordItem> unknownWords, IEnumerable<string> words) : List<OcrSubtitleItem>` — distinct items whose `Word.Word` equals any normalized word with `Ordinal`.
- Consumes: `UnknownWordItem`, `OcrSubtitleItem` (existing types).

- [ ] **Step 1: Write the failing tests**

Create `tests/UI/Features/Ocr/OcrRefreshSpellCheckAfterDictionaryTests.cs`.

`OcrSubtitleItem` requires `IOcrSubtitle` — build items via `OcrSubtitleDummy` + a tiny `Subtitle`:

```csharp
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Ocr;
using Nikse.SubtitleEdit.Features.Ocr.OcrSubtitle;
using Nikse.SubtitleEdit.UiLogic.Ocr.FixEngine;

namespace UITests.Features.Ocr;

public class OcrRefreshSpellCheckAfterDictionaryTests
{
    [Fact]
    public void NormalizeWordsForSpellCheckRefresh_TrimsDropsEmptyDistinct()
    {
        var result = OcrViewModel.NormalizeWordsForSpellCheckRefresh(new[]
        {
            "  Alice ",
            "",
            "Bob",
            "Alice",
            "   ",
            "Bob",
        });

        Assert.Equal(new[] { "Alice", "Bob" }, result);
    }

    [Fact]
    public void CollectItemsWithMatchingUnknownWords_ReturnsDistinctMatchingItems()
    {
        var items = MakeItems(3);
        var item1 = items[0];
        var item2 = items[1];
        var item3 = items[2];

        var unknown = new List<UnknownWordItem>
        {
            MakeUnknown(item1, "Alice"),
            MakeUnknown(item2, "Alice"),
            MakeUnknown(item2, "Bob"),
            MakeUnknown(item3, "Charlie"),
        };

        var result = OcrViewModel.CollectItemsWithMatchingUnknownWords(unknown, new[] { "Alice" });

        Assert.Equal(2, result.Count);
        Assert.Contains(item1, result);
        Assert.Contains(item2, result);
        Assert.DoesNotContain(item3, result);
    }

    [Fact]
    public void CollectItemsWithMatchingUnknownWords_OrdinalCaseSensitive()
    {
        var item = MakeItems(1)[0];
        var unknown = new List<UnknownWordItem> { MakeUnknown(item, "Alice") };

        var result = OcrViewModel.CollectItemsWithMatchingUnknownWords(unknown, new[] { "alice" });

        Assert.Empty(result);
    }

    private static List<OcrSubtitleItem> MakeItems(int count)
    {
        var subtitle = new Subtitle();
        for (var i = 0; i < count; i++)
        {
            subtitle.Paragraphs.Add(new Paragraph(
                new TimeCode(i * 1000),
                new TimeCode(i * 1000 + 900),
                $"line {i + 1}"));
        }

        return new OcrSubtitleDummy(subtitle).MakeOcrSubtitleItems();
    }

    private static UnknownWordItem MakeUnknown(OcrSubtitleItem item, string word)
    {
        var part = new OcrFixLinePartResult
        {
            Word = word,
            FixedWord = word,
            IsSpellCheckedOk = false,
            LinePartType = OcrFixLinePartType.Word,
        };
        var result = new OcrFixLineResult
        {
            LineIndex = item.Number - 1,
            Words = new List<OcrFixLinePartResult> { part },
        };
        return new UnknownWordItem(item, result, part);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run (from repo root, adjust if the solution uses a different test project path):

```powershell
dotnet test tests/UI/UITests.csproj --filter "FullyQualifiedName~OcrRefreshSpellCheckAfterDictionaryTests" --no-restore
```

If restore is needed, run without `--no-restore`.

Expected: FAIL — `NormalizeWordsForSpellCheckRefresh` / `CollectItemsWithMatchingUnknownWords` not found.

- [ ] **Step 3: Implement the static helpers**

In `OcrViewModel.cs`, near `GetUnknownWordItems` (~line 3449), add:

```csharp
internal static List<string> NormalizeWordsForSpellCheckRefresh(IEnumerable<string> words)
{
    var result = new List<string>();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (var word in words)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            continue;
        }

        var trimmed = word.Trim();
        if (seen.Add(trimmed))
        {
            result.Add(trimmed);
        }
    }

    return result;
}

internal static List<OcrSubtitleItem> CollectItemsWithMatchingUnknownWords(
    IEnumerable<UnknownWordItem> unknownWords,
    IEnumerable<string> words)
{
    var wordSet = new HashSet<string>(NormalizeWordsForSpellCheckRefresh(words), StringComparer.Ordinal);
    if (wordSet.Count == 0)
    {
        return new List<OcrSubtitleItem>();
    }

    var items = new List<OcrSubtitleItem>();
    var seenItems = new HashSet<OcrSubtitleItem>();
    foreach (var unknownWord in unknownWords)
    {
        if (!wordSet.Contains(unknownWord.Word.Word))
        {
            continue;
        }

        if (seenItems.Add(unknownWord.Item))
        {
            items.Add(unknownWord.Item);
        }
    }

    return items;
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test tests/UI/UITests.csproj --filter "FullyQualifiedName~OcrRefreshSpellCheckAfterDictionaryTests"
```

Expected: PASS (all three facts).

- [ ] **Step 5: Commit**

```powershell
git add src/ui/Features/Ocr/OcrViewModel.cs tests/UI/Features/Ocr/OcrRefreshSpellCheckAfterDictionaryTests.cs
git commit -m "Add helpers to select OCR lines for dictionary-word spell-check refresh."
```

---

### Task 2: Refresh helper + sidebar call sites

**Files:**
- Modify: `src/ui/Features/Ocr/OcrViewModel.cs` — `AddUnknownWordToNames` (~591–614), `AddUnknownWordToUserDictionary` (~616–634), new private `RefreshSpellCheckAfterDictionaryWordAdded`

**Interfaces:**
- Consumes: `NormalizeWordsForSpellCheckRefresh`, `CollectItemsWithMatchingUnknownWords`, `GetUnknownWordItems`, `_ocrFixEngine.ReloadNames()`, `_ocrFixEngine.FixOcrErrors(...)`, `_allOcrSubtitleItems`
- Produces: `private void RefreshSpellCheckAfterDictionaryWordAdded(IEnumerable<string> words)`

- [ ] **Step 1: Implement `RefreshSpellCheckAfterDictionaryWordAdded`**

Place it next to `RefreshSpellCheckColoring` (~3438):

```csharp
private void RefreshSpellCheckAfterDictionaryWordAdded(IEnumerable<string> words)
{
    var normalized = NormalizeWordsForSpellCheckRefresh(words);
    if (normalized.Count == 0)
    {
        return;
    }

    if (!_ocrFixEngine.IsLoaded() ||
        SelectedDictionary == null ||
        SelectedDictionary.Name == GetDictionaryNameNone())
    {
        return;
    }

    _ocrFixEngine.ReloadNames();

    var affectedItems = CollectItemsWithMatchingUnknownWords(UnknownWords, normalized);
    if (affectedItems.Count == 0)
    {
        return;
    }

    foreach (var item in affectedItems)
    {
        var lineIndex = _allOcrSubtitleItems.IndexOf(item);
        if (lineIndex < 0)
        {
            continue;
        }

        if (string.IsNullOrWhiteSpace(item.Text))
        {
            item.FixResult = null;
            var emptyRemovals = UnknownWords.Where(uw => uw.Item == item).ToList();
            foreach (var uw in emptyRemovals)
            {
                UnknownWords.Remove(uw);
            }

            continue;
        }

        var result = _ocrFixEngine.FixOcrErrors(lineIndex, item.Text, doTryToGuessUnknownWords: false);
        item.FixResult = result;

        var removals = UnknownWords.Where(uw => uw.Item == item).ToList();
        foreach (var uw in removals)
        {
            UnknownWords.Remove(uw);
        }

        foreach (var unknownWordItem in GetUnknownWordItems(item, result))
        {
            UnknownWords.Add(unknownWordItem);
        }
    }
}
```

- [ ] **Step 2: Wire `AddUnknownWordToNames`**

Replace the success body so it no longer only calls `ReloadNames()`. After `OkPressed`:

```csharp
if (result.OkPressed)
{
    IEnumerable<string> wordsToRefresh = result.IsMultiMode
        ? result.MultiNames.SplitToLines()
        : new[] { result.Name };

    RefreshSpellCheckAfterDictionaryWordAdded(wordsToRefresh);
}
```

Ensure `using Nikse.SubtitleEdit.Core.Common;` (or whichever namespace provides `SplitToLines`) is already present — `OcrViewModel.cs` already uses core utilities heavily.

- [ ] **Step 3: Wire `AddUnknownWordToUserDictionary`**

Capture the original OCR word **before** the dialog (already have `selectedWord`). After `OkPressed`, refresh using the original casing:

```csharp
if (result.OkPressed)
{
    // Match UnknownWords with the OCR casing, not the lowercased dictionary form.
    RefreshSpellCheckAfterDictionaryWordAdded(new[] { selectedWord.Word.Word });
}
```

Remove the bare `_ocrFixEngine.ReloadNames()` call (helper reloads).

Leave `AddUnknownWordToOcrPair` unchanged (still `ReloadNames()` only).

- [ ] **Step 4: Build to verify compile**

```powershell
dotnet build src/ui/UI.csproj -c Debug
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```powershell
git add src/ui/Features/Ocr/OcrViewModel.cs
git commit -m "Refresh OCR spell-check on lines matching words added to name/user dictionary."
```

---

### Task 3: In-OCR prompt call sites

**Files:**
- Modify: `src/ui/Features/Ocr/OcrViewModel.cs` — `PromptForUnknownWordsAsync` (~3589–3603)

**Interfaces:**
- Consumes: `RefreshSpellCheckAfterDictionaryWordAdded`, `_ocrFixEngine.AddName`, `UserWordsHelper.AddToUserDictionary`
- Produces: prompt paths that refresh other already-OCR’d lines after name / user-dictionary add

- [ ] **Step 1: Update `AddToNamesListPressed` branch**

Replace:

```csharp
if (result.AddToNamesListPressed)
{
    _ocrFixEngine.AddName(result.Word);
    continue;
}
```

with:

```csharp
if (result.AddToNamesListPressed)
{
    _ocrFixEngine.AddName(result.Word);
    // Refresh other already-OCR'd lines; current line is re-checked by GetNextUnknownWord.
    RefreshSpellCheckAfterDictionaryWordAdded(new[] { result.Word });
    continue;
}
```

Note: `AddName` already persists + updates in-memory lists; the helper’s `ReloadNames()` reloads from disk (includes the new name). That is intentional and matches the design.

- [ ] **Step 2: Update `AddToUserDictionaryPressed` branch**

Replace:

```csharp
if (result.AddToUserDictionaryPressed)
{
    if (SelectedDictionary != null)
    {
        UserWordsHelper.AddToUserDictionary(result.Word, SelectedDictionary.GetFiveLetterLanguageName() ?? "en_US");
    }

    _ocrFixEngine.ReloadNames();
    continue;
}
```

with:

```csharp
if (result.AddToUserDictionaryPressed)
{
    if (SelectedDictionary != null)
    {
        UserWordsHelper.AddToUserDictionary(result.Word, SelectedDictionary.GetFiveLetterLanguageName() ?? "en_US");
    }

    // Prefer unknownWord.Word.Word so Ordinal match finds the OCR casing in UnknownWords.
    RefreshSpellCheckAfterDictionaryWordAdded(new[] { unknownWord.Word.Word });
    continue;
}
```

- [ ] **Step 3: Build + re-run unit tests**

```powershell
dotnet test tests/UI/UITests.csproj --filter "FullyQualifiedName~OcrRefreshSpellCheckAfterDictionaryTests"
```

Expected: PASS.

- [ ] **Step 4: Manual verification checklist**

1. OCR several lines that share the same unknown name (prompt-for-unknown off or after a batch).
2. Select that word in the unknown-words list → **Add to names list** → confirm all matching unknown entries clear and red marks update without re-OCR.
3. Repeat with another word → **Add to user dictionary**.
4. With prompt-for-unknown on, when prompted add a name that appeared on earlier lines; confirm earlier lines update.
5. Confirm **Add to OCR replace list** still only reloads (no new refresh behavior).

- [ ] **Step 5: Commit**

```powershell
git add src/ui/Features/Ocr/OcrViewModel.cs
git commit -m "Refresh other OCR lines when adding names/user words during prompt."
```

---

## Spec coverage (self-review)

| Spec requirement | Task |
| --- | --- |
| Shared helper reloads names once, targeted line re-fix, rebuild UnknownWords | Task 2 |
| Sidebar name list (incl. multi) | Task 2 |
| Sidebar user dictionary (original casing for match) | Task 2 |
| Prompt AddToNamesList / AddToUserDictionary | Task 3 |
| OCR pair unchanged | Task 2 (explicit non-touch) |
| Guessing off | Task 2 |
| Ordinal match | Task 1 + Global Constraints |
| Unit-testable selection logic | Task 1 |
| Manual checklist | Task 3 |
