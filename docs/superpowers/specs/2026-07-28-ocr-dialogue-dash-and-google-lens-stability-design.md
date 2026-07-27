# OCR Dialogue-Dash Fixer & Google Lens Stability — Design

## Background

Two independent problems reported against the OCR subsystem (Avalonia-based rewrite,
`src\ui\Features\Ocr\`, `src\ui\Logic\Ocr\`):

1. **Dialogue-dash multiline errors.** OCR (both standalone Google Lens Sharp and the SE
   integration) frequently mis-splits two-speaker dialogue lines that use a leading `-`
   marker. Symptoms seen in real output:
   - A line is only `-` on its own (an "orphan" dash), separated from the text it
     belongs to.
   - One line of a two-line dialogue entry has a leading `-` and the sibling line
     doesn't, even though subtitle convention requires: if any line in the entry uses
     dash-dialogue formatting, every line in that entry must have the dash.
   - Today, Subtitle Edit's grid only red-highlights entries with more than
     `MaxNumberOfLines` (default 2) lines (`SubtitleLineViewModel.HasTextError`), so a
     2-line entry with an asymmetric/missing dash is invisible in the grid — the user
     has to spot it by reading text. Google Lens standalone's UI puts each dash on its
     own row, which paradoxically makes the error easier to spot there, even though it
     mis-splits more often.

2. **Google Lens engine stability on long batch runs.** SubtitleEdit has two independent
   "Google Lens" OCR engines (`OcrEngineType.GoogleLens` and `.GoogleLensSharp`). Both
   stall on long unattended `.srt` runs, but for different root causes:
   - `GoogleLensOcrSharp` (`src\ui\Features\Ocr\Engines\GoogleLensOcrSharp.cs`): a plain
     sequential `foreach` over bitmaps, `await`-ing `Lens.ScanByBitmap` one at a time,
     with **no per-item try/catch**. Any single transient failure (network blip, HTTP
     error, rate limiting) throws out of the loop entirely; the exception is only
     caught at the `Task.Run` boundary in `OcrViewModel.RunGoogleLensOcrSharp`, which
     just logs and aborts — the rest of the batch is silently never processed. This
     matches the "randomly stops, must press Start again" symptom.
   - `GoogleLensOcr` (`src\ui\Features\Ocr\Engines\GoogleLensOcr.cs`): shells out to a
     bundled `chrome-lens.exe` for the *entire batch* in one process, and calls
     `process.WaitForExit()` with **no timeout**. Cancellation is only observed inside
     the `OutputDataReceived` handler, so if the external process hangs or stops
     producing output (e.g. stuck internal retry loop, rate-limited), nothing ever
     unblocks it. This matches the "stuck at 200/400 forever" symptom.

## Goals

- Detect and (optionally, with review) fix dialogue-dash irregularities in subtitle
  text, independent of which OCR engine (or import path) produced the text.
- Surface these irregularities in the grid distinctly from the existing
  too-many-lines check, so 2-line entries with dash problems are visible.
- Make both Google Lens engines resilient to transient failures/stalls on long batch
  runs, without silently truncating or hanging.

## Non-goals

- Detecting *missing* dash-dialogue formatting when **no** line in the entry has a
  dash at all (i.e. no evidence a `-` was ever present). Plain multi-line OCR
  reflow/line-count issues with no dash evidence are already handled by
  `Fix3PlusLines`/`Utilities.AutoBreakLine` and are out of scope here.
- Using OCR bounding-box geometry (as `LensCore.OrderSegmentsIntoReadingLines` does)
  to increase detection confidence. Text-only heuristics are sufficient for the
  reported cases and work across all OCR engines; geometry-based confirmation is
  deferred as a possible future enhancement.
- Making retry/backoff/stall-timeout values user-configurable. Fixed constants are
  sufficient to fix the reported hangs; can be revisited if real-world tuning is
  needed.

## Design

### 1. `DialogueDashFixer` (core detection/fix algorithm)

New standalone class: `src\libse\Forms\FixCommonErrors\DialogueDashFixer.cs`. Pure text
in, text out — no OCR or UI dependency, fully unit-testable. Grouped with the existing
`Fix3PlusLines` since it's a general subtitle-text correctness fix (also reusable from
Fix Common Errors / `seconv` later), not an OCR-only concern.

```csharp
public static class DialogueDashFixer
{
    public static DialogueDashFixResult Analyze(string text);
}

public class DialogueDashFixResult
{
    public bool Changed { get; }
    public string FixedText { get; }
}
```

Algorithm, operating on the paragraph's lines:

1. Split `text` into raw lines.
2. Classify each line:
   - `DashOnly`: trimmed line is exactly `-`.
   - `DashText`: trimmed line starts with `-` followed by whitespace and content.
   - `Plain`: anything else.
3. Resolve orphan `DashOnly` lines, in order:
   - If the following line is `Plain`, merge: prefix `"- "` onto it, remove the
     orphan line.
   - Else if the preceding line is `Plain` (orphan is the last line, or the following
     line isn't `Plain`), merge backward instead.
   - Else (both neighbors, or the only neighbor, are already `DashText`/`DashOnly`):
     drop the orphan line. This is safe because the remaining lines already all
     carry dashes — dropping cannot create a state that violates the invariant
     below.
4. Invariant enforcement: after step 3, if **any** remaining line is `DashText` and
   **any** remaining line is `Plain`, prefix `"- "` onto every `Plain` line. This is
   deliberately general — not limited to "exactly one line missing" — since the rule
   is "any dash present in the entry ⇒ every line has a dash."
5. Rejoin lines. `Changed` is true if the result differs from the input.

This directly resolves every example pattern reported (orphan leading/trailing dash,
orphan dash sandwiched between text lines, asymmetric single missing dash, multiple
orphan dashes alternating with text).

### 2. Tools menu dialog

New dialog under `src\ui\Features\Tools\FixDialogueDashes\` (`FixDialogueDashesWindow`
+ `FixDialogueDashesViewModel`), following the existing `MergeContinuationLines`
template:

- On open, runs `DialogueDashFixer.Analyze` over every subtitle entry, lists only
  entries where `Changed == true`, showing before/after text per row.
- Each row has a checkbox (default checked); Apply applies the fix to checked rows
  only, following the same `Initialize`/`Ok`/`Cancel` pattern as other tools.
- Menu entry added in `InitMenu.cs` under Tools; command
  `ShowToolsFixDialogueDashesCommand` in `MainViewModel.cs`; language strings under
  `Se.Language.Tools.*` (added to the base English JSON; other locales fall back to
  English until translated).

### 3. Grid highlighting

`SubtitleLineViewModel` gets a second, independent error check alongside
`HasTextError` (too-many-lines):

- `HasDialogueDashError`: true when `DialogueDashFixer.Analyze(Text).Changed` is true.
- New setting `Se.Settings.General.ColorTextDialogueDashError` (default `true`),
  alongside the existing `ColorTextTooManyLines`.
- Rendered with a distinct highlight color from the too-many-lines red, so the two
  error classes are visually distinguishable in the grid. Both can independently
  apply to the same row (an entry can be both >2 lines and dash-broken).
- `GetErrors(prev, next)` gets a corresponding status-bar/tooltip message (e.g.
  "Dialogue dash mismatch") when this fires.

### 4. OCR pipeline integration

- New checkbox in the OCR dialog options: "Auto-fix dialogue dashes after OCR"
  (default on), stored alongside existing OCR-related settings.
- When enabled, `DialogueDashFixer.Analyze`/fix runs per item in the same place
  `DoAutoBreak`/`OcrFixLineAndSetText` already post-process OCR text in
  `OcrViewModel.cs`, before results are displayed to the user.

### 5. Google Lens stability fixes

**`GoogleLensOcrSharp.OcrBatch`** (`src\ui\Features\Ocr\Engines\GoogleLensOcrSharp.cs`):
wrap each `await _lens.ScanByBitmap(...)` call in try/catch inside the existing
`foreach`. On exception: retry up to 3 times with short backoff (1s, 2s, 4s); if still
failing, record that item as failed/empty (so progress/output length stays consistent
with the input batch) and continue to the next item rather than letting the exception
escape the loop. The batch will now always run to completion, with only truly
unresolvable individual items coming back empty instead of the whole run dying
silently partway through.

**`GoogleLensOcr.OcrBatch`** (`src\ui\Features\Ocr\Engines\GoogleLensOcr.cs`): replace
the blocking `process.WaitForExit()` (line ~188) with a polling loop:
`while (!process.WaitForExit(500)) { check cancellationToken; check stall watchdog; }`.
The watchdog tracks the timestamp of the last stdout/stderr line received (already
wired via `OutputDataReceived`); if `now - lastOutputTime` exceeds a fixed threshold
(90 seconds) while the process is still running, treat it as stalled: kill the
process tree and surface a clear error/partial result instead of hanging
indefinitely. Cancellation-token checks move into this loop too, so cancellation is
observed even if the process has stopped producing output entirely (previously only
checked inside the output-received handler).

## Testing

- Unit tests for `DialogueDashFixer` directly against the example strings from the
  bug report (orphan leading dash, orphan trailing dash, orphan sandwiched between
  dashed lines, asymmetric single missing dash, multiple alternating orphan dashes,
  plain 3-line entry with no dash evidence — verifying it's left alone).
- Unit/integration tests for the Tools dialog's filtering (only changed entries
  listed) and checkbox-scoped apply.
- Tests for `HasDialogueDashError` grid flagging, independent of `HasTextError`.
- For the stability fixes: tests around the retry/continue behavior for
  `GoogleLensOcrSharp.OcrBatch` (simulate a failing item, assert the batch completes
  and later items are still processed), and around the watchdog/cancellation loop for
  `GoogleLensOcr.OcrBatch` (simulate no output for the threshold duration, assert the
  process is killed and the call returns rather than hanging).
