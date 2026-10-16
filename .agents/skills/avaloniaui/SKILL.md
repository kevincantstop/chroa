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
