using System;
using Godot;

namespace Roguelike;

/// <summary>
/// Меню паузы. Игра пошаговая, поэтому «пауза» — это просто запрет на ходы,
/// останавливать весь движок не нужно. Здесь же живёт справка по управлению:
/// подсказок на экране во время игры нет, и пропустивший обучение найдёт клавиши тут.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    private const string Controls =
        "WASD / стрелки — ходить и атаковать (шагни во врага)\n" +
        "Q E Z C — ходить по диагонали\n" +
        "Удерживать клавишу — идти, пока не случится что-то важное\n" +
        "Пробел — защитная стойка: половина урона, блок удара заряжает способности\n" +
        "F, G — способности героя\n" +
        "1–5 — применить предмет из первых ячеек сумки\n" +
        "I — инвентарь (Enter — применить или надеть, X — выбросить)\n" +
        "M — скрыть или показать мини-карту\n" +
        "Колёсико мыши — приблизить или отдалить камеру\n" +
        "Esc — пауза\n" +
        "\n" +
        "В сетевой игре:\n" +
        "Enter — чат с отрядом, 6 7 8 — быстрые фразы\n" +
        "Правая кнопка мыши — метка на карте\n" +
        "Шагни в призрака дважды — поднимешь союзника\n" +
        "T и Y в инвентаре — передать вещь и золото союзнику\n" +
        "Призрак: летает сквозь стены, F пугает врага, G лечит союзника";

    public event Action ResumeRequested;
    public event Action SettingsRequested;
    public event Action SaveAndExitRequested;

    private ColorRect _mainScreen;
    private ColorRect _controlsScreen;
    private Button _resumeButton;
    private Button _controlsButton;
    private Button _backButton;
    private Button _exitButton;

    public override void _Ready()
    {
        Layer = 10;
        Visible = false;
        var column = Ui.CreateCenteredScreen(this, 0.6f, out _mainScreen);

        var title = Ui.CreateLabel(48, "Пауза");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", Ui.TitleColor);
        column.AddChild(title);

        _resumeButton = Ui.CreateButton("Продолжить", () => ResumeRequested?.Invoke());
        column.AddChild(_resumeButton);
        _controlsButton = Ui.CreateButton("Управление", ShowControls);
        column.AddChild(_controlsButton);
        column.AddChild(Ui.CreateButton("Настройки", () => SettingsRequested?.Invoke()));
        _exitButton = Ui.CreateButton("Сохранить и выйти в меню", () => SaveAndExitRequested?.Invoke());
        column.AddChild(_exitButton);

        var controls = Ui.CreateCenteredScreen(this, 0.6f, out _controlsScreen);
        _controlsScreen.Visible = false;
        controls.AddChild(Ui.CreateTitle("Управление", 40));
        controls.AddChild(_controlsText = Ui.CreateLabel(18, Controls));
        _backButton = Ui.CreateButton("Назад", () => CloseControls());
        controls.AddChild(_backButton);
    }

    /// <summary>
    /// exitText — надпись на кнопке выхода: она разная в одиночной игре, в сетевой и в обучении.
    /// В сетевой игре пауза не останавливает мир: друзья продолжают ходить.
    /// </summary>
    public void Open(string exitText = "Сохранить и выйти в меню")
    {
        _exitButton.Text = exitText;
        _mainScreen.Visible = true;
        _controlsScreen.Visible = false;
        Visible = true;
        Ui.FocusLater(_resumeButton);
    }

    private Label _controlsText;

    /// <summary>Справка показывает настоящие клавиши игрока, а не исходные.</summary>
    private static string ActualControls() => Controls
        .Replace("WASD", KeyBindings.Walk)
        .Replace("Q E Z C", KeyBindings.Diagonal)
        .Replace("F, G", $"{KeyBindings.Name(GameKey.Skill1)}, {KeyBindings.Name(GameKey.Skill2)}")
        .Replace("I — инвентарь", $"{KeyBindings.Name(GameKey.Inventory)} — инвентарь")
        .Replace("X — выбросить", $"{KeyBindings.Name(GameKey.Drop)} — выбросить")
        .Replace("M — скрыть", $"{KeyBindings.Name(GameKey.Minimap)} — скрыть")
        .Replace("F пугает", $"{KeyBindings.Name(GameKey.Skill1)} пугает")
        .Replace("G лечит", $"{KeyBindings.Name(GameKey.Skill2)} лечит");

    public void ShowControls()
    {
        _controlsText.Text = ActualControls() + "\n\nИзменить клавиши можно в настройках.";        _mainScreen.Visible = false;
        _controlsScreen.Visible = true;
        Ui.FocusLater(_backButton);
    }

    /// <summary>Закрыть справку (Esc). true — справка была открыта, и Esc на этом обработан.</summary>
    public bool CloseControls()
    {
        if (!_controlsScreen.Visible)
            return false;
        _controlsScreen.Visible = false;
        _mainScreen.Visible = true;
        Ui.FocusLater(_controlsButton);
        return true;
    }
}
