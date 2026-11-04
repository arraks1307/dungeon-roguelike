using System;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Выбор класса перед новым забегом: три карточки рядом.
/// Карточка — кнопка с подробным текстом, поэтому работает и мышью, и стрелками с Enter.
/// После выбора карточки показывается экран подтверждения, чтобы случайный клик не начал забег.
/// </summary>
public partial class ClassSelectPanel : CanvasLayer
{
    public event Action<ClassDef, DifficultyDef> ClassChosen;

    private Button _firstCard;
    private DifficultyDef _difficulty = Difficulties.Normal;

    // Два «экрана» внутри одного окна: выбор героя и подтверждение
    private ColorRect _selectScreen;
    private ColorRect _confirmScreen;
    private Label _confirmText;
    private Button _confirmButton;
    private ClassDef _pendingClass;
    private Button _pendingCard;

    /// <summary>Сейчас открыт экран подтверждения (Esc должен вернуть к карточкам, а не в меню).</summary>
    public bool IsConfirming => _confirmScreen.Visible;

    public override void _Ready()
    {
        Layer = 10;
        Visible = false;
        var column = Ui.CreateCenteredScreen(this, 0.6f, out _selectScreen);

        var title = Ui.CreateLabel(44, "Выбери героя");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", Ui.TitleColor);
        column.AddChild(title);

        var cards = new HBoxContainer();
        cards.AddThemeConstantOverride("separation", 20);
        column.AddChild(cards);

        foreach (var playerClass in ClassCatalog.All)
        {
            var card = CreateCard(playerClass);
            cards.AddChild(card);
            _firstCard ??= card;
        }

        // Сложность: три кнопки-переключателя. ButtonGroup делает так, что нажата всегда ровно одна
        var difficultyRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        difficultyRow.AddThemeConstantOverride("separation", 12);
        column.AddChild(difficultyRow);
        difficultyRow.AddChild(Ui.CreateLabel(20, "Сложность:"));
        var group = new ButtonGroup();
        foreach (var difficulty in Difficulties.All)
        {
            var toggle = new Button
            {
                Text = difficulty.Name,
                ToggleMode = true,
                ButtonGroup = group,
                ButtonPressed = difficulty == _difficulty,
                CustomMinimumSize = new Vector2(150f, 40f),
            };
            toggle.AddThemeFontSizeOverride("font_size", 18);
            toggle.Pressed += () => _difficulty = difficulty;
            difficultyRow.AddChild(toggle);
        }

        var hint = Ui.CreateLabel(15, "Выбери сложность, затем героя (клик или стрелки и Enter)  •  Esc — назад");
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Modulate = new Color(1f, 1f, 1f, 0.6f);
        column.AddChild(hint);

        CreateConfirmScreen();
    }

    private void CreateConfirmScreen()
    {
        var column = Ui.CreateCenteredScreen(this, 0.75f, out _confirmScreen);
        _confirmScreen.Visible = false;

        var title = Ui.CreateLabel(40, "Начать забег?");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", Ui.TitleColor);
        column.AddChild(title);

        _confirmText = Ui.CreateLabel(22);
        _confirmText.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_confirmText);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 10f) });

        _confirmButton = Ui.CreateButton("Начать", () => ClassChosen?.Invoke(_pendingClass, _difficulty));
        column.AddChild(_confirmButton);
        column.AddChild(Ui.CreateButton("Назад к выбору", CancelConfirm));
    }

    public void Open()
    {
        Visible = true;
        _selectScreen.Visible = true;
        _confirmScreen.Visible = false;
        Ui.FocusLater(_firstCard);
    }

    /// <summary>Показать подтверждение для выбранного класса (вызывается кнопкой-карточкой).</summary>
    public void AskConfirm(ClassDef playerClass, Button card = null)
    {
        _pendingClass = playerClass;
        _pendingCard = card;
        _confirmText.Text = $"{playerClass.Name}  •  сложность: {_difficulty.Name.ToLower()}";
        _confirmText.AddThemeColorOverride("font_color", playerClass.Color.Lightened(0.4f));

        // Прячем карточки целиком, чтобы стрелки не уводили фокус на них из окна подтверждения
        _selectScreen.Visible = false;
        _confirmScreen.Visible = true;
        Ui.FocusLater(_confirmButton);
    }

    /// <summary>Вернуться к карточкам; фокус — на ту, которую выбирали.</summary>
    public void CancelConfirm()
    {
        _confirmScreen.Visible = false;
        _selectScreen.Visible = true;
        Ui.FocusLater(_pendingCard ?? _firstCard);
    }

    private Button CreateCard(ClassDef playerClass)
    {
        var start = string.Join(", ", playerClass.StartingItems.Select(item => item.Name).Distinct());
        var skills = string.Join("\n", playerClass.Skills.Select((skill, i) =>
            $"{(i == 0 ? "F" : "G")} — {skill.Name}: {skill.Description} ({(skill.Cooldown == 0 ? "без зарядки" : $"заряжается за {ClassCatalog.Attacks(skill.Cooldown)}")})"));

        var card = new Button
        {
            CustomMinimumSize = new Vector2(330f, 340f),
            Text = $"{playerClass.Name}\n\n{playerClass.Description}\n\n" +
                   $"Здоровье: {playerClass.MaxHp}   Атака: {playerClass.Attack}\n" +
                   $"С собой: {start}\n\n{skills}",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Alignment = HorizontalAlignment.Left,
            VerticalIconAlignment = VerticalAlignment.Top,
        };
        card.AddThemeFontSizeOverride("font_size", 17);
        card.AddThemeColorOverride("font_color", playerClass.Color.Lightened(0.35f));
        card.AddThemeColorOverride("font_focus_color", playerClass.Color.Lightened(0.6f));
        card.AddThemeColorOverride("font_hover_color", playerClass.Color.Lightened(0.6f));
        card.Pressed += () => AskConfirm(playerClass, card);
        card.MouseEntered += () => card.GrabFocus();
        return card;
    }
}
