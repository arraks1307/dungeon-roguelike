using System;
using Godot;

namespace Roguelike;

/// <summary>
/// Окно перед началом игры: пройти обучение или пропустить. В сетевой игре то же окно
/// показывает ожидание, пока остальные игроки не закончат своё обучение.
/// </summary>
public partial class TutorialPanel : CanvasLayer
{
    private Label _title;
    private Label _text;
    private Button _primaryButton;
    private Button _secondaryButton;
    private Action _onPrimary;
    private Action _onSecondary;

    public override void _Ready()
    {
        Layer = 10;
        Visible = false;
        var column = Ui.CreateCenteredScreen(this, 0.75f);

        _title = Ui.CreateLabel(40);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_title);

        _text = Ui.CreateWrappedLabel(19, 620f);
        _text.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_text);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 8f) });

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 12);
        column.AddChild(buttons);
        _primaryButton = Ui.CreateButton("", () => _onPrimary?.Invoke());
        buttons.AddChild(_primaryButton);
        _secondaryButton = Ui.CreateButton("", () => _onSecondary?.Invoke());
        buttons.AddChild(_secondaryButton);
    }

    public void OpenOffer(Action onTake, Action onSkip)
    {
        _title.Text = "Обучение";
        _text.Text = "Короткий уровень за пару минут: ходьба, предметы, бой и способности. " +
                     "Погибнуть в нём нельзя, а прогресс в забег не переносится.";
        _primaryButton.Text = "Пройти обучение";
        _primaryButton.Visible = true;
        _secondaryButton.Text = "Пропустить";
        _onPrimary = onTake;
        _onSecondary = onSkip;
        Visible = true;
        Ui.FocusLater(_primaryButton);
    }

    public void OpenWaiting(Action onLeave)
    {
        _title.Text = "Почти готово";
        _text.Text = "Ждём, пока остальные игроки закончат обучение.";
        _primaryButton.Visible = false;
        _secondaryButton.Text = "Выйти в меню";
        _onPrimary = null;
        _onSecondary = onLeave;
        Visible = true;
        Ui.FocusLater(_secondaryButton);
    }

    /// <summary>Esc в этом окне — то же, что вторая кнопка: пропустить или выйти.</summary>
    public void Cancel() => _onSecondary?.Invoke();
}
