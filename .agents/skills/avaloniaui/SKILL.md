---
name: avaloniaui
description: This skill should be used when writing or debugging Avalonia UI in Chroa Video Player — XAML, controls, styles, templates, popups, context menus, pointer input, custom controls, or when a UI feature silently does nothing. Covers the traps this project has actually hit, with the measurements that pinned them down, and how to bisect a suspected one.
---

# Avalonia traps in Chroa Video Player

Every entry below was measured in this repository, not guessed. The numbers matter: they are how the cause was
separated from the symptom.

## 1. A derived control needs `StyleKeyOverride` — or its popup is 0×0

A control that derives from a themed control looks its theme up **by its own type**. With no theme for the
derived type it gets no template, and a `Popup`-based control then measures to nothing:

```csharp
public partial class PlaylistEntryMenu : ContextMenu
{
    protected override Type StyleKeyOverride => typeof(ContextMenu);
}
```

**Symptom worth recognising**: a `ContextMenu` subclass "opens" — `IsOpen == true`, the `MenuItem`s exist in the
visual tree with sane bounds — yet nothing appears, because the popup root stays **0 × 0**. Measured:

| Variant | Popup root size |
| --- | --- |
| plain `ContextMenu`, built in code | 178 × 69.3 ✓ |
| plain `ContextMenu` + `Items.Add(MenuItem)` | 178 × 69.3 ✓ |
| **subclass of `ContextMenu`** | **0 × 0** ✗ |
| **subclass + `StyleKeyOverride`** | **178 × 69.3** ✓ |

Everything else was innocent and was ruled out one variable at a time: the plugin window, its transparency and
missing decorations, the `ItemsControl` the menu hangs off, the window-scoped styles, `Items` vs `ItemsSource`,
and XAML-declared vs code-built.

`SVGIconButton : Button` renders fine without the override, so this is not a blanket rule for every derived
control — it depends on how the base control's template is provided. When a derived control renders nothing or
sizes to zero, try the override first.

## 2. `IsOpen == true` is not proof a popup is visible

`IsOpen` is a state bit. A popup can be open, parented and measured at zero, so a headless probe that only
checks `IsOpen` reports success while the user sees nothing. Measure the popup instead:

```csharp
var root = TopLevel.GetTopLevel(menu);
Console.WriteLine((root as TopLevel)?.ClientSize);   // 0,0 => nothing will ever be drawn
```

Headless is fine for input logic (gestures, handlers, which element got the event) but it cannot tell you a
popup was rendered. For anything involving a popup, run the probe on the real platform (`UsePlatformDetect()`)
and compare against a known-good control in the same run.

## 3. Fluent controls are drawn for a light surface

The player's palette is monochrome on black; the Fluent theme is not. Stock controls bring a white background,
black text and the blue accent with them, and the fix is to restyle the template parts by name (verified names,
not guesses):

```xml
<!-- ContextMenu / MenuItem: default is a white popup with black labels -->
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

<!-- CheckBox: unchecked border is #99000000 (invisible on black), checked fill is #ff0078d7 (accent blue) -->
<Style Selector="CheckBox /template/ Border#NormalRectangle">
    <Setter Property="Width" Value="14" />
    <Setter Property="Height" Value="14" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderBrush" Value="#55FFFFFF" />
</Style>
<Style Selector="CheckBox:checked /template/ Border#NormalRectangle">
    <Setter Property="Background" Value="#E0E0E0" />
</Style>
<Style Selector="CheckBox:indeterminate /template/ Border#NormalRectangle">
    <Setter Property="Background" Value="#E0E0E0" />   <!-- otherwise the glyph lands dark-on-black -->
</Style>
<Style Selector="CheckBox /template/ Path#CheckGlyph">
    <Setter Property="Fill" Value="#101010" />
</Style>
```

