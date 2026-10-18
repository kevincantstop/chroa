---
name: playback
description: This skill should be used when playback misbehaves in Chroa Video Player — a video loads but does not play, the window does not size to the video, the playlist never records what was played, autoplay advances wrongly, or an mpv event seems not to arrive. Covers the mpv event order and what each end-file reason means, the fact that pause survives across file loads, and why one throwing subscriber can silently starve every later one.
---

# Playback: the mpv boundary and the event chains on top of it

Everything mpv-related converges on `ChroaVideoPlayer.Core/Mpv/ChroaMpvContext.cs` (partial class). The
control layer (`Controls/MpvVideoPlayer.axaml.cs`) publishes player events to the bus, the view model
mirrors state, and `Settings/VideoPlayerSettingsBinder` restores per-video state. All four listen to the
*same* `ChroaMpvContext` events, which is where the traps live.

## 1. mpv state survives across files

`pause` is a property of the mpv instance, **not of the file**. Once playback is paused, the next
`loadfile` starts **paused** as well. Measured: after a single pause, every subsequent load logged
`start-file pause=True`, so loading from the playlist, autoplay-next and the file picker all appeared to do
nothing — the file had loaded, it simply never started.

The fix is explicit, at the point where a file finishes loading:

```csharp
private void EnsurePlaying()
{
    var context = _context;
    if (context is null) return;

    SafeRun(nameof(EnsurePlaying), () =>
    {
        if (context.Pause.Get() == true) context.Pause.Set(false);
    });
}
```

`Play(path)` means "play this file", so a fresh load resets the transient pause state. A pause the user
makes afterwards still holds — only a new load clears it. Anything else keyed on `pause` (a "resume where
I left off" feature, say) would need its own explicit handling instead.

## 2. The event order, and what `end-file` really means

Per file, mpv emits: `StartFile` → (`FileLoaded`) → … → `EndFile`. `ChroaMpvContext` maps those to
`MediaOpened` / `MediaEnded` / `EndedChanged`, and `EndFile` fires **for reasons other than reaching the
end**:

| `MpvEndFileReason` | Meaning |
| --- | --- |
| `EndOfFile` | the file actually played to the end |
| `Stop` | stopped — **this is what a new `loadfile` produces for the *previous* file** |
| `Redirect`, `Quit`, `Error` | replacement / shutdown / failure |

So:

- `SetEnded(true)` on every `EndFile` is fine for UI (the play button should show ▶), because `StartFile`
  immediately sets it back to `false`.
- Anything that *acts* on "the video finished" — autoplay-next — must filter:
  `if (e.Reason == MpvEndFileReason.EndOfFile)`. Without the filter, manually opening another video reads
  as "this one finished" and the queue advances on its own.
- Expect `HasEnded` to flicker true→false on every load. Never drive a side effect off the transition
  alone without also checking the reason.

## 3. One throwing subscriber starves all the others

`MediaOpened` is an ordinary .NET multicast event. If the **first** subscriber throws, the exception
propagates out of `MediaOpened?.Invoke(...)`, so:

1. **every later subscriber never runs** — the view model's `HasVideo`, then `MpvVideoPlayer`, then
   `VideoPlayerSettingsBinder` (subscribed last, in `MainWindow.OnOpened`);
2. **the rest of the publisher's method is skipped** — including `EnsurePlaying()` when it sat after the
   `Invoke`.

Measured symptom set from a single such abort: the video loaded but never started, the per-video window
size was never restored, and the playlist never recorded the video (its `VideoPlayed` publish sits behind
`_pendingPlayedPath`, which is assigned in the aborted handler).

Two habits prevent the whole class:

- **Assign state first, do side effects last.** `OnMediaOpened` now sets `_openedPath` /
  `_pendingPlayedPath` *before* the cache cleanup, so even a failing cleanup cannot lose the publish.
- **Start playback before announcing it.** `OnFileLoaded` calls `EnsurePlaying()` before
  `MediaOpened?.Invoke(...)`, so a bad subscriber cannot leave a loaded file paused.
- Wrap best-effort work in its own `try/catch` (`ClearPreviewCache` does) rather than trusting it to be
  side-effect free.

## 4. A guarded early return can disable a feature permanently

```csharp
if (_settings.HasSettingsFor(path)) return;   // ← disabled the aspect fit for every saved video
```

The intent was "restore the saved size instead of fitting". The effect: any video with a row in
`chroa.db` skipped the aspect fit **forever**, so a saved size that did not match the video's aspect (or a
window that had not been fitted yet) left letterbox bars with no way to recover — dragging the window by
hand fixed it, which made it look like a resize bug.

