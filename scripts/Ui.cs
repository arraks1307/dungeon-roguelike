using System;
using Godot;

namespace Roguelike;

/// <summary>
/// Общие функции для создания элементов интерфейса в одном стиле (стили — в UiTheme).
/// </summary>
public static class Ui
{
    public static readonly Color TitleColor = UiTheme.Text;

    /// <summary>Как проиграть звук интерфейса. Назначает GameManager, чтобы Ui не зависел от SoundManager.</summary>
    public static Action<Sound> PlaySound { get; set; }

    public static Label CreateLabel(int fontSize, string text = "")
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    /// <summary>Приглушённая подпись: заголовки разделов, подсказки.</summary>
    public static Label CreateMutedLabel(int fontSize, string text = "")
    {
        var label = CreateLabel(fontSize, text);
        label.AddThemeColorOverride("font_color", UiTheme.TextMuted);
        return label;
    }

    public static Button CreateButton(string text, Action onPressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(300f, 50f) };
        button.AddThemeFontSizeOverride("font_size", 20);
        button.Pressed += onPressed;
        AddFeedback(button);
        return button;
    }

    /// <summary>
    /// Отклик кнопки: тихий щелчок при выборе (мышью или стрелками) и звук нажатия,
    /// а при наведении мыши кнопка сразу получает фокус — так мышь и клавиатура не спорят.
    /// </summary>
    public static void AddFeedback(Button button)
    {
        button.MouseEntered += () =>
        {
            if (button.FocusMode != Control.FocusModeEnum.None && !button.Disabled)
                button.GrabFocus();
        };
        button.FocusEntered += () => PlaySound?.Invoke(Sound.UiHover);
        button.Pressed += () => PlaySound?.Invoke(Sound.UiClick);
    }

    /// <summary>
    /// Кнопка-предмет (строка списка): иконка, текст цвета редкости. onFocused вызывается, когда кнопку
    /// выбрали стрелками или навели мышью, — так окна показывают подробности о предмете.
    /// </summary>
    public static Button CreateItemButton(Item item, string text, Action onPressed, Action onFocused)
    {
        var button = CreateButton(text, onPressed);
        button.Icon = ItemIcons.Get(item, 48);
        button.Alignment = HorizontalAlignment.Left;
        button.CustomMinimumSize = new Vector2(380f, 58f);
        button.AddThemeFontSizeOverride("font_size", 17);
        button.AddThemeColorOverride("font_color", item.RarityColor);
        button.AddThemeColorOverride("font_focus_color", item.RarityColor.Lightened(0.3f));
        button.AddThemeColorOverride("font_hover_color", item.RarityColor.Lightened(0.3f));
        button.FocusEntered += onFocused;
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

    /// <summary>Текст с цветной разметкой (BBCode: [color=#ff0000]красный[/color]) — для описаний предметов.</summary>
    public static RichTextLabel CreateRichText(float width)
    {
        var text = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            CustomMinimumSize = new Vector2(width, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        text.AddThemeFontSizeOverride("normal_font_size", 16);
        return text;
    }

    /// <summary>Заголовок окна: крупный светлый текст и короткая акцентная черта под ним.</summary>
    public static Control CreateTitle(string text, int fontSize = 34)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        box.AddChild(CreateLabel(fontSize, text));
        box.AddChild(CreateAccentBar());
        return box;
    }

    /// <summary>Короткая полоска акцентного цвета — главный «декоративный» элемент минималистичного стиля.</summary>
    public static ColorRect CreateAccentBar(float width = 48f)
    {
        var bar = new ColorRect
        {
            Color = UiTheme.Accent,
            CustomMinimumSize = new Vector2(width, 3f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        UiTheme.RegisterAccentBar(bar);
        return bar;
    }

    /// <summary>
    /// Окно по центру экрана: полупрозрачное затемнение и «стеклянная» карточка.
    /// Возвращает колонку внутри карточки, в которую добавляются заголовки и кнопки.
    /// </summary>
    public static VBoxContainer CreateCenteredScreen(Node parent, float dimAlpha) =>
        CreateCenteredScreen(parent, dimAlpha, out _);

    /// <summary>То же самое, но ещё отдаёт фон — через него весь экран можно прятать и показывать.</summary>
    public static VBoxContainer CreateCenteredScreen(Node parent, float dimAlpha, out ColorRect background)
    {
        background = new ColorRect { Color = new Color(0.02f, 0.03f, 0.05f, dimAlpha), Theme = UiTheme.Main };
        background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        parent.AddChild(background);

        // Окно появляется плавно: и когда показывают весь слой, и когда переключают экран внутри него
        var screen = background;
        screen.VisibilityChanged += () => FadeIn(screen);
        if (parent is CanvasLayer layer)
            layer.VisibilityChanged += () => FadeIn(screen);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        background.AddChild(center);

        var card = new PanelContainer();
        center.AddChild(card);

        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 14);
        card.AddChild(column);
        return column;
    }

    private static void FadeIn(Control screen)
    {
        if (!screen.IsInsideTree() || !screen.IsVisibleInTree() || Settings.AnimationScale == 0f)
            return;
        screen.Modulate = Colors.Transparent;
        screen.CreateTween().TweenProperty(screen, "modulate", Colors.White, 0.14f * Settings.AnimationScale)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
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
