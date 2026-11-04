using System;
using Godot;

namespace Roguelike;

/// <summary>
/// Главное меню в минималистичном стиле: подземелье-заставка видно справа, а слева — затемнённая
/// полоса с крупным заголовком, акцентной чертой и колонкой кнопок. По краям экрана — виньетка.
/// </summary>
public partial class MainMenu : CanvasLayer
{
    public const string Version = "v2.0";

    private static readonly Color VictoryColor = new(1f, 0.82f, 0.4f);

    public event Action NewGameRequested;
    public event Action ContinueRequested;
    public event Action SettingsRequested;
    public event Action NetworkRequested;
    public event Action LegacyRequested;

    private Button _continueButton;
    private VBoxContainer _mainPage;
    private VBoxContainer _scoresPage;
    private GridContainer _scoresGrid;
    private Button _newGameButton;
    private Button _backButton;
    private Label _title;
    private Tween _titleTween;

    public override void _Ready()
    {
        Layer = 10; // поверх HUD

        var root = new Control { Theme = UiTheme.Main, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        root.AddChild(CreateSideShade());
        root.AddChild(UiTheme.CreateVignette(0.55f));

        // Левая колонка: отступ 120 пикселей от края, содержимое по центру по вертикали
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 120);
        margin.AddThemeConstantOverride("margin_top", 60);
        margin.AddThemeConstantOverride("margin_bottom", 60);
        root.AddChild(margin);

        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddChild(column);

        _mainPage = new VBoxContainer();
        _mainPage.AddThemeConstantOverride("separation", 12);
        column.AddChild(_mainPage);

        _title = Ui.CreateLabel(80, "ПОДЗЕМЕЛЬЕ");
        _mainPage.AddChild(_title);
        _mainPage.AddChild(Ui.CreateMutedLabel(20, "пошаговый 3D-рогалик"));
        _mainPage.AddChild(Ui.CreateAccentBar(64f));
        _mainPage.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 28f) });

        _continueButton = CreateMenuButton("Продолжить", () => ContinueRequested?.Invoke());
        _newGameButton = CreateMenuButton("Одиночная игра", () => NewGameRequested?.Invoke());
        CreateMenuButton("Сетевая игра", () => NetworkRequested?.Invoke());
        CreateMenuButton("Рекорды", ShowScores);
        CreateMenuButton("Настройки", () => SettingsRequested?.Invoke());
        CreateMenuButton("Выход", () => GetTree().Quit());

        // Страница рекордов — в «стеклянной» карточке на месте кнопок
        _scoresPage = new VBoxContainer { Visible = false };
        _scoresPage.AddThemeConstantOverride("separation", 16);
        column.AddChild(_scoresPage);
        _scoresPage.AddChild(Ui.CreateTitle("Рекорды", 44));

        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        _scoresPage.AddChild(card);
        // GridContainer раскладывает элементы по строкам из Columns колонок — получается таблица
        _scoresGrid = new GridContainer { Columns = 7 };
        _scoresGrid.AddThemeConstantOverride("h_separation", 28);
        _scoresGrid.AddThemeConstantOverride("v_separation", 8);
        card.AddChild(_scoresGrid);

        _backButton = Ui.CreateButton("Назад", ShowMain);
        _backButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _scoresPage.AddChild(_backButton);

        // Подпись с версией в левом нижнем углу
        var footer = Ui.CreateMutedLabel(14, $"{Version}  •  Godot 4.7  •  C#");
        footer.AnchorTop = footer.AnchorBottom = 1f;
        footer.OffsetLeft = 120f;
        footer.OffsetRight = 600f;
        footer.OffsetTop = -44f;
        root.AddChild(footer);
    }

    private Button CreateMenuButton(string text, Action onPressed)
    {
        var button = Ui.CreateButton(text, onPressed);
        button.Alignment = HorizontalAlignment.Left;
        button.CustomMinimumSize = new Vector2(340f, 52f);
        button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _mainPage.AddChild(button);
        return button;
    }

    /// <summary>
    /// Затемнение слева, плавно исчезающее к середине экрана: градиентная текстура,
    /// растянутая на весь экран. Так текст читается, а заставка справа остаётся видна.
    /// </summary>
    private static TextureRect CreateSideShade()
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0.02f, 0.03f, 0.05f, 0.92f));
        gradient.SetColor(1, new Color(0.02f, 0.03f, 0.05f, 0f));
        gradient.SetOffset(1, 0.6f);

        var shade = new TextureRect
        {
            Texture = new GradientTexture2D { Gradient = gradient, FillFrom = Vector2.Zero, FillTo = new Vector2(1f, 0f), Width = 256, Height = 4 },
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return shade;
    }

    public void ShowMain()
    {
        Visible = true;
        _scoresPage.Visible = false;
        _mainPage.Visible = true;
        // «Продолжить» есть, только если есть сохранённый забег
        _continueButton.Visible = false; // спуски продолжаются из лагеря своего мира
        Ui.FocusLater(_continueButton.Visible ? _continueButton : _newGameButton);
        StartTitleBreathing();
    }

    /// <summary>Заголовок медленно «дышит» — едва заметно меняет яркость. Tween с SetLoops повторяется бесконечно.</summary>
    private void StartTitleBreathing()
    {
        _titleTween?.Kill();
        _title.Modulate = Colors.White;
        if (Settings.AnimationScale == 0f)
            return;
        _titleTween = CreateTween().SetLoops();
        _titleTween.TweenProperty(_title, "modulate", new Color(0.82f, 0.86f, 1f), 2.2)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _titleTween.TweenProperty(_title, "modulate", Colors.White, 2.2)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    /// <summary>Esc в меню: со страницы рекордов возвращаемся на главную.</summary>
    public void GoBack()
    {
        if (_scoresPage.Visible)
            ShowMain();
    }

    public void ShowScores()
    {
        foreach (Node child in _scoresGrid.GetChildren())
        {
            _scoresGrid.RemoveChild(child); // убираем сразу, QueueFree удалит только в конце кадра
            child.QueueFree();
        }

        var scores = HighScores.Load();
        if (scores.Count == 0)
        {
            _scoresGrid.Columns = 1;
            _scoresGrid.AddChild(Ui.CreateMutedLabel(18, "Пока нет рекордов. Самое время их поставить!"));
        }
        else
        {
            _scoresGrid.Columns = 7;
            foreach (var header in new[] { "#", "Герой", "Сложность", "Этаж", "Убито", "Уровень", "Дата" })
                _scoresGrid.AddChild(Ui.CreateMutedLabel(15, header));
            for (int i = 0; i < scores.Count; i++)
            {
                var score = scores[i];
                // Победные забеги отмечаем звёздочкой и золотым цветом
                string floor = score.Victory ? $"★ {score.Floor}" : $"{score.Floor}";
                foreach (var cell in new[] { $"{i + 1}", score.Class ?? "—", score.Difficulty ?? "—", floor,
                             $"{score.Kills}", $"{score.Level}", score.Date })
                {
                    var label = Ui.CreateLabel(18, cell);
                    if (score.Victory)
                        label.AddThemeColorOverride("font_color", VictoryColor);
                    _scoresGrid.AddChild(label);
                }
            }
        }

        _mainPage.Visible = false;
        _scoresPage.Visible = true;
        Ui.FocusLater(_backButton);
    }
}
