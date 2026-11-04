using System;
using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>Список миров: открыть, удалить или создать новый.</summary>
public partial class WorldPanel : CanvasLayer
{
    public event Action<WorldInfo> WorldChosen;
    public event Action CreateRequested;
    public event Action Closed;

    private VBoxContainer _list;
    private Button _createButton;

    public override void _Ready()
    {
        Layer = 10;
        Visible = false;
        var column = Ui.CreateCenteredScreen(this, 0.75f);
        column.AddChild(Ui.CreateTitle("Миры", 38));

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(820f, 420f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        column.AddChild(scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_list);

        _createButton = Ui.CreateButton("Создать новый мир", () => CreateRequested?.Invoke());
        column.AddChild(_createButton);
        column.AddChild(Ui.CreateButton("Назад", () => Closed?.Invoke()));
    }

    public void Open()
    {
        Refresh();
        Visible = true;
    }

    private void Refresh()
    {
        foreach (Node child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }

        Button first = null;
        foreach (var world in Worlds.LoadAll())
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            _list.AddChild(row);

            var open = Ui.CreateButton(
                $"{world.Name}\n{Worlds.HeroSummary(world)}\n{Difficulties.Get(world.Difficulty).Name}  •  {Worlds.Lengths[world.LengthIndex].Name}  •  {world.ModeName}  •  спусков: {world.Descents}",
                () => WorldChosen?.Invoke(world));
            open.Alignment = HorizontalAlignment.Left;
            open.CustomMinimumSize = new Vector2(660f, 92f);
            open.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            open.AddThemeFontSizeOverride("font_size", 15);
            open.AddThemeColorOverride("font_color", world.Class.Color.Lightened(0.35f));
            row.AddChild(open);
            first ??= open;

            Button remove = null;
            bool armed = false;
            remove = Ui.CreateButton("Удалить", () =>
            {
                // Удаление необратимо, поэтому первое нажатие только спрашивает
                if (!armed)
                {
                    armed = true;
                    remove.Text = "Точно?";
                    return;
                }
                Worlds.Delete(world);
                Refresh();
            });
            remove.CustomMinimumSize = new Vector2(130f, 92f);
            row.AddChild(remove);
        }
        if (first == null)
            _list.AddChild(Ui.CreateMutedLabel(18, "Миров пока нет. Создай первый."));
        Ui.FocusLater(first ?? _createButton);
    }
}

/// <summary>Первый шаг создания: настройки мира.</summary>
public partial class WorldCreatePanel : CanvasLayer
{
    public event Action<WorldInfo> Confirmed;
    public event Action Closed;

    private LineEdit _name;
    private LineEdit _seed;
    private Button _nextButton;
    private WorldInfo _draft = new();

    public override void _Ready()
    {
        Layer = 10;
        Visible = false;
        var column = Ui.CreateCenteredScreen(this, 0.75f);
        column.AddChild(Ui.CreateTitle("Создание мира", 38));
        column.AddChild(Ui.CreateMutedLabel(16, "Эти настройки останутся с миром навсегда."));

        _name = AddField(column, "Название", "Мой мир");
        _seed = AddField(column, "Зерно (пусто — случайное)", "");

        var difficulties = new List<string>();
        foreach (var difficulty in Difficulties.All)
            difficulties.Add(difficulty.Name);
        AddChoice(column, "Сложность", difficulties, 1, index => _draft.Difficulty = Difficulties.All[index].Id);
        AddChoice(column, "Длина подземелья", new List<string> { Worlds.Lengths[0].Name, Worlds.Lengths[1].Name, Worlds.Lengths[2].Name }, 1, index => _draft.LengthIndex = index);
        AddChoice(column, "События на этажах", new List<string>(Worlds.EventNames), 1, index => _draft.EventsIndex = index);
        AddChoice(column, "Если герой погибнет", new List<string> { "Вернётся в лагерь", "Вечная смерть: мир будет удалён" }, 0, index => _draft.Permadeath = index == 1);

        _nextButton = Ui.CreateButton("Дальше: выбор героя", () =>
        {
            Confirmed?.Invoke(new WorldInfo
            {
                Name = string.IsNullOrWhiteSpace(_name.Text) ? "Безымянный мир" : _name.Text.Trim(),
                Seed = Worlds.ParseSeed(_seed.Text),
                Difficulty = _draft.Difficulty,
                LengthIndex = _draft.LengthIndex,
                EventsIndex = _draft.EventsIndex,
                Permadeath = _draft.Permadeath,
            });
        });
        column.AddChild(_nextButton);
        column.AddChild(Ui.CreateButton("Назад", () => Closed?.Invoke()));
    }

    public void Open()
    {
        Visible = true;
        Ui.FocusLater(_nextButton);
    }