- `CheckBox` also has `MinHeight` 32 by default — taller than a 28 px row, so set `MinHeight="0"`.
- `MenuItem` hover must be set on `Border#PART_LayoutRoot`, not on the `MenuItem` itself.
- A window-scoped style beats the app-wide one, so a plugin window can override the player's own look.
- **The accent colour cannot be overridden with a `Style` — not even on the template part.** Measured: adding
  `Selector="core|InputBox:focus /template/ Border#PART_BorderElement"` with a `BorderBrush` setter left the
  focused brush at the theme's accent (`#ff0078d7`); the `ControlTheme`'s state setter wins. Overriding the
  **resource** does work:

  ```xml
  <Application.Resources>
      <Color x:Key="SystemAccentColor">#FFE0E0E0</Color>
      <Color x:Key="SystemAccentColorLight1">#FFE0E0E0</Color>
      <Color x:Key="SystemAccentColorDark1">#FFE0E0E0</Color>
      <!-- …Light2/3 and Dark2/3 as well -->
  </Application.Resources>
  ```

  Measured effect: the same focused border went from `#ff0078d7` to `#ffe0e0e0`. This app pins every accent
  variant to `#E0E0E0` in `App.axaml`, so no stock Fluent control can introduce a colour the palette lacks.
- A focused `TextBox` paints its border on `Border#PART_BorderElement` (accent brush, `BorderThickness` 2 — it
  jumps from 1 when focus arrives). `TextBox.InnerLeftContent` is the supported way to put an icon inside the
  box; `TextBox.Watermark` is obsolete in Avalonia 12 — use `PlaceholderText`.

## 3a. Dialogs for this player are `Core/UI` windows, not Fluent ones

`AlertDialog` / `PromptDialog` (`Core/UI/`), surfaced through the static facade
`ChroaDialogs.AlertAsync(owner, message)` / `ChroaDialogs.PromptAsync(owner, message)` (returns `bool`) in
`Core/Utils/`, derive from `ChroaWindow` — so they inherit the
black panel, the 10 px radius and the `0 6 24 0 #A0000000` shadow for free and only supply content through
`PanelContent`. They draw their buttons as a plain `Border` + `TextBlock` driven by
`PointerEntered` / `PointerExited` / `Tapped`, **not** a themed `Button`: on a black panel the Fluent hover
state repaints the template with a near-white brush, and neutralising it needs a `/template/ ContentPresenter`
override for every state. Modality: `WindowStartupLocation = CenterOwner`, `SizeToContent = Height`,
`ShowDialog(owner)` (and `ShowDialog<bool>` when a result is needed).

## 4. A captured pointer swallows the release, so gestures never complete

`Pointer.Capture(element)` on press routes the release to the capture target and its ancestors — the element
that was pressed never sees it, and Avalonia's `Tapped` / `DoubleTapped` need both halves of the gesture. This
is what made double-click-on-a-row impossible inside `PluginWindow` (whose drag captures the pointer) and why
`PluginWindow` skips controls marked `Classes="interactive"` (any `Button` is exempt already).

Check it by observing what the element actually receives, not what you expect:

```
row PointerPressed      ← press arrives
row PointerReleased     ← absent: something captured the pointer
row DoubleTapped        ← therefore never raised
```

## 5. Shape sizing: `Viewbox` centres, `Stretch` does not

`SvgIcon` puts its `Path` in a 24×24 `Canvas` inside a `Viewbox` sized to `IconSize`, so the geometry is scaled
and centred for free. A hand-rolled `<Path Stretch="Uniform" Width="14" Height="14"/>` has no such layer — the
geometry and the stroke scale under different rules, and the icon renders off-centre inside its button. Use the
project's own icon control rather than raw `Path` markup.

## 6. What the build already proves

XAML is compiled, so a clean build rules out more than it looks: event handler names (`Click="OnX"`), `x:Name`
fields, and every binding path under an `x:DataType` (`{Binding IsSelected}` must exist on the model). A
feature that compiles and then silently does nothing is therefore a runtime/visual problem — go to §1, §2 and §4
rather than re-reading the XAML.

## 7. How to bisect a silent UI failure

