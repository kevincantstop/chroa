---
name: plugin-development
description: This skill should be used when writing, reviewing or debugging a Chroa Video Player plugin — scaffolding a plugin project, implementing IChroaPlugin, listening to player events, driving playback through the application context, building a PluginWindow, or wiring the plugin into the solution and build. Covers the assembly-scanning contract, the window and magnetic-docking API, the interactive-content rule, icon resource URIs, UI wording rules, and the known deployment traps.
---

# Writing a plugin for Chroa Video Player

A plugin is a class library that the player discovers at startup by scanning the `Plugins` folder next to
`ChroaVideoPlayer.exe` (plus its own assembly). Every enabled plugin gets one entry in the home menu, and
clicking that entry opens the plugin's window.

Two working examples ship with the repository:

| Project | Enabled | Why it exists |
| --- | --- | --- |
| `ChroaVideoPlayer.Plugins.Playlist` | yes | A real plugin: records every played video in a table |
| `ChroaVideoPlayer.Plugins.Sample` | no | Kept switched off, to exercise assembly scanning |

## 1. Project setup

A plugin is a `net10.0` class library — the same target framework as the player — referencing
`ChroaVideoPlayer.Core` and `Avalonia`:

```xml
<ItemGroup>
  <ProjectReference Include="..\ChroaVideoPlayer.Core\ChroaVideoPlayer.Core.csproj" />
</ItemGroup>

<ItemGroup>
  <PackageReference Include="Avalonia" Version="12.1.4" />
</ItemGroup>

<ItemGroup>
  <AvaloniaResource Include="Icons\**" />
</ItemGroup>
```

Add it to `ChroaVideoPlayer.sln`, then register it in `ChroaVideoPlayer/ChroaVideoPlayer.csproj` so the
build deploys it next to the player:

```xml
<ProjectReference Include="..\MyPlugin\MyPlugin.csproj"
                  ReferenceOutputAssembly="false" Private="false" IsPlugin="true" />
```

`IsPlugin="true"` is what the `CopyPlugins` target selects on, so the project path only has to be written
once. There is no registration code to write anywhere.

## 2. The contract

```csharp
public interface IChroaPlugin
{
    string Name { get; }                           // stable id, developer facing ("playlist")
    string Label { get; }                          // the text in the menu, user facing ("Playlist")
    string Icon { get; }                           // avares:// URI of an SVG resource
    PluginLocation Location { get; }               // entry point; MainMenu is the only one today
    bool Enabled { get; }                          // false = discovered but never registered
    ChroaApplicationContext? Context { get; set; } // assigned by the player
    PluginWindow CreateWindow();                   // called every time the user opens the plugin
}
```

`Enabled = false` still **scans** the plugin (so assembly discovery stays verifiable) but never registers
it: no menu entry, and `SubscribeEvents` is not called. It is completely inert.

`Context` is filled in by the player before the plugin is registered and before `SubscribeEvents` runs, so it
is `null` until then — never demand it from a constructor. Through it a plugin drives the player:

```csharp
public ChroaApplicationContext? Context { get; set; }

Context?.Mpv.Play(path);        // the same ChroaMpvContext the player itself plays through
Context?.Mpv.TogglePause();
Context?.Mpv.SeekTo(seconds);
Context?.Mpv.SeekBy(seconds);
```

The `ChroaMpvContext` inside is the player's own instance and lives for the whole session — hold on to it
freely, and start playback by calling `Play` even when no video has been opened yet. The mpv context *behind*
it is replaced when the player's video surface is created; a plugin never sees that swap.

## 3. Player events

The player broadcasts events to plugins; plugins never publish. To listen, implement `IChroaPluginEvents`:

```csharp
public sealed class MyPlugin : IChroaPlugin, IChroaPluginEvents
{
    public void SubscribeEvents(IEventSubscriber events) =>
        events.Subscribe<VideoPlayed>(message => { /* message.Path, message.Duration */ });
}
```

- `VideoPlayed(string Path, TimeSpan Duration)` is raised once a video has actually opened — the player
  waits for the first progress tick so the duration is known.
- **Handlers always run on the UI thread**, so a handler may touch its window directly. The bus marshals
  for you (`UIEventBus`), which matters because the player's playback events originate on mpv's thread.
