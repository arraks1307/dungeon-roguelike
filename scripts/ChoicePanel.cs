using System;
using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>Окно выбора: событие на этаже предлагает несколько вариантов, Esc — отказаться.</summary>
public partial class ChoicePanel : CanvasLayer
{
    public event Action<int> Chosen;
    public event Action Cancelled;

    private Label _title;
    private Label _text;
    private VBoxContainer _options;

    public override void _Ready()
    {
        Layer = 6;
        Visible = false;
        var column = Ui.CreateCenteredScreen(this, 0.6f);
        _title = Ui.CreateLabel(38);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_title);
        _text = Ui.CreateWrappedLabel(18, 760f);
        _text.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_text);
        _options = new VBoxContainer();
        _options.AddThemeConstantOverride("separation", 10);
        column.AddChild(_options);
    }

    public void Open(string title, string text, List<string> options, string leaveText = "Пройти мимо")
    {
        _title.Text = title;
        _text.Text = text;
        foreach (Node child in _options.GetChildren())
        {
            _options.RemoveChild(child);
            child.QueueFree();
        }

        Button first = null;
        for (int i = 0; i < options.Count; i++)
        {
            int index = i;
            var button = Ui.CreateButton(options[i], () => Chosen?.Invoke(index));
            button.CustomMinimumSize = new Vector2(760f, 54f);
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _options.AddChild(button);
            first ??= button;
        }
        var leave = Ui.CreateButton(leaveText, () => Cancelled?.Invoke());
        leave.CustomMinimumSize = new Vector2(760f, 46f);
        _options.AddChild(leave);
        first ??= leave;

        Visible = true;
        Ui.FocusLater(first);
    }
}