The context-menu bug took nine variants. The method that found it, in order:

1. **Reproduce in a probe** against the real deployed assembly (not a copy of the source).
2. **Reduce to two halves**: is the event arriving? is the visual produced? One boolean each tells you which
   half to keep digging in.
3. **Change one variable per run** and keep a table of the results — window type, styles present or not,
   subclass or plain, XAML or code.
4. **Prefer a number to a boolean.** `ClientSize` found the cause in one run after three boolean probes had all
   said "opened".
5. Run the failing case **on the real platform** — headless can differ precisely where popups and sizing are
   concerned.
6. **Do not fix by deduction.** Three successive "plausible cause" fixes changed nothing, because each one was
   verified only for the quantity that *felt* wrong (visibility, minimise state, position) while the quantity
   that actually changed was one nobody had measured yet (z-order). Reproduce the state first, then measure
   every quantity that could differ and compare it against a healthy run of the same steps.
7. **When a style does not bite, dump the element *and the resources* — not just the selector.** The accent
   colour on a focused `TextBox` survived three different selector attempts, because the brush came from a
   `ControlTheme` state setter that reads a theme resource. Measuring the resource value, and overriding that
   resource, is what fixed it (see §3).

## 8. A window that is "open but invisible"

A window can be `IsVisible == true`, `WindowState == Normal`, at a sane `Position`, and still be impossible to
see — because it is **covered**. Avalonia's API cannot tell you the z-order; Win32 can. Reconcile the four
cases before touching anything:

| Symptom | What it is | How to tell |
| --- | --- | --- |
| `IsVisible == false` | hidden (`Hide()`, or `_hiddenByHost` in `PluginWindow`) | Avalonia alone |
| `WindowState == Minimized` | minimized, often as a side effect of the owner window | Avalonia alone |
| rect outside the screen bounds | off-screen (docking math, or a position set while the screen was unknown) | `GetWindowRect` |
| **all of the above look normal** | **covered — a z-order problem** | `GetWindow(hwnd, GW_OWNER)` + `EnumWindows` order |

What the last row looked like in this repository: the plugin window was not above the player, so a taskbar
focus cycle (player deactivates, gets re-activated) left it hidden behind the player — and because the two
overlapped, "not visible" was indistinguishable from "closed", which is why the menu needed two clicks.

The cause, measured: **`Owner` only takes effect when it is set after `Show()`.** Assigning it before leaves
`GW_OWNER != host` — the window is not an owned window at all, and Windows then makes no promise about keeping
it above the owner. Re-asserting `Owner` in `OnOpened` flipped both facts:

| | `GW_OWNER == host` | z-order (0 = topmost) | verdict |
| --- | --- | --- | --- |
| `Owner` set before `Show()` only | False | plugin 31, host 29 | covered ✗ |
| `Owner` re-asserted after `Show()` | True | plugin 29, host 30 | above ✓ |

- `Window.Owner`'s setter is **protected** — only a derived `Window` can assign it. Assigning it again after
  `Show()` does reach the platform; it is not a no-op just because the value is unchanged.
- `ShowInTaskbar` is not what decides ownership — flipping it changed nothing in those measurements.
- Compare **relative** positions in `EnumWindows` order, never absolute indices: unrelated desktop windows
  shift them run to run.

## 9. Clamping a window into the working area

`Screens.ScreenFromWindow(window)` returns a **nonsense working area for a window that is already off-screen**
— measured `-1920,550,1920,1032` for a window at `x = -984` on a `0,0,3840,2088` desktop, and it reported
`Scaling = 1` where the real screen reports `1.5`. A clamp built on that result clamps the window back into the
bogus rect and changes nothing at all:

```
before:                                Position = -984, 550
after KeepInsideWorkingArea(window):   Position = -984, 550     ← self-cancelling
```

Resolve the screen from a window you know is on screen (the anchor) and pass it in:

