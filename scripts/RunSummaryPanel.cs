using System;
using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>
/// Итоги забега: карточка с героем, снаряжением, сделками и наградами.
/// Её можно сохранить картинкой и показать друзьям.
/// </summary>
public partial class RunSummaryPanel : CanvasLayer
{
    private const string Folder = "user://summaries";

    private VBoxContainer _column;
    private Button _saveButton;

    public override void _Ready()
    {
        Layer = 8;
        Visible = false;
        _column = Ui.CreateCenteredScreen(this, 0.82f);
    }

    public void Open(bool victory, string title, string subtitle, List<string> stats, List<Item> gear,
        List<string> pacts, string details, string hint)
    {
        foreach (Node child in _column.GetChildren())
        {
            _column.RemoveChild(child);
            child.QueueFree();
        }

        var heading = Ui.CreateLabel(40, title);
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        heading.AddThemeColorOverride("font_color", victory ? new Color(1f, 0.85f, 0.4f) : new Color(0.95f, 0.4f, 0.35f));
        _column.AddChild(heading);

        var story = Ui.CreateWrappedLabel(17, 880f);
        story.Text = subtitle;
        story.HorizontalAlignment = HorizontalAlignment.Center;
        story.AddThemeColorOverride("font_color", Story.Tint);
        _column.AddChild(story);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 36);
        columns.Alignment = BoxContainer.AlignmentMode.Center;
        _column.AddChild(columns);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(400f, 0f) };
        left.AddThemeConstantOverride("separation", 4);
        columns.AddChild(left);
        left.AddChild(Ui.CreateMutedLabel(14, "ЗАБЕГ"));
        foreach (string line in stats)
            left.AddChild(Ui.CreateLabel(17, line));

        var right = new VBoxContainer { CustomMinimumSize = new Vector2(440f, 0f) };
        right.AddThemeConstantOverride("separation", 6);
        columns.AddChild(right);
        right.AddChild(Ui.CreateMutedLabel(14, "СНАРЯЖЕНИЕ"));
        var icons = new HBoxContainer();
        icons.AddThemeConstantOverride("separation", 10);
        foreach (var item in gear)
            icons.AddChild(new TextureRect { Texture = ItemIcons.Get(item, 64), TooltipText = item.Name });
        if (gear.Count == 0)
            icons.AddChild(Ui.CreateLabel(16, "Ничего: только кулаки и упрямство"));
        right.AddChild(icons);

        right.AddChild(Ui.CreateMutedLabel(14, "СДЕЛКИ С ПОДЗЕМЕЛЬЕМ"));
        var pactLabel = Ui.CreateWrappedLabel(16, 430f);
        pactLabel.Text = pacts.Count == 0 ? "Ни одной. Чистые руки." : string.Join(", ", pacts);
        right.AddChild(pactLabel);

        if (details.Length > 0)
        {
            right.AddChild(Ui.CreateMutedLabel(14, "ИТОГИ"));
            var detailLabel = Ui.CreateWrappedLabel(15, 430f);
            detailLabel.Text = details;
            right.AddChild(detailLabel);
        }

        _saveButton = Ui.CreateButton("Сохранить картинку", SavePicture);
        _saveButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _column.AddChild(_saveButton);

        var footer = Ui.CreateMutedLabel(15, hint);
        footer.HorizontalAlignment = HorizontalAlignment.Center;
        _column.AddChild(footer);

        Visible = true;
        Ui.FocusLater(_saveButton);
    }

    /// <summary>Снимок экрана ложится в папку данных игры; после сохранения папка открывается.</summary>
    private void SavePicture()
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Folder));
        string path = $"{Folder}/run_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        _saveButton.Text = error == Error.Ok ? "Картинка сохранена" : "Не удалось сохранить";
        if (error == Error.Ok)
            OS.ShellOpen(ProjectSettings.GlobalizePath(Folder));
    }
}
