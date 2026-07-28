# OCR Refresh Spell-Check After Name / User Dictionary Add — Design

## Background

In the Avalonia OCR window (`src/ui/Features/Ocr/OcrViewModel.cs`), unknown words are
tracked in `UnknownWords` and highlighted via each line’s `FixResult` from
`IOcrFixEngine.FixOcrErrors`.

When the user adds an unknown word to the **names list** or **user dictionary**:

- Sidebar: `AddUnknownWordToNames` / `AddUnknownWordToUserDictionary` only call
  `_ocrFixEngine.ReloadNames()` after a successful dialog.
- In-OCR prompt: `AddToNamesListPressed` calls `AddName`, and
  `AddToUserDictionaryPressed` adds the user word then `ReloadNames`. The prompt loop
  re-fixes the *current* line via `GetNextUnknownWord`, but other already-OCR’d lines
  that contain the same word stay marked unknown.

Result: the user must re-OCR those lines for the word to be recognized as known.

A full re-fix of every OCR’d line already exists for Thai word-break changes
(`RefreshOcrAfterThaiWordBreakChange`), which rebuilds `UnknownWords` with
`doTryToGuessUnknownWords: false`.

## Goals

- After a successful add to **names list** or **user dictionary**, re-spell-check
  already OCR’d lines that currently list that word as unknown.
- Update `FixResult` coloring and the `UnknownWords` list without re-running OCR.
- Cover both sidebar buttons and the in-OCR unknown-word prompt.
- Support multi-name import from the names-list dialog.

## Non-goals

- OCR replace / OCR pair list (`AddUnknownWordToOcrPair`) — out of scope.
- Re-OCR of images.
- Full re-spell-check of every OCR’d line on every add (too heavy for large files).
- Changing guess / auto-fix behavior for unrelated words.

## Design

### Shared helper

Add a private helper on `OcrViewModel`, e.g.
`RefreshSpellCheckAfterDictionaryWordAdded(IEnumerable<string> words)`:

1. No-op if the OCR fix engine is not loaded, no dictionary is selected, dictionary is
   “None”, or `words` is empty after trim.
2. Call `_ocrFixEngine.ReloadNames()` once (helper owns reload so call sites stay
   consistent).
3. For each distinct added word (trimmed, non-empty), find `UnknownWords` entries where
   `Word.Word` equals the added word with `StringComparison.Ordinal` (same as
   `UnknownWordsRemoveCurrent`).
4. Collect the distinct `OcrSubtitleItem` lines from those entries.
5. For each affected line:
   - Run `_ocrFixEngine.FixOcrErrors(lineIndex, item.Text, doTryToGuessUnknownWords: false)`.
   - Assign `item.FixResult = result`.
   - Remove existing `UnknownWords` entries for that item.
   - Re-add `GetUnknownWordItems(item, result)`.
6. Must run on the UI thread (sidebar commands already do; prompt path already uses
   `Dispatcher.UIThread.Post`).

### Call sites

| Location | When |
| --- | --- |
| `AddUnknownWordToNames` | After `OkPressed`; pass the name(s) from the dialog (single or multi). |
| `AddUnknownWordToUserDictionary` | After `OkPressed`; pass the added word. |
| Prompt `AddToNamesListPressed` | After `_ocrFixEngine.AddName(...)`; also refresh other lines that listed that word. Current line continues via the existing prompt loop. |
| Prompt `AddToUserDictionaryPressed` | After user-dictionary add; call the helper (which reloads names) instead of only `ReloadNames`. |

`AddUnknownWordToOcrPair` is unchanged.

### Multi-name import

`AddToNamesListViewModel` may add several names in multi mode. The helper should accept
multiple words and refresh lines matching any of them (one `ReloadNames`, one pass over
affected lines — union of matching unknown entries).

After `OkPressed`, the caller reads the intended word(s) from the dialog VM:

- Single mode: `result.Name`
- Multi mode: non-empty trimmed lines from `result.MultiNames`

Matching against `UnknownWords` only affects lines that had those strings as unknowns.
Words that failed to add (already present) still refresh harmlessly if they were already
known, or no-op if they never appeared as unknowns.

### Edge cases

| Case | Behavior |
| --- | --- |
| Word not present in `UnknownWords` | Reload names only; no line updates. |
| OCR still running | Safe: only lines already in `UnknownWords` are touched. |
| Line text may change | Only via fix engine with guessing off (same posture as Thai refresh). |
| Empty / whitespace word | Ignored. |

### Testing

- Unit or UI-level coverage is optional if awkward; preferred: a focused test or manual
  checklist:
  1. OCR several lines containing the same unknown name.
  2. Add that word to names list from the unknown-words panel.
  3. Confirm all prior unknown entries for that word disappear and red spelling marks
     clear on those lines without re-OCR.
  4. Repeat for user dictionary.
  5. During prompted OCR, add a name and confirm earlier lines that listed it update.

## Implementation notes

- Prefer reusing `GetUnknownWordItems` and the Thai refresh posture
  (`doTryToGuessUnknownWords: false`) rather than inventing a parallel spell-check path.
- Keep the helper private to `OcrViewModel` unless a second consumer appears.
- Avoid changing `IOcrFixEngine` unless needed; `ReloadNames` + `FixOcrErrors` are enough.
