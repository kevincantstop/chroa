---
name: clip-capture
description: This skill should be used when working on video frame or clip extraction in Chroa Video Player — ClipCaptureManager, TaskRunner, the ChroaVideoPlayer.Headless child process, the hover preview on the seek bar, PNG frame sequences, mpv's vo=image, or when a preview is slow, blank, or shows the wrong frame. Covers the two-process contract, the verified mpv option recipe with its measured numbers, the cache design, and the known open items.
---

# Frame and clip capture

One feature, two processes. The player never decodes a second file itself: asking mpv for a frame of
*some other* file, at some time, would mean seeking the file that is currently playing. So the work is
handed to a child process that owns its own `MpvContext`.

```
ChroaVideoPlayer (UI)
  ClipCaptureManager          ← cache, concurrency, argument assembly, frame collection
    TaskRunner                ← generic: run an exe, collect stdout/stderr, timeout, kill tree
      ChroaVideoPlayer.Headless.exe   ← Program (args) → CaptureRequest → ClipCaptureJob (mpv)
        mpv vo=image            ← writes PNG frames itself; no rendering, no encoder code
```

| Piece | Where | Job |
| --- | --- | --- |
| `ClipCaptureOptions` | `Core/ClipCapture/` | headless path, fps (12), frame width (320), max concurrency (2), timeout (30 s) |
| `ClipCaptureResult` | `Core/ClipCapture/` | `IReadOnlyList<string> Frames` + `int FramesPerSecond` |
| `ClipCaptureProtocol` | `Core/ClipCapture/` | the inter-process contract: argument names, `frame-` prefix, `done` marker |
| `ClipCaptureManager` | `Core/ClipCapture/` | `CaptureAsync(path, captureAt, duration, token)` → `ClipCaptureResult?` |
| `TaskRunner` / `TaskResult` | `Core/ClipCapture/` | one exe, one result (`ExitCode` / `Output` / `Error` / `TimedOut`) |
| `Program` / `CaptureRequest` / `ClipCaptureJob` | `ChroaVideoPlayer.Headless/` | args → validation → mpv |
| `ClipCapture` | `ChroaVideoPlayer/Controls/` | the hover preview UserControl |

## 1. The API contract

```csharp
var manager = new ClipCaptureManager();

ClipCaptureResult? result = await manager.CaptureAsync(path, captureAt, duration);
```

- `captureAt` and `duration` are `TimeSpan`. `duration == TimeSpan.Zero` means **one frame**;
  anything larger means a clip of that length, sampled at `Options.FramesPerSecond`.
- **Failure returns `null`, it never throws.** Same style as `ChroaMpvContext.CaptureFrameAsync()` returning
  `byte[]?`: a missing preview must not disturb playback.
- A cancelled request also returns `null` (`OperationCanceledException` is caught inside).
- `Frames` is ordered, absolute, and belongs to the cache — do not delete or move those files.
- `FramesPerSecond` is 0 for a single frame, so a caller can tell the two modes apart without re-deriving it.

## 2. The verified mpv recipe

Driving `MpvContext` directly, with **no window and no render context**, works with the shipped
`libmpv-2.dll`. `vo=image` writes the PNGs itself, so there is no render API and no encoder to write.

```csharp
MpvApi.RootPath = Path.Combine(AppContext.BaseDirectory, "native");

Set(context, "vo", "image");
Set(context, "vo-image-outdir", request.OutputDirectory);
Set(context, "vo-image-format", "png");
Set(context, "audio", "no");
Set(context, "sub", "no");
Set(context, "keep-open", "no");
Set(context, "untimed", "yes");      // decode flat out instead of in real time
Set(context, "msg-level", "all=error");
Set(context, "start", Seconds(request.CaptureAt));

if (request.Duration > TimeSpan.Zero)
{
    Set(context, "length", Seconds(request.Duration));
    Set(context, "vf", $"fps={request.FramesPerSecond},scale={request.Width}:-2");
}
else
{
    Set(context, "frames", "1");
    Set(context, "vf", $"scale={request.Width}:-2");
}

context.LoadFile(request.Input).InvokeAsync().GetAwaiter().GetResult();
finished.Wait();                      // an EndFile subscriber sets this
```

Measured against a 13.4 s source, 320 px wide:

| Request | Result |
| --- | --- |
| whole file, `fps=12,scale=320:-2` | 161 frames |
| `start=2s` + `length=2s` + `fps=12` | **exactly 24 frames, 201 ms** |
| `start=2s` + `frames=1` + `scale=320:-2` | **exactly 1 frame, 205 ms** |

- **`untimed=yes` is what makes it usable.** Without it mpv paces against the clock and a 2 s clip takes
  2 s; with it the same clip takes 0.2 s.
- `start` / `length` / `frames` / `vf` are all accepted through `context.Option[name].Set(...)` **before**
  `LoadFile`. They are file-local options, so they must be set before the load, not after.
- **`start` past the end of the file is not an error**: mpv produces 0 or 1 frame and `EndFile` still fires.
  `CaptureAsync` therefore returns a *short* sequence rather than `null`. A caller that needs a strict answer
  compares `Frames.Count` against what it asked for.
- `scale=<width>:-2` keeps the width and derives an even height, so the frame's aspect follows the source.
  Never assume 16:9 — see §4.
- Set the options through a `try`/`catch` helper: a rejected option should print to stderr and let the run
  continue, not abort it.

## 3. The inter-process contract

