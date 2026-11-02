using System;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Выбор класса перед новым забегом: три карточки рядом.
/// Карточка — кнопка с подробным текстом, поэтому работает и мышью, и стрелками с Enter.
/// </summary>
public partial class ClassSelectPanel : CanvasLayer
{
    public event Action<ClassDef, DifficultyDef> ClassChosen;

    private Button _firstCard;
    private DifficultyDef _difficulty = Difficulties.Normal;

    public override void _Ready()
    {
        Layer = 10;
        Visible = false;
        var column = Ui.CreateCenteredScreen(this, 0.6f);

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
    }

    public void Open()
    {
        Visible = true;
        Ui.FocusLater(_firstCard);
    }

    private Button CreateCard(ClassDef playerClass)
    {
        var start = string.Join(", ", playerClass.StartingItems.Select(item => item.Name).Distinct());
        var skills = string.Join("\n", playerClass.Skills.Select((skill, i) =>
            $"{(i == 0 ? "F" : "G")} — {skill.Name}: {skill.Description} (перезарядка {skill.Cooldown})"));

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
        card.Pressed += () => ClassChosen?.Invoke(playerClass, _difficulty);
        card.MouseEntered += () => card.GrabFocus();
        return card;
    }
}
