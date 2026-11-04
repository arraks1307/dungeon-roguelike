using System;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Меню «Наследие»: на осколки покупаются постоянные бонусы, рядом — достижения и бестиарий
/// (описание врага появляется, когда встретишь его хотя бы раз).
/// </summary>
public partial class LegacyPanel : CanvasLayer
{
    public event Action Closed;

    private Label _shards;
    private VBoxContainer _upgrades;
    private VBoxContainer _achievements;
    private VBoxContainer _bestiary;
    private Button _backButton;

    public override void _Ready()
    {
        Layer = 10;
        Visible = false;
        var column = Ui.CreateCenteredScreen(this, 0.75f);

        var header = new HBoxContainer();
        column.AddChild(header);
        header.AddChild(Ui.CreateTitle("Наследие", 38));
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _shards = Ui.CreateLabel(20);
        _shards.AddThemeColorOverride("font_color", new Color(0.6f, 0.85f, 1f));
        _shards.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        header.AddChild(_shards);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 20);
        column.AddChild(columns);
        _upgrades = CreateColumn(columns, "ПОСТОЯННЫЕ БОНУСЫ (ОДИНОЧНАЯ ИГРА)", 340f);
        _achievements = CreateColumn(columns, "ДОСТИЖЕНИЯ", 340f);
        _bestiary = CreateColumn(columns, "БЕСТИАРИЙ", 380f);

        _backButton = Ui.CreateButton("Назад", () => Closed?.Invoke());
        _backButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        column.AddChild(_backButton);
    }

    private static VBoxContainer CreateColumn(Node parent, string title, float width)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        parent.AddChild(box);
        box.AddChild(Ui.CreateMutedLabel(14, title));
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(width, 430f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        box.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(list);
        return list;
    }

    public void Open()
    {
        Refresh();
        Visible = true;
        Ui.FocusLater(_backButton);
    }

    private void Refresh()
    {
        var profile = Profile.Current;
        _shards.Text = $"Осколки: {profile.Shards}";
        Clear(_upgrades);
        Clear(_achievements);
        Clear(_bestiary);

        foreach (var upgrade in Profile.Upgrades)
        {
            int level = Profile.Level(upgrade.Id);
            bool full = level >= upgrade.MaxLevel;
            string cost = full ? "максимум" : $"{Profile.Price(upgrade)} осколков";
            var button = Ui.CreateButton($"{upgrade.Name}  {level}/{upgrade.MaxLevel}\n{upgrade.Description}  •  {cost}", () =>
            {
                if (Profile.TryBuy(upgrade))
                    Refresh();
            });
            button.CustomMinimumSize = new Vector2(320f, 64f);
            button.Alignment = HorizontalAlignment.Left;
            button.AddThemeFontSizeOverride("font_size", 14);
            button.Disabled = full || profile.Shards < Profile.Price(upgrade);
            _upgrades.AddChild(button);
        }

        foreach (var achievement in Profile.Achievements)
        {
            bool done = profile.Achievements.Contains(achievement.Id);
            var label = Ui.CreateWrappedLabel(15, 320f);
            label.Text = $"{(done ? "✓ " : "")}{achievement.Name}\n{achievement.Description}  •  +{achievement.Reward}";
            label.Modulate = done ? new Color(0.65f, 1f, 0.7f) : new Color(1f, 1f, 1f, 0.55f);
            _achievements.AddChild(label);
        }

        foreach (var type in Enemy.Types.Concat(Enemy.Bosses))
        {
            bool seen = profile.Seen.Contains(type.Name);
            var label = Ui.CreateWrappedLabel(15, 355f);
            label.Text = seen ? $"{type.Name}\n{Enemy.Describe(type)}" : "???";
            if (seen)
                label.AddThemeColorOverride("font_color", type.Color.Lightened(0.4f));
            else
                label.Modulate = new Color(1f, 1f, 1f, 0.4f);
            _bestiary.AddChild(label);
        }
    }

    private static void Clear(Node list)
    {
        foreach (Node child in list.GetChildren())
        {
            list.RemoveChild(child);
            child.QueueFree();
        }
    }
}
