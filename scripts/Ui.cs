using System;
using Godot;

namespace Roguelike;

/// <summary>
/// Общие функции для создания элементов интерфейса в одном стиле.
/// </summary>
public static class Ui
{
    public static readonly Color TitleColor = new(1f, 0.8f, 0.3f);

    public static Label CreateLabel(int fontSize, string text = "")
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeConstantOverride("outline_size", 6);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        return label;
    }

    public static Button CreateButton(string text, Action onPressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(300f, 52f) };
        button.AddThemeFontSizeOverride("font_size", 22);
        button.Pressed += onPressed;
        return button;
    }

    /// <summary>
    /// Полупрозрачное затемнение на весь экран с колонкой по центру.
    /// Возвращает колонку, в которую добавляются заголовки и кнопки.
    /// </summary>
    public static VBoxContainer CreateCenteredScreen(Node parent, float dimAlpha)
    {
        var background = new ColorRect { Color = new Color(0f, 0f, 0f, dimAlpha) };
        background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        parent.AddChild(background);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        background.AddChild(center);

        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 14);
        center.AddChild(column);
        return column;
    }

    /// <summary>
    /// Фокус на кнопке, чтобы меню работало с клавиатуры (стрелки + Enter).
    /// CallDeferred откладывает вызов до конца кадра, когда кнопка уже видима.
    /// </summary>
    public static void FocusLater(Button button) => button.CallDeferred(Control.MethodName.GrabFocus);
}