```
ChroaVideoPlayer.Headless.exe --input <video> --at <seconds> --duration <seconds>
                              --fps <n> --width <px> --out <dir>
```

- exit `0` success, `1` capture failed, `2` bad arguments; failures print one line to stderr; stdout stays
  empty (reserved).
- The child **clears the output directory first** (stale `*.png` and the marker), decodes, renames mpv's
  `%08d.png` to `frame-0001.png …` so lexicographic order is playback order, and writes the completion
  marker `done` **last**.
- **The marker is the point.** `ClipCaptureManager.ReadFrames` treats a directory as cached only when
  `done` exists. Without it, a child killed mid-write leaves half the frames behind and every later call
  "hits the cache" and returns that truncated set forever.
- Every string in this contract lives in `ClipCaptureProtocol` — one source of truth for both sides.

## 4. The cache

`%TEMP%\ChroaVideoPlayer\previews\{hash}\`, where the hash covers
`path | captureAt | duration | effective fps | frame width | file length | file last-write ticks`.

- Same position hovered twice ⇒ **0 ms**, no process spawned (measured).
- Replacing the file changes its length or write time, so the key changes and a stale preview can never be
  served.
- Use the **effective** fps in the key (0 for a single frame), not the configured one, so changing the fps
  option only invalidates the previews that actually depend on it.
- `MaxConcurrency` (2) is a `SemaphoreSlim`; a hover sweeping the seek bar would otherwise start a dozen
  mpv processes at once.

## 5. The hover preview

`Controls/ClipCapture` is a `UserControl` that owns its own `ClipCaptureManager`:

- **Two-stage, by design.** `Show(path, captureAt)` requests `TimeSpan.Zero` immediately, so a still frame
  lands in about 0.3 s. A `_dwellTimer` of `DwellLength` (1 s) then upgrades the same position to a
  `PreviewLength` (1 s) clip. Any pointer movement calls `Show` again and restarts the dwell, so the moving
  flipbook only appears when the pointer actually rests.
- The flipbook is `List<Bitmap>` + a `DispatcherTimer` at `1 / FramesPerSecond`; the previous set is disposed
  when a new one arrives. Bitmaps are decoded up front, so switching frames costs nothing.
- Mounting: a `Canvas` (`IsHitTestVisible="False"`) added as the **last** child of the player's root `Grid`
  — document order is z-order, so last means above the control bar. **Not a `Popup`**: this repository has
  already been bitten by `ContextMenu` subclasses opening at 0×0 and by `IsOpen == true` while nothing is
  visible.
- Positioning uses `SeekBar.TranslatePoint(...)` plus `fraction × SeekBar.Bounds.Width`, never the raw
  pointer coordinate, so it stays correct if the bar moves or resizes. It reads `Preview.Width/Height`
  (properties, valid before layout) rather than `Bounds`, which is 0 until the control is measured.
- The popup's height follows the first frame's `PixelSize` (`320 × h / w` + `2 × ShadowMargin`) and the
  control raises `FrameReady` so the caller re-positions. A fixed 16:9 box would letterbox every source
  that is not 16:9 — those dark bands read as "a black line at the top and bottom".
- `DragBar` supplies `Hovered(double fraction)` / `HoverLeft`; hovering computes a fraction and **never**
  touches `_fraction` or the fill, because hovering must not move the progress bar.
- A shadow needs reserved margin inside the control — the `avaloniaui` skill, §11.

## 6. Project wiring

- `ChroaVideoPlayer.Headless` is a console `Exe` project referencing **Core only**; `Core` keeps its
  zero-Avalonia rule and the headless side never touches UI.
- The app references it with `ReferenceOutputAssembly="false" Private="false" IsHeadless="true"`, and a
  `CopyHeadless` target copies exactly four files — `.exe`, `.dll`, `.runtimeconfig.json`, `.deps.json` —
  from `..\ChroaVideoPlayer.Headless\$(OutputPath)` into `$(OutDir)`, plus a `CopyHeadlessToPublish` twin for
  `$(PublishDir)`. Using `$(OutputPath)` (rather than a hard-coded `bin\$(Configuration)\$(TargetFramework)`)
  keeps it correct under `-p:BaseOutputPath=.verify-out\`.
- Everything shared is used from the app's own folder: `ChroaVideoPlayer.Core.dll`, LibMpv, Avalonia, and
  `native\libmpv-2.dll` (the child resolves the latter from `AppContext.BaseDirectory\native`).
- `ClipCaptureManager.DefaultHeadlessPath` looks next to `AppContext.BaseDirectory`, so a probe that
  references `Core` must set `Options.HeadlessPath` explicitly — its base directory is the probe's.

## 7. Open items

- **The preview cache is never cleaned up.** Every new hover position writes ~1 MB (24 frames ≈ 1.9 MB) and
  nothing evicts it. Suggested directions: trim by total size or age, clear on exit, or keep the newest N.
- **PNG is heavy for previews.** A 320×180 photographic frame is ~83 KB, so a 24-frame clip is ~1.9 MB of
  disk and ~2.7 MB of decoded bitmap. `vo-image-format=jpg` would cut that by roughly six times for about
  one line of change; the format is not yet configurable.
- Dragging the seek bar hides the preview (`OnBarDragStarted` → `HidePreview`). Showing it during a drag is
  possible but competes with the seek itself for the two-process budget.
- `ChroaVideoPlayer.Headless` may later host plugins without the GUI. It is deliberately single-purpose
  today; adding commands means a `switch` in `Program.Main`, which is currently only argument parsing.
