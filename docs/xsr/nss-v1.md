# NSS/1 authoring and style API 1

Decision date: 2026-10-14. NSS/1 is a finite, typed, script-free stylesheet language.
The historical host NSS language remains accepted for host templates. External
themes use the separately validated NSS/1 compiler and style-only commit channel.

## Sources, identity and precedence

`theme.json.styles` is an ordered list. Tokens and rules resolve deterministically
in that order; selector specificity breaks ties before later rule order. NXML
retains authored kind, key, classes and role independently of flattened base
styles. Selector matching uses those identities; themes cannot change them.
Explicitly protected host semantic/lifetime policy remains final.

Public selectors use kind, class, key and role. A compound selector may be
`Button.capsule:hover`, `Text#TitleMain`, or `[key="dotted.key"][role=Button]`.
Whitespace selects descendants; `>` selects direct children. There are at most
eight selector parts. Interaction pseudo classes are `hover`, `pressed`, `focus`,
`focus-visible`, `selected`, `disabled`, and `checked`, and apply to the target.
They inspect host state and cannot set it. Unsupported selector syntax rejects
the stylesheet, including rules that match no currently loaded node.

## Tokens, options and conditions

```nss
@tokens {
  accent: #68A5FF;
  inset: 12;
}

Button.capsule { Border: var(accent); }
Text#TitleMain { FontSize: option(title-size); }

@when mode = dark {
  Text#TitleMain { Foreground: #FFFFFF; }
}
@when platform = linux and option(ornament) = true {
  Text#TitleMain { CornerRadius: 6; }
}
@viewport width >= 700 and width <= 1400 {
  Text#TitleMain { FontSize: 25; }
}
@supports grid {
  StackPanel#TitleBrand { GridColumns: 1fr; }
}
```

`var(accent)` and `var(--accent)` refer to the same exact token. `option(id)` uses
the current manifest's finite typed option value. Tokens/options are not state
bindings and cannot produce commands. Conditions support bounded grouping,
`and`, `or`, and `not`, platform (`windows`, `linux`, `macos`), light/dark mode,
option comparisons, capability checks and viewport width/height comparisons.
Unknown option IDs, invalid numeric values, cycles and evaluation-budget overflow
are errors. There is no JavaScript, reflection, function callback or network import.

## Presentation properties

Capitalized legacy property names and documented CSS kebab-case aliases normalize
to the same finite property. Numeric values must be finite and within the compiler's
limits. Colors are literal values for external themes; the built-in RGB compatibility
mapping does not rewrite third-party authored colors.

| Group | Properties |
| --- | --- |
| Layout | Width/Height, MinWidth/MaxWidth, MinHeight/MaxHeight, Weight, Margin/Padding, HorizontalAlignment/VerticalAlignment, Direction, Spacing |
| Paint | Background/Foreground/Border/Hover, Surface, Opacity, CornerRadius, BorderWidth, BlurRadius, Shadow, BackgroundImage, BackgroundImageFit, BackgroundImageOpacity, BackgroundGradient, ImageSource/IconSource |
| Text | FontSize, FontWeight, FontFamily, TextAlignment, WrapText |
| Grid | GridColumns/GridRows, ColumnGap/RowGap, GridColumn/GridRow, GridColumnSpan/GridRowSpan |
| Position | Position, Left/Top/Right/Bottom, Order |
| Transform | Transform, TranslateX/TranslateY, ScaleX/ScaleY, Rotation |
| Motion | Animation with named finite keyframes |
| Virtual metrics | ItemWidth/ItemHeight, ItemSpacing, ItemColumns under a host-owned virtual layout contract |

Grid tracks are `auto`, bounded pixel values or positive `fr` values. A column
span must fit the parent's admitted tracks. An explicit position extending past
the final column rejects the candidate; automatic placement wraps to a row that
can accommodate the span. Tracks and spans are not silently clipped. Position is
`Flow`, `Absolute` or `Overlay`; it does not reparent nodes or alter modal ownership.
Order affects permitted visual ordering while preserving owned Dynamic membership.
Transform uses bounded `translate(x,y) scale(x,y) rotate(degrees)` operations.
Shadow is `color offsetX offsetY blurRadius`. BackgroundGradient is a bounded
`linear-gradient(angledeg,color position%,color position%)`. Comma thickness values
retain legacy Left,Top,Right,Bottom order; whitespace thickness values use CSS
Top,Right,Bottom,Left order (one, two or four values). FontFamily can name a family or an
admitted `asset("fonts/Font.ttf")`. Resources resolve through prepared bindings;
the compiler and renderer do not open paths.
`BackgroundImageFit` accepts `cover`, `contain`, `stretch` or `tile`. SVG tile fit
repeats a prepared native vector picture; it does not expand an authored image into
one shape collection per visible tile. SVG numbers, cumulative transforms and
viewBox dimensions must satisfy the native resource bounds documented in
[theme-resource-security.md](theme-resource-security.md).

Virtualized list roots carry a host-owned layout contract. A theme cannot modify
only the painted row height while leaving spacer/scroll/realization calculations
unchanged. Effective metrics belong to the same activation generation and retain
the logical item and scroll anchor. Layout, transform, clipping, hit testing and
keyboard navigation consume the same geometry.

## Animation

```nss
@keyframes breathe {
  0%   { Opacity: 0.82; }
  100% { Opacity: 1; }
}
Text#TitleMain { Animation: breathe 2s infinite alternate; }
```

Animated properties are Opacity, TranslateX/Y, ScaleX/Y, Rotation, CornerRadius and
BlurRadius. Each property needs both 0% and 100% endpoints. Durations, keyframes and
repetition are bounded. The renderer owns progression; unchanged frames do not
reparse NSS. Reduced motion disables theme motion regardless of author settings.

## Capabilities and evidence

Required capabilities are checked before activation. Optional capabilities need
declared fallback. Capability names come from the host's versioned registry; an
accepted declaration, a portable scene fixture and an actual native execution are
different pieces of evidence. Advanced is a runnable author template, not an
assertion that every platform has native acrylic, mica, every installed codec or
the same font fallback. The capability matrix and platform receipts in the NTP/1
acceptance note record actual execution coverage.

No style property may set Recipe, Kind, Key, Class, Role, Command, bindings,
Enabled/IsVisible/Selected/Checked, password state, tree ownership, Anchor or
DismissCommand. The compiler rejects these names even in inactive conditional rules.
