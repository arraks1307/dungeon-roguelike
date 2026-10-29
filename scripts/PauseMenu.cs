using System;
using Godot;

namespace Roguelike;

/// <summary>
/// Меню паузы. Игра пошаговая, поэтому «пауза» — это просто запрет на ходы,
/// останавливать весь движок не нужно.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    public event Action ResumeRequested;
    public event Action MainMenuRequested;

    private Button _resumeButton;

    public override void _Ready()
    {
        Layer = 10;
        Visible = false;
        var column = Ui.CreateCenteredScreen(this, 0.6f);

        var title = Ui.CreateLabel(48, "Пауза");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", Ui.TitleColor);
        column.AddChild(title);

        _resumeButton = Ui.CreateButton("Продолжить", () => ResumeRequested?.Invoke());
        column.AddChild(_resumeButton);
        column.AddChild(Ui.CreateButton("В главное меню", () => MainMenuRequested?.Invoke()));
    }

    public void Open()
    {
        Visible = true;
        Ui.FocusLater(_resumeButton);
    }
}