- `Subscribe` returns an `IDisposable` — keep it if the subscription needs to end later.
- The bus is one-way (player → plugin). To *control* playback a plugin does not use the bus at all — it calls
  `Context.Mpv` (see §2).

## 4. The window

Derive from `PluginWindow`, never from a bare `Window`. The panel chrome (black, rounded, soft shadow,
per-pixel transparent frame), dragging with magnetic docking against the player, and resizing all come
with it.

`PluginWindow` extends `ChroaWindow` (`ChroaVideoPlayer.Core.UI`), the same base the player's own window
uses, so the two look alike by construction rather than by copied constants. That has three consequences
worth knowing:

- **The chrome spec lives in `ChroaWindow`** — corner radius, the `0 6 24 0 #A0000000` shadow and the panel
  margin. `WindowUtils.PanelShadowMargin` is the single source of the margin value, shared with the
  docking maths and the player's aspect fitting. Changing it there changes every window at once; do not
  reintroduce a second copy.
- **Put content in `PluginContent`** (the supported surface) rather than reaching for the panel. Setting
  `Content` still works — `PluginWindow` relocates it into the panel — but `PluginContent` says what you
  mean.
- **The transparent ring around the panel belongs to nobody.** Docking targets the host's *panel* rect
  (`WindowUtils.GetPanelBounds`), not its window rect, which is why a docked window sits flush against the
  visible player instead of 24 px away.


```xml
<chroa:PluginWindow xmlns:chroa="clr-namespace:ChroaVideoPlayer.Core.Plugin.UI;assembly=ChroaVideoPlayer.Core"
                    x:Class="MyPlugin.MyPluginWindow"
                    Title="My plugin"
                    Width="280"
                    Height="320"
                    InitialDockSide="Left">
  <StackPanel Margin="12"> ... </StackPanel>
</chroa:PluginWindow>
```

Rules and knobs:

- **Keep a public parameterless constructor.** A window whose only constructor takes arguments triggers
  `AVLN3001` and its XAML becomes unreachable through the runtime loader.
- Write the UI as the root's child in XAML — the player redirects `Content` into its own panel. In code,
  assign `PluginContent` instead.
- `InitialDockSide` / `InitialDockOffset` open the window already docked to the player.
- `IsResizable` (default `true`), `SnapDistance` (12) and `DetachDistance` (24) tune the behaviour.
- `Host` is assigned by the player — do not set it. `IsAttached` and `AttachedSide` report where the window
  currently sits.
- A docked window follows the player when it moves or resizes, and hides while the player is maximized or
  full-screen.
- **Mark anything that handles pointer input of its own with `Classes="interactive"`** — a clickable list row,
  a canvas, a drag handle that is not a `Button`. Pressing anywhere else in the panel starts a *window drag*,
  and that drag captures the pointer, so your element never receives the release: clicks and double clicks
  aimed at it simply never arrive. `Button` is exempt already; `interactive` is how a non-button opts out.
- `CheckBox` derives from `Button`, so checkboxes inside a window are exempt from that drag already — and a
  double click on one never reaches the row underneath it. Stock Fluent checkboxes are drawn for a light
  surface, though: on the black panel the unchecked border (`#99000000`) is nearly invisible and the checked
  fill is the blue accent, so restyle the template parts `Border#NormalRectangle` and `Path#CheckGlyph`
  (and set `MinHeight="0"`, since the default 32 px box otherwise dictates the row height — fill the box for
  `:indeterminate` too, or its glyph lands dark-on-black).
- A plugin window opens its own file picker through the player's storage provider —
  `TopLevel.GetTopLevel(this)?.StorageProvider` — and passes `FileTypeFilter = VideoFileTypes.Filter` from
  `ChroaVideoPlayer.Core.Utils` so the dialog offers exactly the types the player's own dialogs offer
  (`VideoFileTypes.Filter` for videos, `SubtitleFileTypes.Filter` for subtitles). Hand the
  chosen paths to the plugin over an event instead of acting on them inside the window.

## 5. Visual conventions

A plugin window sits next to the player, so it has to look like it belongs there. These are the rules the
playlist window was tuned to; follow them instead of inventing a look.

