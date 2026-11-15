# TKCode.Arc WPF adaptation

Source: https://github.com/AIDotNet/TKCode.Arc
Revision: 1bef215d677fdbaf7820f4a86b4dacd51a06ad1e
Copyright (c) 2026 TKCode.Arc contributors
Arc free components: Copyright (c) 2026 Elia Kuratli

The WPF theme adapts the semantic token vocabulary and button/input state structure from:
- src/TKCode.Arc.Core/Theme/ArcDesignToken.cs
- src/TKCode.Arc/Buttons/Button/Themes/ButtonTheme.axaml
- src/TKCode.Arc/TextFields/Input/Themes/InputTheme.axaml
- src/TKCode.Arc/TextFields/Textarea/Themes/TextareaTheme.axaml

ReciteHelper modifications: WPF ControlTemplate and Trigger implementations, a compact 36 px control size, 4 px corners, an ink-blue accent, native Windows fonts, static state feedback, and a continuous desktop form layout. No Avalonia binaries, Arc Pro code, icon assets or fonts are included. See LICENSE.txt and Arc-LICENSE.txt for the full MIT terms.