Ask, of every short-circuit: *is this condition true for inputs where the skipped work is still needed?*
Here the right rule was the user's actual intent — respect free resize, otherwise always fit:

```csharp
if (VideoPlayer.ViewModel.IsFreeResize) return;

WindowUtils.FitToAspect(this, aspect, MaxWorkingAreaFraction, WindowUtils.PanelShadowMargin);
```

`FitToAspect` keeps the current window **width** and derives the height from the video aspect, so a saved
width is still honoured; only the height is corrected.

## 5. Aspect reporting timing

- `VideoAspectDetected` comes from `TryReportAspect()`, which needs `DisplayWidth/Height` to be readable.
  When `_sizeTrustworthy` is false it retries for ~2 seconds, so the aspect can arrive **after**
  `MediaOpened` — never assume it is available at load time.
- The aspect is consumed by `WindowUtils.FitToAspect` (window sizing) and by `WindowResizer.Aspect`
  (interactive resizing). Both read `VideoPlayerViewModel.VideoAspect`, which the view model updates via
  `Dispatcher.UIThread.Post`, so it is one frame behind at worst.
- A useful field check: if dragging the window edge produces a correctly proportioned window, the aspect
  is valid — the problem is downstream of it.

## 6. Diagnosing playback problems

Deduction failed repeatedly here; measurement settled it in one run. The recipe:

1. Write a few `File.AppendAllText(path, ...)` lines to `%TEMP%\chroa-diag.log` at the decision points —
   the mpv callbacks (`start-file`, `file-loaded`, `pause-changed`, `end-file reason=…`) and the
   `PropertyChanged` / fit code. Log **values** (booleans, sizes, reason enums, path *lengths*), never file
   paths.
2. Have the user reproduce once, then read the file. The log above pinned both remaining bugs in 30
   seconds after six rounds of guessing.
3. Delete the instrumentation and the helper in the same change that ships the fix.

And before trusting any observation, confirm the process is the build you just made — see the `rules`
skill, §6.

## 7. Playback progress: reporting it, storing it, resuming from it

`ChroaMpvContext` already throttles `ProgressChanged` to 200 ms; anything that only needs a coarse position
throws most of those away. The playlist's progress record does:

- `MpvVideoPlayer` re-throttles to **1 s** with a `Stopwatch` and publishes `VideoProgress(path, position,
  duration)`; the playlist service updates the entry in memory every tick but writes to SQLite at most every
  **5 s** (plus once on `VideoEnded`). 1 write/second for hours is wasteful; 5 s of loss is invisible.
- **`end-file` does not land at `position == duration`.** mpv reports a duration a few hundred ms longer
  than the last decodable frame, so a 31 s clip finished at ~31.0 of 31.6 s and displayed as **98 %** even
  though `HandleVideoEnded` had written the full duration. Do not chase exactness: treat
  `duration - position <= 1 s` as complete, and let the percentage, the green "100 %" style and the
  "restart from 0" decision all read that one helper.
- Resuming is a `seek`, applied **once** from `file-loaded` (`EnsurePlaying()` → `SeekTo(start)` →
  `MediaOpened`), not an mpv `start` option: `ChroaMpvContext.Play(path, start)` parks the value in
  `_startSeconds`, `OnFileLoaded` consumes and clears it. Clearing matters — `TogglePause` restarts an ended
  file through `Play(_currentFile)`, and a still-set start would jump back on every replay.
- The resume point and the "is it finished" test must share the same tolerance, otherwise a finished video
  restarts mid-tail or a half-watched one jumps to the end.