    private static LineEdit AddField(Node parent, string title, string value)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var label = Ui.CreateLabel(18, title);
        label.CustomMinimumSize = new Vector2(270f, 0f);
        row.AddChild(label);
        var field = new LineEdit { Text = value, CustomMinimumSize = new Vector2(520f, 42f), MaxLength = 32 };
        row.AddChild(field);
        parent.AddChild(row);
        return field;
    }

    /// <summary>Ряд кнопок-переключателей: ButtonGroup оставляет нажатой ровно одну.</summary>
    private static void AddChoice(Node parent, string title, List<string> options, int selected, Action<int> onChosen)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var label = Ui.CreateLabel(18, title);
        label.CustomMinimumSize = new Vector2(270f, 0f);
        row.AddChild(label);
        var group = new ButtonGroup();
        for (int i = 0; i < options.Count; i++)
        {
            int index = i;
            var toggle = new Button { Text = options[i], ToggleMode = true, ButtonGroup = group, ButtonPressed = i == selected, CustomMinimumSize = new Vector2(0f, 42f) };
            toggle.AddThemeFontSizeOverride("font_size", 15);
            toggle.Pressed += () => onChosen(index);
            row.AddChild(toggle);
        }
        parent.AddChild(row);
    }
}

/// <summary>Последний шаг: очки и черта героя. После подтверждения изменить их нельзя.</summary>
public partial class HeroBuildPanel : CanvasLayer
{
    public event Action Confirmed;
    public event Action Closed;

    private WorldInfo _world;
    private Label _points;
    private Label _title;
    private GridContainer _stats;
    private VBoxContainer _traits;
    private Button _createButton;

    public override void _Ready()
    {
        Layer = 10;
        Visible = false;
        var column = Ui.CreateCenteredScreen(this, 0.75f);
        _title = Ui.CreateLabel(34);
        column.AddChild(_title);
        var warning = Ui.CreateLabel(16, "Этот герой останется с миром навсегда: сменить его или перераспределить очки будет нельзя.");
        warning.AddThemeColorOverride("font_color", new Color(1f, 0.7f, 0.4f));
        column.AddChild(warning);
        _points = Ui.CreateLabel(20);
        _points.AddThemeColorOverride("font_color", new Color(0.6f, 0.85f, 1f));
        column.AddChild(_points);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 32);
        column.AddChild(columns);
        _stats = new GridContainer { Columns = 5 };
        _stats.AddThemeConstantOverride("h_separation", 10);
        _stats.AddThemeConstantOverride("v_separation", 8);
        columns.AddChild(_stats);
        _traits = new VBoxContainer();
        _traits.AddThemeConstantOverride("separation", 6);
        columns.AddChild(_traits);

        _createButton = Ui.CreateButton("Создать мир", () => Confirmed?.Invoke());
        column.AddChild(_createButton);
        column.AddChild(Ui.CreateButton("Назад к выбору героя", () => Closed?.Invoke()));
    }

    public void Open(WorldInfo world)
    {
        _world = world;
        _title.Text = $"Герой мира «{world.Name}»: {world.Class.Name}";
        Refresh();
        Visible = true;
        Ui.FocusLater(_createButton);
    }

    private void Refresh()
    {
        int left = Worlds.PointBudget - Worlds.Spent(_world);
        _points.Text = $"Осталось очков: {left} из {Worlds.PointBudget}";
        foreach (Node list in new Node[] { _stats, _traits })
            foreach (Node child in list.GetChildren())
            {
                list.RemoveChild(child);
                child.QueueFree();
            }

        for (int i = 0; i < Worlds.Stats.Length; i++)
        {
            int index = i;
            var stat = Worlds.Stats[i];
            var name = Ui.CreateLabel(16, $"{stat.Name}: {stat.Effect} ({stat.Cost} оч.)");
            name.CustomMinimumSize = new Vector2(370f, 0f);
            _stats.AddChild(name);
            _stats.AddChild(Step("−", _world.Points[i] > 0, () => Change(index, -1)));
            var value = Ui.CreateLabel(18, _world.Points[i].ToString());
            value.CustomMinimumSize = new Vector2(30f, 0f);
            value.HorizontalAlignment = HorizontalAlignment.Center;
            _stats.AddChild(value);
            _stats.AddChild(Step("+", _world.Points[i] < Worlds.MaxPerStat && left >= stat.Cost, () => Change(index, 1)));
            _stats.AddChild(new Control());
        }

        _traits.AddChild(Ui.CreateMutedLabel(14, "ЧЕРТА ГЕРОЯ"));
        for (int i = 0; i < Worlds.Traits.Length; i++)
        {
            int index = i;
            var trait = Worlds.Traits[i];
            var button = Ui.CreateButton($"{(i == _world.Trait ? "✓ " : "")}{trait.Name}: {trait.Effect}", () =>
            {
                _world.Trait = index;
                Refresh();
            });
            button.Alignment = HorizontalAlignment.Left;
            button.CustomMinimumSize = new Vector2(420f, 38f);
            button.AddThemeFontSizeOverride("font_size", 15);
            _traits.AddChild(button);
        }
    }

    private static Button Step(string text, bool enabled, Action onPressed)
    {
        var button = Ui.CreateButton(text, onPressed);
        button.CustomMinimumSize = new Vector2(44f, 38f);
        button.Disabled = !enabled;
        return button;
    }

    private void Change(int index, int delta)
    {
        _world.Points[index] += delta;
        Refresh();
    }
}
