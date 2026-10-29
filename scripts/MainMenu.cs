using System;
using Godot;

namespace Roguelike;

/// <summary>
/// Главное меню: новая игра, рекорды, выход. Рисуется поверх подземелья-заставки.
/// </summary>
public partial class MainMenu : CanvasLayer
{
    public event Action NewGameRequested;

    private VBoxContainer _mainPage;
    private VBoxContainer _scoresPage;
    private GridContainer _scoresGrid;
    private Button _newGameButton;
    private Button _backButton;

    public override void _Ready()
    {
        Layer = 10; // поверх HUD
        var column = Ui.CreateCenteredScreen(this, 0.35f);

        _mainPage = new VBoxContainer();
        _mainPage.AddThemeConstantOverride("separation", 14);
        column.AddChild(_mainPage);

        var title = Ui.CreateLabel(64, "ПОДЗЕМЕЛЬЕ");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", Ui.TitleColor);
        _mainPage.AddChild(title);

        var subtitle = Ui.CreateLabel(20, "пошаговый 3D-рогалик");
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        _mainPage.AddChild(subtitle);
        _mainPage.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 20f) });

        _newGameButton = Ui.CreateButton("Новая игра", () => NewGameRequested?.Invoke());
        _mainPage.AddChild(_newGameButton);
        _mainPage.AddChild(Ui.CreateButton("Рекорды", ShowScores));
        _mainPage.AddChild(Ui.CreateButton("Выход", () => GetTree().Quit()));

        _scoresPage = new VBoxContainer { Visible = false };
        _scoresPage.AddThemeConstantOverride("separation", 14);
        column.AddChild(_scoresPage);

        var scoresTitle = Ui.CreateLabel(44, "Рекорды");
        scoresTitle.HorizontalAlignment = HorizontalAlignment.Center;
        scoresTitle.AddThemeColorOverride("font_color", Ui.TitleColor);
        _scoresPage.AddChild(scoresTitle);

        // GridContainer раскладывает элементы по строкам из Columns колонок — получается таблица
        _scoresGrid = new GridContainer { Columns = 5 };
        _scoresGrid.AddThemeConstantOverride("h_separation", 32);
        _scoresPage.AddChild(_scoresGrid);

        _backButton = Ui.CreateButton("Назад", ShowMain);
        _scoresPage.AddChild(_backButton);
    }

    public void ShowMain()
    {
        Visible = true;
        _scoresPage.Visible = false;
        _mainPage.Visible = true;
        Ui.FocusLater(_newGameButton);
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
            _scoresGrid.AddChild(Ui.CreateLabel(20, "Пока нет рекордов. Самое время их поставить!"));
        }
        else
        {
            _scoresGrid.Columns = 5;
            foreach (var header in new[] { "#", "Этаж", "Убито", "Уровень", "Дата" })
            {
                var label = Ui.CreateLabel(18, header);
                label.Modulate = new Color(1f, 1f, 1f, 0.6f);
                _scoresGrid.AddChild(label);
            }
            for (int i = 0; i < scores.Count; i++)
            {
                var score = scores[i];
                foreach (var cell in new[] { $"{i + 1}", $"{score.Floor}", $"{score.Kills}", $"{score.Level}", score.Date })
                    _scoresGrid.AddChild(Ui.CreateLabel(20, cell));
            }
        }

        _mainPage.Visible = false;
        _scoresPage.Visible = true;
        Ui.FocusLater(_backButton);
    }
}