```csharp
public static void KeepInsideWorkingArea(Window window, Window? anchor = null)
{
    var screen = (anchor is not null ? anchor.Screens.ScreenFromWindow(anchor) : null)
        ?? window.Screens.ScreenFromWindow(window)
        ?? window.Screens.Primary;
    ...
}
```

- `WorkingArea` and `Window.Position` are both **physical** pixels while `Window.Width` is **logical** —
  multiply the size by `screen.Scaling` before comparing.
- Anchor only where anchoring is right: a window the user deliberately dragged to another monitor should keep
  clamping to *its own* screen.
- **A restored size of 0 makes a frameless window vanish, not minimize.** `chroa.db` rows written while the
  window was fullscreen or maximized kept `WindowWidth = 0` (the fields are only filled in `Normal` state),
  and the next restore called `FitInsideWorkingArea(window, 0 + 48, 0 + 48)` → a 48×48 window whose 24 px
  shadow margin leaves a **0×0 panel**: audio keeps playing, nothing is visible, and there is no edge left to
  grab. Guard all three layers: skip saving when the window is not `Normal` or its size is non-positive, skip
  restoring when the stored size is non-positive, and refuse any fit below a sane minimum (200×120) inside
  the geometry helper so no future caller can reproduce it.

## 10. Making an input lose focus when the user clicks elsewhere

Clicking a non-focusable element — a `Panel`, a `Border`, a list row — does **not** move focus away from a
`TextBox`. WPF clears it; Avalonia leaves the caret where it is. Clearing it needs a real focusable target, and
"just focus the window" never works because **a `Window` is `Focusable = False`**:

| Attempt | `TextBox.IsFocused` afterwards |
| --- | --- |
| `window.Focus()` | True ✗ |
| `FocusManager.Focus(window)` | True ✗ |
| `FocusManager.Focus(aFocusableControl)` | False ✓ |
| `input.Focusable = false` then `true` | True ✗ |

`FocusManager.Focus(null)` is not the answer either: it compiles, then throws at runtime — and because it threw
from inside a `PointerPressed` handler it took the whole input pipeline with it, so every button in that window
stopped responding.

The recipe `ChroaVideoPlayer.Core/UI/InputBox` uses: on attach, take the top level's `Content` root, make it
focusable *without* adding a tab stop, and hand focus to it on any press outside the box.

```csharp
root.Focusable = true;
root.IsTabStop = false;
```

Hook that press on the **top level** with `handledEventsToo: true` — a plugin window's panel handler marks
presses handled for dragging, so an ordinary handler never sees them — and unhook in
`OnDetachedFromVisualTree`. If the control derives from a themed control, remember §1.

## 11. A `BoxShadow` needs reserved margin, or it is simply not drawn

Setting `BoxShadow` on a `Border` that fills its own control produced **no visible shadow at all** — the
popup's edge was a hard line against the video, with no falloff anywhere. The shadow is painted outside
the border, and it needs room *inside the control's own bounds* to appear; a border that is the same size
as its control has nowhere to put it.

The repository already had the working shape in `ChroaVideoPlayer.Core/Plugin/UI/PluginWindow.cs`:

```csharp
private const double ShadowMargin = 24;

_panel = new Border
{
    Margin = new Thickness(ShadowMargin),
    Background = Brushes.Black,
    BoxShadow = BoxShadows.Parse("0 6 24 0 #A0000000"),
    CornerRadius = new CornerRadius(10),
};
```

Two borders, two jobs, never one border doing both:

```xml
<UserControl Width="368" Height="228">          <!-- content + ShadowMargin * 2 -->
  <Border Margin="24" Background="#E6000000" CornerRadius="6"
          BoxShadow="0 6 24 0 #A0000000">        <!-- paints background + shadow -->
    <Border CornerRadius="6" ClipToBounds="True"> <!-- clips content to the rounded corners -->
```

- `ClipToBounds="True"` on the **outer** border clips the shadow away along with everything else, which is
  how the shadow disappeared in the first place. Put it on an inner border.
