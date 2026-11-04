using Godot;

namespace Roguelike;

/// <summary>
/// Тема оформления интерфейса в стиле «современный минимализм»: полупрозрачные тёмные панели,
/// мягкие скругления, тонкие светлые рамки и один акцентный цвет (в игре — цвет класса героя).
///
/// Theme в Godot — набор стилей для типов элементов (Button, Label, ProgressBar...).
/// Его назначают корневому элементу окна, и все вложенные элементы берут стиль оттуда.
/// </summary>
public static class UiTheme
{
    public static readonly Color Text = new(0.92f, 0.94f, 0.98f);
    public static readonly Color TextMuted = new(0.6f, 0.64f, 0.72f);
    public static readonly Color Positive = new(0.45f, 0.9f, 0.55f);
    public static readonly Color Negative = new(1f, 0.45f, 0.45f);
    public static readonly Color DefaultAccent = new(0.55f, 0.68f, 1f);

    private const int Radius = 10;

    public static Color Accent { get; private set; } = DefaultAccent;

    // Акцентные черты под заголовками: их создают один раз, поэтому при смене акцента перекрашиваем вручную
    private static readonly System.Collections.Generic.List<ColorRect> AccentBars = new();

    public static void RegisterAccentBar(ColorRect bar) => AccentBars.Add(bar);

    // Эти стили зависят от акцента, поэтому храним ссылки и перекрашиваем их в SetAccent
    private static StyleBoxFlat _buttonHover;
    private static StyleBoxFlat _buttonPressed;
    private static StyleBoxFlat _buttonFocus;
    private static StyleBoxFlat _barFill;
    private static StyleBoxFlat _sliderGrabber;

    public static Theme Main { get; } = Build();

    /// <summary>Сменить акцентный цвет: все кнопки и полоски перекрасятся сразу.</summary>
    public static void SetAccent(Color accent)
    {
        Accent = accent;
        _buttonHover.BorderColor = new Color(accent, 0.55f);
        _buttonPressed.BgColor = new Color(accent, 0.22f);
        _buttonPressed.BorderColor = accent;
        _buttonFocus.BorderColor = accent;
        _barFill.BgColor = accent;
        _sliderGrabber.BgColor = accent;

        AccentBars.RemoveAll(bar => !GodotObject.IsInstanceValid(bar)); // удалённые из игры — забываем
        foreach (var bar in AccentBars)
            bar.Color = accent;
    }