**The title bar holds the title and the close button — nothing else.** Actions, counters and filters belong on
a row of their own between the title and the content, right-aligned, so the close button never moves around.

| Element | Spec |
| --- | --- |
| Window title | `FontSize="16"`, `Foreground="White"` |
| Body text — list cells, table headers, counters, button labels | `FontSize="14"` |
| Icon buttons in the content (per-row actions, a contextual delete) | `ButtonSize="28"`, `IconSize="14"` |
| The close button in the title bar | `ButtonSize="32"`, `IconSize="16"` |
| List rows | `Height="32"` |

One rule behind those numbers: **the icon takes about half the button's side and the rest is padding**. That is
how the player sizes its own buttons — the control bar is 40/20 (`ButtonSize="40" IconSize="20"`, 48/24 for
play/pause) and the window chrome is 48/24 — and `SVGIconButton` turns any of those into a circle with
`CornerRadius = min(width, height) / 2`. Its state colours are the ones to copy:

| State | Neutral | Destructive |
| --- | --- | --- |
| Background | `Transparent` | `Transparent` |
| Hover | `#40FFFFFF` | `#33FF6B6B` |
| Pressed | `#66FFFFFF` | `#55FF6B6B` |
| Icon | `#E0E0E0` | `#FF6B6B` |

- **Build icon buttons from `core:SVGIconButton`, never a bare `Button` with a `Path` inside.** It owns the
  size, the circle, the hand cursor, the icon-only hit area and a centred icon — the icon goes through
  `SvgIcon`, whose `Viewbox` scales and centres it, while a hand-rolled `Path` with `Stretch` lands off-centre.
  The player's app-wide style then supplies the hover, pressed and disabled colours for free:

  ```xml
  <core:SVGIconButton Icon="avares://MyPlugin/Icons/folder.svg"
                      ButtonSize="28"
                      IconSize="14"
                      VerticalAlignment="Center"
                      Click="OnRevealClick"
                      ToolTip.Tip="Show in folder" />
  ```

- **Pass `VerticalAlignment="Center"` yourself.** That app-wide style pins the button to `Top` for the player's
  title bar, so a plugin window without an explicit value gets its buttons pushed to the top of the row.
- **An icon-only button must carry a `ToolTip.Tip`** — that tooltip is its label.
- A destructive button keeps its colour by overriding both states for its own class — one neutral rule matches
  its buttons too, and they would otherwise flash white on hover:

  ```xml
  <Style Selector="core|SVGIconButton.remove:pointerover /template/ ContentPresenter">
      <Setter Property="Background" Value="#33FF6B6B" />
  </Style>
  <Style Selector="core|SVGIconButton.remove:pressed /template/ ContentPresenter">
      <Setter Property="Background" Value="#55FF6B6B" />
  </Style>
  ```
- **Size a column that holds icon buttons to fit them**: two 28 px circles plus a 6 px gap need 68 px, and the
  row that carries them has to be a few pixels taller than the buttons.
- Colour is monochrome by design: white and black at various alphas (`#XXFFFFFF`, `#XX000000`) plus `#E0E0E0`
  and `#B0FFFFFF` for text. The destructive red above is the only exception — never introduce an accent
  colour, and restyle any stock Fluent control you use, because the theme brings its blue accent with it
  (see the checkbox note in §4).
- Leave room between the title and the content below: the playlist window puts `Margin="0,12,0,10"` on the row
  between them, so the title never sits on top of the content.
- Keep ~16 px between a contextual action and a right-aligned counter, and give such a counter a *left* margin
  only, so its right edge stays flush with the close button.
- **A right-click menu is a Fluent `ContextMenu` plus `MenuItem`s, and it is light by default** — a white popup
  with black labels. Restyle it or it will not belong on the black panel:

  ```xml
  <Style Selector="ContextMenu">
      <Setter Property="Background" Value="#000000" />
      <Setter Property="Padding" Value="0" />
  </Style>
  <Style Selector="MenuItem">
      <Setter Property="Foreground" Value="#E0E0E0" />
  </Style>
  <Style Selector="MenuItem:pointerover /template/ Border#PART_LayoutRoot">
      <Setter Property="Background" Value="#26FFFFFF" />
  </Style>
  ```

  Attach **one** menu to the list rather than one per row, and resolve the clicked item from the row's
  `ContextRequested` handler (it bubbles through the row *before* the menu opens) instead of relying on the
  menu inheriting a `DataContext` — a `ContextMenu` is a popup and sits outside the row's visual tree. If you
  derive from `ContextMenu` to carry the clicked entry, you **must** override `StyleKeyOverride` (see the
  `avaloniaui` skill) or the popup opens at 0×0 and never appears.

