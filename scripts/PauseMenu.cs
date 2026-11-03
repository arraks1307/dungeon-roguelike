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
    public event Action SettingsRequested;
    public event Action SaveAndExitRequested;

    private Button _resumeButton;
    private Button _exitButton;

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
        column.AddChild(Ui.CreateButton("Настройки", () => SettingsRequested?.Invoke()));
        _exitButton = Ui.CreateButton("Сохранить и выйти в меню", () => SaveAndExitRequested?.Invoke());
        column.AddChild(_exitButton);
    }

    /// <summary>В сетевой игре пауза не останавливает мир (друзья продолжают ходить), а сохранения нет.</summary>
    public void Open(bool network = false)
    {
        _exitButton.Text = network ? "Покинуть отряд и выйти в меню" : "Сохранить и выйти в меню";
        Visible = true;
        Ui.FocusLater(_resumeButton);
    }
}