- Use the project's value, `0 6 24 0 #A0000000` — it is what `PluginWindow` and
  `Styles/PlayerBar.axaml`'s `Border.subtitleMenu` use. An invented `0 6 20 0 #99000000` only looks
  inconsistent next to them.
- **Anything positioned absolutely must compensate the margin.** With `ShadowMargin` on the border, a
  `Canvas.SetTop(control, barTop - control.Height - gap)` puts the *visible* popup `gap + ShadowMargin`
  away from the bar. The vertical formula is
  `Canvas.SetTop(control, barTop - control.Height + ShadowMargin - gap)`; the horizontal one is unchanged,
  because a symmetric margin keeps "centre the control" equal to "centre the visible popup".
- A `const` used by the positioning code outside the control has to be `public` (`ClipCapture.ShadowMargin`),
  not `private`.

## 12. A 1px line around the frameless window: it is `WindowDecorations="BorderOnly"`

Symptom: a hairline runs along the window edges — most obvious at the top and bottom, but present on all
four sides. It looks like a bug in the app's own content, and it is not.

**Measured** with an isolated probe (a window declared exactly like `MainWindow`, content filled with
magenta so any non-magenta pixel is a frame), 1.5× scaling:

| Configuration | Non-client band | Rounded corners + DWM shadow |
| --- | --- | --- |
| `BorderOnly` + `ExtendClientAreaToDecorationsHint="True"` (the player's setup) | **2 physical px ≈ 1.33 logical px**, `#363636`, every side | **both present** |
| `BorderOnly` + `ExtendClientAreaToDecorationsHint="False"` | **1 physical px ≈ 0.67 logical px**, `#0C0C0C` | **both lost** |
| `WindowDecorations="None"` | **0 — `GetClientRect` equals `GetWindowRect` exactly** | both lost |

Two conclusions, both measured on the real app as well as the probe:

- The band is the price of the DWM frame, and **it cannot be traded away gradually**: the only setting that
  removes it (`None`) also removes the rounded corners and the drop shadow. Turning
  `ExtendClientAreaToDecorationsHint` off halves the band and loses the corners and shadow with it — a
  strictly worse bargain, tried and rejected.
- Having a shadow, rounded corners **and** no band means abandoning the DWM frame entirely: a transparent
  window whose content is inset by a shadow margin around a `Border` with `CornerRadius` + `BoxShadow`
  (§11), exactly as `PluginWindow` does. For a video-filling player that costs a transparent ring around
  the picture and touches every piece of window sizing, moving and resizing math — a redesign, not a fix.

Dead ends — all **measured to have no effect**, so do not spend time on them:

- `DWMWA_BORDER_COLOR` (34) = `DWMWA_COLOR_NONE` (`0xFFFFFFFE`): accepted, `hr = 0`, band unchanged.
- `DWMWA_USE_IMMERSIVE_DARK_MODE` (20) = 1: accepted, `hr = 0`, band unchanged.
- `DwmExtendFrameIntoClientArea(hwnd, {-1,-1,-1,-1})`: `hr = 0`, `GetClientRect` unchanged.
- `TransparencyLevelHint`: `AcrylicBlur` and `None` produce identical bands.
- `Application.RequestedThemeVariant`: affects only how Avalonia paints, never the Win32 frame. Pinning the
  app to Dark fixes Avalonia-side light-mode leakage (stock control surfaces) and leaves this band alone —
  do not mistake one symptom for the other.

**How to reproduce and measure it**, instead of guessing (two wrong deductions were made before this):

1. `GetWindowRect` minus `GetClientRect` = 2 px in each dimension is already proof of a 1 px non-client
   band; the client area starts one pixel inside the window, so the app *cannot* paint there.
2. Build a throwaway window with **magenta content** and measure `CopyFromScreen` pixels per row/column.
   With magenta any band is unmissable; measuring the real player fails whenever the video edge happens to
   be dark or the user moves the window mid-measurement.
3. Vary exactly one declaration per run (`WindowDecorations`, then `ExtendClientArea…`, then transparency).

## 13. A self-drawn window panel: three traps, all silent

Once the window uses `WindowDecorations="None"` plus a panel drawn inside it (`ChroaWindow` in
`Core/UI/`), three failures appear that the compiler cannot see.

**Deleting an element orphans the style selectors that named it.** The hover that reveals the control bar
was `Grid.root:pointerover Border.overlay`; it had been retargeted to `Border.chrome:pointerover` so that a
transparent shadow ring would not trigger it. When the chrome panel moved into the base class, the element
carrying `Classes="chrome"` disappeared — and the selector simply matched nothing, leaving
`Border.overlay` at `Opacity = 0` forever. **Every button and the whole control bar vanished, with a clean
build.** XAML selectors are not validated, so removing a classed element and removing its styles are one
operation, not two. Move the selector back onto an element that still exists (`Grid.root`, now itself
inside the panel, so the ring is excluded again).

**`WindowState.Maximized` needs the panel flushed too.** Flushing only for `FullScreen` leaves the 24 px
margin in place when maximized: the window does fill the working area, but the panel floats 24 px inside
it, which reads as "the maximize button did nothing, the edges have gaps". Treat
`Maximized` the same as `FullScreen` — zero margin, corner radius and shadow — and hide the resize grips,
since `WindowResizer` already ignores presses outside `WindowState.Normal`.

**A member named `Panel` collides with `Avalonia.Controls.Panel`.** `CS0119: 'Panel' is a type, which is not
valid in the given context` — in `new WindowMover(this, Panel)` the type wins and the property is never
found. Name the chrome border something unambiguous (`PanelBorder`), which keeps it distinct from
`PanelContent` as well.

Two smaller things worth knowing about the same change:

- The base sets `Content` to its own panel, so **derived XAML must place its content in `PanelContent`**.
  A derived window that overrides `OnPropertyChanged` to intercept `Content` (as `PluginWindow` does, to
  keep plugin XAML working) must compare against the base's already-initialised panel property — comparing
  a *derived* field would read `null` during base construction and throw.
- A transparent ring means the outer 24 px belongs to no content: dragging the ring no longer moves the
  window and dropping files on it does nothing. If that matters, the window needs a pointer handler for
  the ring — the panel itself only covers the inset area.

## 14. Drag-to-reorder in an `ItemsControl`: Avalonia has no built-in, here is the recipe

There is no `CanReorderItems` (that is WinUI). Avalonia gives you the generic drag-drop layer and you
assemble the reorder yourself. Verified against **12.1.4** — the API is not the 11.x one:

```csharp
// process-local payload, no serialization, so you can carry the item itself
public static readonly DataFormat<PlaylistEntry> EntryFormat =
    DataFormat.CreateInProcessFormat<PlaylistEntry>("chroa-playlist-entry");

var data = new DataTransfer();
data.Add(DataTransferItem.Create(EntryFormat, entry));
await DragDrop.DoDragDropAsync(trigger, data, DragDropEffects.Move);   // 11.x: DoDragDrop
```

- `DragDrop.DoDragDropAsync` takes the **`PointerPressedEventArgs` of the original press**, so store it in
  `PointerPressed` and call the drag from `PointerMoved` once the pointer has moved more than a few pixels.
  A 4 px threshold is enough to keep click and double-click working.
- `DragEventArgs.DataTransfer.TryGetValue(format)` / `.Contains(format)` (`DataTransferExtensions`) read it
  back on the target; `e.GetPosition(row).Y > row.Bounds.Height / 2` is the cheapest before/after rule.
- The target needs `DragDrop.AllowDrop="True"` plus `DragDrop.DragOver` / `DragLeave` / `Drop`; the attached
  events are writable in XAML exactly like `DragDrop.DragOver="OnFileDragOver"`.
- `ObservableCollection.Move(from, to)` is *remove-then-insert*, so `to` must be computed on the
  post-removal list: `to = index + (after ? 1 : 0); if (from < to) to--;`.
- **A window-level file-drop handler will kill the internal drag.** The window's `DragOver` runs after the
  row's, sees no files, sets `DragEffects = None` and `Handled = true` — the drop never happens. Bail out at
  the top: `if (e.DataTransfer.Contains(EntryFormat)) return;`, and have the row set `e.Handled = true` for
  its own format so the window never sees it either.
- Show the insertion point from the *model* (two flags, `IsDropBefore` / `IsDropAfter`, plus two 2 px
  `Border`s in the row template) rather than from a floating overlay — no `TranslatePoint` maths, and it
  scrolls with the row for free.
- What the OS drag loop will not do for you: auto-scroll near the edges, and a drop target for the empty
  area below the last row. Both need explicit code (a timer, and a container-level `Drop`).

## 15. Turning a plain `ItemsControl` list into a virtualized one (`Core/UI/ChroaVirtualList`)

`ItemsControl` does not virtualize until the items panel is a `VirtualizingStackPanel` **and** something
bounds its viewport. `ChroaVirtualList` (Header / ItemTemplate / ItemsSource) does that: header in row 0, a
`ScrollViewer` holding the `ItemsControl` in row 1 of an `Auto,*` grid.

```csharp
_items = new ItemsControl
{
    ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
};
```

In 12.1.4 `ITemplate<T>` is no longer paired with a non-generic `ITemplate` you can implement (it erased to
`object Build()`), so hand-rolled template classes fail with `CS0738` — use `FuncTemplate<T>`.

Three traps, all silent and all hit for real:

- **Give the header and the rows one shared horizontal scroll context — do not fake it.** The first version
  put the header in a `Border(ClipToBounds) > ContentControl(RenderTransform)` and moved it by
  `-listScroll.Offset.X`. Three attempts to make that behave ("fill the width", "also recompute on
  `SizeChanged`/`LayoutUpdated`") all left a chunk of the header background missing once the horizontal
  scrollbar existed: a `Border` arranges its child at the *border's* size, so the header paints only the
  visible strip and the translate then exposes an unpainted region. The arrangement that works is nested
  scroll viewers:

  ```
  ScrollViewer (horizontal Auto, vertical Disabled)      ← scrolls header + rows together
    Grid  RowDefinitions="Auto,*"  MinWidth = outer viewport   ← fills the window
      ContentControl  header (stretches)
      ScrollViewer (horizontal Disabled, vertical Auto)  ← this is the viewport VSP virtualizes against
        ItemsControl  ItemsPanel = VirtualizingStackPanel
  ```

  Horizontal position can then never disagree, the fill is a single `MinWidth` on the shared grid, and the
  inner scroll viewer still gives `VirtualizingStackPanel` a bounded height. `IsHitTestVisible = false` is
  not needed anywhere, so checkboxes and `GridSplitter`s keep working.
- **`SharedSizeGroup` stops working once rows are virtualized** (unrealized rows never contribute), and a
  `GridSplitter` only ever resizes the column definitions of the grid it sits in. Keep the header as the
  single source of truth and copy its widths onto realized rows after layout:

  ```csharp
  ItemHost.LayoutUpdated += (_, _) => SyncColumnWidths();

  foreach (var row in ItemHost.GetVisualDescendants().OfType<Grid>())
  {
      if (row.ColumnDefinitions.Count != header.ColumnDefinitions.Count) continue;
      for (var index = 0; index < count; index++)
      {
          if (row.ColumnDefinitions[index].Width == header.ColumnDefinitions[index].Width) continue;
          row.ColumnDefinitions[index].Width = header.ColumnDefinitions[index].Width;
      }
  }
  ```

  The `!=` guard is what stops layout from ping-ponging; match on the column count (rows are the
  `ContentPresenter`'s direct `Grid` child) rather than on a business class name.
- **Measure before promising a win.** With virtualization only the visible ~20 containers exist, so opening a
  500-row list should be instant; if it is not, the panel never got a bounded viewport and no amount of
  batching elsewhere will help.







