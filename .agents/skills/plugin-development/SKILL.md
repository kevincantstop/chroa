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

## 5. Visual conventions

A plugin window sits next to the player, so it has to look like it belongs there. These are the rules the
playlist window was tuned to; follow them instead of inventing a look.

**The title bar holds the title and the close button — nothing else.** Actions, counters and filters belong on
a row of their own between the title and the content, right-aligned, so the close button never moves around.

| Element | Spec |
| --- | --- |
| Window title | `FontSize="16"`, `Foreground="White"` |
| Body text — list cells, table headers, counters, button labels | `FontSize="14"` |
| Icon buttons in the content (per-row actions, a contextual delete) | 28×28, `CornerRadius="14"`, icon 14 px |
| The close button in the title bar | 32×32, `CornerRadius="16"`, icon 16 px |
| List rows | `Height="32"` |

One rule behind those numbers: **the icon takes about half the button's side and the rest is padding**, which
is what the player's own `SVGIconButton` does (`ButtonSize` 32, `IconSize` 16, `ButtonShape = Circle` →
`CornerRadius = min(width, height) / 2`). Copy its state colours too:

| State | Neutral | Destructive |
| --- | --- | --- |
| Background | `Transparent` | `Transparent` |
| Hover | `#40FFFFFF` | `#33FF6B6B` |
| Pressed | `#66FFFFFF` | `#55FF6B6B` |
| Icon | `#E0E0E0` | `#FF6B6B` |

- Give every button your own class with `Padding="0"`, `BorderThickness="0"` and `Cursor="Hand"` — the stock
  Fluent `Button` look does not belong on the black panel.
- **An icon-only button must carry a `ToolTip.Tip`** — that tooltip is its label.
- Set the pressed state as well as the hover one. A neutral `:pressed` rule also matches your destructive
  buttons, so give those their own `:pressed` colour or they flash white.
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

## 9. Dependencies

Stay on `ChroaVideoPlayer.Core` + `Avalonia`. The build currently copies only the plugin's own assembly
into `Plugins`, so a third-party NuGet dependency is not resolvable at runtime yet — ask before adding one.

## 10. Traps worth knowing

- **Renaming or deleting a plugin project leaves its old DLL in `Plugins\`** — the stale plugin still loads
  and appears in the menu a second time. Delete the file after such a change.
- A plugin DLL is only refreshed by a build, and a running player locks its own output — close the player
  before rebuilding, or the copy step fails with `MSB3021` / `MSB3027`.
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
- [ ] Icon buttons are round and follow the player's proportions (content 28×28 / radius 14 / icon 14,
      close 32×32 / radius 16 / icon 16) with its hover, pressed and red-destructive colours
- [ ] Body text 14, window title 16, no accent colour introduced
- [ ] Icon uses a full `avares://` URI and a `0 0 24 24` viewBox
- [ ] Window derives from `PluginWindow` with a public parameterless constructor
- [ ] Event handlers assumed to run on the UI thread
- [ ] State kept on the plugin, not the window
