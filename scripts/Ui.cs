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
    /// Кнопка-предмет: иконка, текст цвета редкости. onFocused вызывается, когда кнопку выбрали
    /// стрелками или навели мышью, — так окна показывают подробности о предмете.
    /// </summary>
    public static Button CreateItemButton(Item item, string text, Action onPressed, Action onFocused)
    {
        var button = CreateButton(text, onPressed);
        button.Icon = ItemIcons.Get(item);
        button.Alignment = HorizontalAlignment.Left;
        button.CustomMinimumSize = new Vector2(380f, 38f);
        button.AddThemeFontSizeOverride("font_size", 17);
        button.AddThemeColorOverride("font_color", item.RarityColor);
        button.AddThemeColorOverride("font_focus_color", item.RarityColor.Lightened(0.3f));
        button.AddThemeColorOverride("font_hover_color", item.RarityColor.Lightened(0.3f));
        button.FocusEntered += onFocused;
        button.MouseEntered += () => button.GrabFocus();
        return button;
    }

    /// <summary>Многострочная надпись с переносом слов для описаний.</summary>
    public static Label CreateWrappedLabel(int fontSize, float width)
    {
        var label = CreateLabel(fontSize);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(width, 0f);
        return label;
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
    public static void FocusLater(Button button) =>
        Callable.From(() =>
        {
            // За время ожидания окно могло пересоздать кнопки — тогда эта уже удалена из сцены
            if (GodotObject.IsInstanceValid(button) && button.IsInsideTree() && button.IsVisibleInTree())
                button.GrabFocus();
        }).CallDeferred();
}