    /// <summary>«Стеклянная» панель: тёмная полупрозрачная подложка, тонкая рамка и мягкая тень.</summary>
    public static StyleBoxFlat Glass(float alpha = 0.82f, int radius = 16, float padding = 24f)
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.07f, 0.1f, alpha),
            BorderColor = new Color(1f, 1f, 1f, 0.09f),
            ShadowColor = new Color(0f, 0f, 0f, 0.45f),
            ShadowSize = 20,
        };
        box.SetCornerRadiusAll(radius);
        box.SetBorderWidthAll(1);
        box.SetContentMarginAll(padding);
        return box;
    }

    /// <summary>Рамка ячейки предмета: цвет рамки показывает редкость.</summary>
    public static StyleBoxFlat Slot(Color border, float bgAlpha = 0.05f, int borderWidth = 1)
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(1f, 1f, 1f, bgAlpha),
            BorderColor = border,
        };
        box.SetCornerRadiusAll(Radius);
        box.SetBorderWidthAll(borderWidth);
        box.SetContentMarginAll(6f);
        return box;
    }

    /// <summary>
    /// Виньетка — затемнение по краям экрана. Нарисована маленьким шейдером (программой для видеокарты):
    /// для каждой точки считаем расстояние до центра и чем дальше, тем темнее.
    /// </summary>
    public static ColorRect CreateVignette(float strength, Color? color = null)
    {
        var shader = new Shader
        {
            Code = """
                shader_type canvas_item;
                uniform float strength = 0.5;
                uniform vec3 tint = vec3(0.0);
                void fragment() {
                    vec2 offset = (UV - 0.5) * vec2(1.6, 1.0);
                    float darkness = smoothstep(0.35, 1.0, length(offset));
                    COLOR = vec4(tint, darkness * strength);
                }
                """,
        };
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("strength", strength);
        var tint = color ?? Colors.Black;
        material.SetShaderParameter("tint", new Vector3(tint.R, tint.G, tint.B));

        var vignette = new ColorRect { Material = material, MouseFilter = Control.MouseFilterEnum.Ignore };
        vignette.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return vignette;
    }

    private static StyleBoxFlat ButtonBox(Color background, Color border)
    {
        var box = new StyleBoxFlat { BgColor = background, BorderColor = border };
        box.SetCornerRadiusAll(Radius);
        box.SetBorderWidthAll(1);
        box.ContentMarginLeft = box.ContentMarginRight = 18f;
        box.ContentMarginTop = box.ContentMarginBottom = 10f;
        return box;
    }

    private static Theme Build()
    {
        var theme = new Theme { DefaultFontSize = 18 };

        // ----- Кнопки -----
        var normal = ButtonBox(new Color(1f, 1f, 1f, 0.05f), new Color(1f, 1f, 1f, 0.08f));
        _buttonHover = ButtonBox(new Color(1f, 1f, 1f, 0.1f), new Color(DefaultAccent, 0.55f));
        _buttonPressed = ButtonBox(new Color(DefaultAccent, 0.22f), DefaultAccent);
        var disabled = ButtonBox(new Color(1f, 1f, 1f, 0.02f), new Color(1f, 1f, 1f, 0.03f));

        // Фокус (выбор с клавиатуры) рисуется поверх обычного стиля — только рамка без заливки
        _buttonFocus = new StyleBoxFlat { DrawCenter = false, BorderColor = DefaultAccent };
        _buttonFocus.SetCornerRadiusAll(Radius);
        _buttonFocus.SetBorderWidthAll(2);
        _buttonFocus.SetExpandMarginAll(1f);

        theme.SetStylebox("normal", "Button", normal);
        theme.SetStylebox("hover", "Button", _buttonHover);
        theme.SetStylebox("pressed", "Button", _buttonPressed);
        theme.SetStylebox("hover_pressed", "Button", _buttonPressed);
        theme.SetStylebox("disabled", "Button", disabled);
        theme.SetStylebox("focus", "Button", _buttonFocus);
        theme.SetColor("font_color", "Button", Text);
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetColor("font_focus_color", "Button", Colors.White);
        theme.SetColor("font_pressed_color", "Button", Colors.White);
        theme.SetColor("font_hover_pressed_color", "Button", Colors.White);
        theme.SetColor("font_disabled_color", "Button", new Color(1f, 1f, 1f, 0.3f));
        theme.SetConstant("h_separation", "Button", 12);

        // Переключатель: «включено» и так видно по тумблеру, заливать всю строку акцентом не нужно
        theme.SetStylebox("pressed", "CheckButton", normal);
        theme.SetStylebox("hover_pressed", "CheckButton", _buttonHover);

        // ----- Надписи: вместо толстой обводки — мягкая тень, она аккуратнее -----
        theme.SetColor("font_color", "Label", Text);
        theme.SetColor("font_shadow_color", "Label", new Color(0f, 0f, 0f, 0.6f));
        theme.SetConstant("shadow_offset_x", "Label", 0);
        theme.SetConstant("shadow_offset_y", "Label", 2);
        theme.SetConstant("shadow_outline_size", "Label", 3);
        theme.SetColor("default_color", "RichTextLabel", Text);

        // ----- Панели -----
        theme.SetStylebox("panel", "PanelContainer", Glass());
        theme.SetStylebox("panel", "Panel", Glass());
        theme.SetStylebox("panel", "PopupMenu", Glass(0.95f, 10, 8f));
        theme.SetColor("font_color", "PopupMenu", Text);
        theme.SetColor("font_hover_color", "PopupMenu", Colors.White);
        theme.SetStylebox("hover", "PopupMenu", ButtonBox(new Color(1f, 1f, 1f, 0.1f), new Color(1f, 1f, 1f, 0f)));

        var separator = new StyleBoxLine { Color = new Color(1f, 1f, 1f, 0.1f), Thickness = 1 };
        theme.SetStylebox("separator", "HSeparator", separator);
        theme.SetConstant("separation", "HSeparator", 14);

        // ----- Полоски прогресса: тонкие, со скруглением -----
        var barBackground = new StyleBoxFlat { BgColor = new Color(1f, 1f, 1f, 0.08f) };
        barBackground.SetCornerRadiusAll(6);
        _barFill = new StyleBoxFlat { BgColor = DefaultAccent };
        _barFill.SetCornerRadiusAll(6);
        theme.SetStylebox("background", "ProgressBar", barBackground);
        theme.SetStylebox("fill", "ProgressBar", _barFill);

        // ----- Ползунок громкости -----
        var sliderTrack = new StyleBoxFlat { BgColor = new Color(1f, 1f, 1f, 0.12f) };
        sliderTrack.SetCornerRadiusAll(3);
        sliderTrack.ContentMarginTop = sliderTrack.ContentMarginBottom = 3f;
        _sliderGrabber = new StyleBoxFlat { BgColor = DefaultAccent };
        _sliderGrabber.SetCornerRadiusAll(3);
        _sliderGrabber.ContentMarginTop = _sliderGrabber.ContentMarginBottom = 3f;
        theme.SetStylebox("slider", "HSlider", sliderTrack);
        theme.SetStylebox("grabber_area", "HSlider", _sliderGrabber);
        theme.SetStylebox("grabber_area_highlight", "HSlider", _sliderGrabber);

        return theme;
    }
}
