using System;
using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>Окно повышения уровня: три случайных улучшения, нужно выбрать одно.</summary>
public partial class LevelUpPanel : CanvasLayer
{
    public event Action<Perk> PerkChosen;

    private Label _title;
    private VBoxContainer _options;

    public override void _Ready()
    {
        Layer = 6;
        Visible = false;
        var column = Ui.CreateCenteredScreen(this, 0.6f);

        _title = Ui.CreateLabel(44);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.AddThemeColorOverride("font_color", Ui.TitleColor);
        column.AddChild(_title);

        var subtitle = Ui.CreateLabel(20, "Выбери улучшение");
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(subtitle);

        _options = new VBoxContainer();
        _options.AddThemeConstantOverride("separation", 10);
        column.AddChild(_options);
    }

    public void Open(int level, List<Perk> perks)
    {
        _title.Text = $"Уровень {level}!";
        foreach (Node child in _options.GetChildren())
        {
            _options.RemoveChild(child);
            child.QueueFree();
        }

        Button first = null;
        foreach (var perk in perks)
        {
            var button = Ui.CreateButton($"{perk.Name} — {perk.Description}", () => PerkChosen?.Invoke(perk));
            button.CustomMinimumSize = new Vector2(560f, 56f);
            button.MouseEntered += () => button.GrabFocus();
            _options.AddChild(button);
            first ??= button;
        }

        Visible = true;
        Ui.FocusLater(first);
    }
}
