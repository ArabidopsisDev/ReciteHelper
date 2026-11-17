# TKCode.Arc WPF adaptation

Source: https://github.com/AIDotNet/TKCode.Arc
Revision: 1bef215d677fdbaf7820f4a86b4dacd51a06ad1e
Copyright (c) 2026 TKCode.Arc contributors
Arc free components: Copyright (c) 2026 Elia Kuratli

The WPF theme adapts the semantic token vocabulary and button/input state structure from:
- src/TKCode.Arc.Core/Theme/ArcDesignToken.cs
- src/TKCode.Arc.Core/Theme/ArcPalette.cs
- src/TKCode.Arc.Core/Theme/ArcFonts.cs
- src/TKCode.Arc/Buttons/Button/ButtonToken.cs
- src/TKCode.Arc/Buttons/Button/Themes/ButtonTheme.axaml
- src/TKCode.Arc/TextFields/Input/Themes/InputTheme.axaml
- src/TKCode.Arc/TextFields/Textarea/Themes/TextareaTheme.axaml
- src/TKCode.Arc/Selects/Select/Themes/SelectTheme.axaml

ReciteHelper modifications: WPF ControlTemplate and Trigger implementations; Arc's neutral light palette, 44 px medium controls and 18 px control corners; grouped side navigation, constrained form widths, and continuous record lists. Native Windows fonts and locally drawn line icons are used. Checkbox, expander, slider and progress templates retain native WPF interaction. No Avalonia binaries, Arc Pro code, upstream icon assets or fonts are included. See LICENSE.txt and Arc-LICENSE.txt for the full MIT terms.