- **Row selection: one anchor, and it only exists while something is selected.** The playlist grid went
  through four broken versions before settling here, so copy the model rather than inventing one:
  - `_anchor` is set **only when a row is selected** by a plain click on its checkbox (or by the first
    Shift click when the selection is empty); `RefreshState` clears it the moment `SelectionCount` hits 0.
    A plain click on the row body moves the *current row* and must not touch the anchor.
  - `ExtendSelection` = `anchor ?? clickedRow`, selects everything between the two in the **visible**
    (`_view`) order, and does not move the anchor — so repeated Shift clicks re-aim the end of the range.
  - Shift on a checkbox and Shift on the row body are the same operation. The checkbox's own toggle fights
    it (it flips on release), so remember "this press was a Shift selection" and, in the checkbox's `Click`,
    set `box.IsChecked = entry.IsSelected` from the model instead of applying the toggled value.
  - A checkbox swallows `PointerPressed`, so the row handler never sees that press. Register
    `AddHandler(PointerPressedEvent, …, RoutingStrategies.Bubble, handledEventsToo: true)` and bail out
    unless `e.Handled` and Shift are both true, otherwise you also steal clicks from the row buttons.
  - Select-all must decide from the **model** (`all entries in view already selected?`), not from the box's
    post-toggle `IsChecked`: from the indeterminate state a click yields `false`, which would clear the
    selection instead of completing it.

## 6. Icons

Inside your own window, render an SVG asset with the same control the player's chrome uses:

```xml
<core:SvgIcon xmlns:core="clr-namespace:ChroaVideoPlayer.Core.UI;assembly=ChroaVideoPlayer.Core"
              Icon="avares://MyPlugin/Icons/icon.svg"
              IconSize="14"
              Foreground="#E0E0E0" />
```

It draws the path as a stroke in `Foreground`, so an icon that should read as "danger" (a delete button)
just sets `Foreground` to a red brush. Note it is a *display* control, not a button — wrap it in a `Button`
and style that yourself.

```
avares://MyPlugin/Icons/icon.svg
```

- A bare path such as `/Icons/icon.svg` resolves against the **player's** assembly and silently renders
  nothing. Always use the full `avares://<your assembly name>/...` form.
- The SVG should be `viewBox="0 0 24 24"`; nothing is auto-scaled, so any other viewBox renders wrong.
- Supported elements: `path`, `rect`, `circle`, `ellipse`, `line`, `polyline`, `polygon`. Everything else is
  ignored, and a shape that fails to parse is dropped silently.
- `Icon` also accepts raw path data (`"M8 6h13"`) — no `.svg` suffix means it is parsed as geometry.

## 7. Wording in the UI

Never expose implementation language to the user. `Label`, window titles and every string inside the
window are user-facing: write "Playlist", not "Playlist plugin", and never mention assemblies, scanning or
add-ins.

## 8. State and lifecycle

- `CreateWindow()` runs **every time** the user opens the plugin — a new window instance each time. Keep
  state on the plugin object, not on the window.
- The player closes plugin windows when it exits.
- Handing state to the window works well with an `ObservableCollection` bound to an `ItemsControl`: the
  window then updates live without the plugin holding a reference to it.
- **Put the plugin's real work in a plain service class, not in the window and not in the plugin shell.**
  `ChroaVideoPlayer.Plugins.Playlist/Service/PlaylistService.cs` is the reference implementation: it owns
  the entries, the persistence calls and the auto-play queue, and exposes the operations as methods
  (`Add`, `AddRange`, `Remove`, `RemoveRange`, `Move`, `Search`, `Play`, `Reveal`, `Track`,
  `SetAutoPlay`). It references **no Avalonia type** — only `ObservableCollection` — so the same object can
  later be driven by an MCP host, a test, or a second window, and it never calls mpv itself: playback
  leaves through the `PlaybackRequested(path)` event and the host decides what "play" means. The window
  then only renders — it calls the service and keeps its own filtered `_view` — and the plugin class
  shrinks to event wiring plus a public `Service` property that a future MCP layer can resolve.
