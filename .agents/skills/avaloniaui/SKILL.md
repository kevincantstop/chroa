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