- **Split the window the same way the playlist does**: `PlaylistPluginWindow` owns the title, toolbar and
  window-level drag/drop, and `Controls/PlaylistGrid.axaml` owns the header, rows, context menu and
  selection. The grid calls the service directly and exposes only `ShowService`, `Filter`,
  `RemoveSelected`, `TotalCount`, `SelectionCount` and a `SelectionChanged` event — no forwarding events
  per operation. A window-level `StyleInclude` still styles the user control's content through the logical
  tree, so a control that borrows the window's stylesheet works; add the same include to the control if it
  ever has to stand alone.
- **Detach your event subscriptions when a window closes.** `CreateWindow()` builds a new window each time,
  and the service it talks to outlives it, so unsubscribing in `OnDetachedFromVisualTree` (both in the
  window and in any user control it contains) is what stops every closed window from being kept alive by
  the long-lived collection/`AutoPlayChanged` handlers.

## 9. Dependencies

Stay on `ChroaVideoPlayer.Core` + `Avalonia`. The build currently copies only the plugin's own assembly
into `Plugins`, so a third-party NuGet dependency is not resolvable at runtime yet — ask before adding one.

## 10. Traps worth knowing

- **Renaming or deleting a plugin project leaves its old DLL in `Plugins\`** — the stale plugin still loads
  and appears in the menu a second time. Delete the file after such a change.
- A plugin DLL is only refreshed by a build, and a running player locks its own output — close the player
  before rebuilding, or the copy step fails with `MSB3021` / `MSB3027`.
- **A RID publish asks for the plugin's target path *with* the RID, but the plugin is built *without* one.**
  Under `dotnet publish -r win-x64`, `GetTargetPath` answers `…\bin\Release\net10.0\win-x64\MyPlugin.dll` while
  the plugin really lands in `…\bin\Release\net10.0\MyPlugin.dll`, so the copy dies with `MSB3030`. That is why
  the target passes `RemoveProperties="RuntimeIdentifier;SelfContained;RuntimeIdentifiers"`; do not "fix" it by
  giving the plugin projects a runtime identifier instead.
- The plugins are deliberately copied twice: `AfterTargets="Build"` into `$(OutDir)Plugins` (what a local run
  loads) and `AfterTargets="Publish"` into `$(PublishDir)Plugins`, because files written into the build output
  by a custom target are not part of the publish item list and would otherwise be missing from a release.
- Plugins are loaded from `Plugins\` at startup only; there is no reload.
- Anything that publishes player events belongs in the control/window layer (`MpvVideoPlayer`), never in
  `ChroaMpvContext` or the view model.

## 11. Trust

Plugins run **in the player's process with full trust** — no sandbox, no isolated load context, no unload.
Only ship or install plugins you trust.

## Build note

On machines without network access the Avalonia telemetry task can fail (`MSB4018`, writing to
`%LOCALAPPDATA%\AvaloniaUI\BuildServices`). Build with `-p:UsedAvaloniaProducts=` to skip it.

## Checklist

- [ ] Class library, `net10.0`, references Core + Avalonia, added to the solution
- [ ] Registered in `ChroaVideoPlayer.csproj` with `IsPlugin="true"`
- [ ] `IChroaPlugin` implemented; `Label` free of implementation terms
- [ ] `Enabled` set honestly (`false` = scanned but hidden)
- [ ] `Context` declared as an auto-property for the player to fill in
- [ ] Interactive rows/canvases marked `Classes="interactive"`
- [ ] Title bar carries only the title and the close button
- [ ] Icon buttons built from `core:SVGIconButton` with `VerticalAlignment="Center"` (content 28/14,
      close 32/16) and the player's hover, pressed and red-destructive colours
- [ ] Body text 14, window title 16, no accent colour introduced
- [ ] Icon uses a full `avares://` URI and a `0 0 24 24` viewBox
- [ ] Window derives from `PluginWindow` with a public parameterless constructor
- [ ] Event handlers assumed to run on the UI thread
- [ ] State kept on the plugin, not the window
