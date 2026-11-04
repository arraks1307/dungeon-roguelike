using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>Действие, которому можно назначить клавишу.</summary>
public enum GameKey
{
    Up, Down, Left, Right, UpLeft, UpRight, DownLeft, DownRight, Wait,
    Skill1, Skill2, Inventory, Minimap, Drop, GiveItem, GiveGold,
    Slot1, Slot2, Slot3, Slot4, Slot5,
}

/// <summary>
/// Назначения клавиш. Хранятся в настройках как «действие → код клавиши».
/// Стрелки, цифровой блок, Esc и Enter закреплены и работают всегда: так управление нельзя сломать насовсем.
/// </summary>
public static class KeyBindings
{
    private static readonly Dictionary<GameKey, Key> Defaults = new()
    {
        [GameKey.Up] = Key.W, [GameKey.Down] = Key.S, [GameKey.Left] = Key.A, [GameKey.Right] = Key.D,
        [GameKey.UpLeft] = Key.Q, [GameKey.UpRight] = Key.E, [GameKey.DownLeft] = Key.Z, [GameKey.DownRight] = Key.C,
        [GameKey.Wait] = Key.Space, [GameKey.Skill1] = Key.F, [GameKey.Skill2] = Key.G,
        [GameKey.Inventory] = Key.I, [GameKey.Minimap] = Key.M, [GameKey.Drop] = Key.X,
        [GameKey.GiveItem] = Key.T, [GameKey.GiveGold] = Key.Y,
        [GameKey.Slot1] = Key.Key1, [GameKey.Slot2] = Key.Key2, [GameKey.Slot3] = Key.Key3,
        [GameKey.Slot4] = Key.Key4, [GameKey.Slot5] = Key.Key5,
    };

    public static readonly (GameKey Action, string Name)[] Actions =
    {
        (GameKey.Up, "Шаг вверх"), (GameKey.Down, "Шаг вниз"), (GameKey.Left, "Шаг влево"), (GameKey.Right, "Шаг вправо"),
        (GameKey.UpLeft, "Шаг вверх-влево"), (GameKey.UpRight, "Шаг вверх-вправо"),
        (GameKey.DownLeft, "Шаг вниз-влево"), (GameKey.DownRight, "Шаг вниз-вправо"),
        (GameKey.Wait, "Пропустить ход"), (GameKey.Skill1, "Первая способность"), (GameKey.Skill2, "Вторая способность"),
        (GameKey.Inventory, "Инвентарь"), (GameKey.Minimap, "Мини-карта"), (GameKey.Drop, "Выбросить предмет"),
        (GameKey.GiveItem, "Передать предмет"), (GameKey.GiveGold, "Передать золото"),
        (GameKey.Slot1, "Ячейка 1"), (GameKey.Slot2, "Ячейка 2"), (GameKey.Slot3, "Ячейка 3"),
        (GameKey.Slot4, "Ячейка 4"), (GameKey.Slot5, "Ячейка 5"),
    };

    public static Key Get(GameKey action) =>
        Settings.Current.Keys.TryGetValue(action.ToString(), out long code) ? (Key)code : Defaults[action];

    /// <summary>Какое действие висит на клавише; null — никакое.</summary>
    public static GameKey? Find(Key key)
    {
        foreach (var action in Enum.GetValues<GameKey>())
            if (Get(action) == key)
                return action;
        return null;
    }

    /// <summary>Эти клавиши нельзя занять: на них держится меню и запасное управление.</summary>
    public static bool IsReserved(Key key) =>
        key is Key.Escape or Key.Enter or Key.KpEnter or Key.Tab or Key.R or Key.Up or Key.Down or Key.Left or Key.Right
        || (key >= Key.Kp0 && key <= Key.Kp9);

    /// <summary>Назначить клавишу. Если она занята, другое действие получает старую клавишу этого: обмен.</summary>
    public static void Set(GameKey action, Key key)
    {
        var previous = Get(action);
        var other = Find(key);
        if (other.HasValue && other != action)
            Settings.Current.Keys[other.Value.ToString()] = (long)previous;
        Settings.Current.Keys[action.ToString()] = (long)key;
    }

    public static void Reset() => Settings.Current.Keys.Clear();

    public static string Name(GameKey action) => OS.GetKeycodeString(Get(action));

    public static string Walk => string.Join("", new[] { GameKey.Up, GameKey.Left, GameKey.Down, GameKey.Right }.Select(Name));

    public static string Diagonal => string.Join(" ", new[] { GameKey.UpLeft, GameKey.UpRight, GameKey.DownLeft, GameKey.DownRight }.Select(Name));
}

/// <summary>Экран настройки клавиш: выбираешь действие и нажимаешь новую клавишу.</summary>
public partial class KeyBindPanel : CanvasLayer
{
    private GridContainer _grid;
    private Label _status;
    private Button _backButton;
    private GameKey? _waiting;

    public override void _Ready()
    {
        Layer = 21; // поверх настроек
        Visible = false;
        var column = Ui.CreateCenteredScreen(this, 0.85f);
        column.AddChild(Ui.CreateTitle("Управление", 38));
        _status = Ui.CreateMutedLabel(16);
        column.AddChild(_status);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(860f, 520f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        column.AddChild(scroll);
        _grid = new GridContainer { Columns = 4 };
        _grid.AddThemeConstantOverride("h_separation", 14);
        _grid.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(_grid);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 12);
        column.AddChild(buttons);
        buttons.AddChild(Ui.CreateButton("Сбросить всё", () =>
        {
            KeyBindings.Reset();
            Settings.Save();
            Refresh("Клавиши возвращены к исходным.");
        }));
        _backButton = Ui.CreateButton("Назад", Close);
        buttons.AddChild(_backButton);
    }

    public void Open()
    {
        Refresh(DefaultHint);
        Visible = true;
        Ui.FocusLater(_backButton);
    }

    private const string DefaultHint = "Нажми на действие, затем новую клавишу. Стрелки и цифровой блок работают всегда.";

    private void Close()
    {
        _waiting = null;
        Visible = false;
    }

    private void Refresh(string status)
    {
        _status.Text = status;
        foreach (Node child in _grid.GetChildren())
        {
            _grid.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var (action, name) in KeyBindings.Actions)
        {
            var label = Ui.CreateLabel(16, name);
            label.CustomMinimumSize = new Vector2(220f, 0f);
            _grid.AddChild(label);
            var button = Ui.CreateButton(_waiting == action ? "..." : KeyBindings.Name(action), () =>
            {
                _waiting = action;
                Refresh($"«{name}»: нажми новую клавишу. Esc — отмена.");
            });
            button.CustomMinimumSize = new Vector2(180f, 38f);
            button.AddThemeFontSizeOverride("font_size", 15);
            _grid.AddChild(button);
        }
    }

    /// <summary>_Input срабатывает раньше кнопок и игры, поэтому клавиша не нажмёт ничего лишнего.</summary>
    public override void _Input(InputEvent @event)
    {
        if (!Visible || @event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        var code = key.PhysicalKeycode;
        if (_waiting == null)
        {
            if (code == Key.Escape)
            {
                Close();
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        GetViewport().SetInputAsHandled();
        var action = _waiting.Value;
        if (code == Key.Escape)
        {
            _waiting = null;
            Refresh(DefaultHint);
            return;
        }
        if (KeyBindings.IsReserved(code))
        {
            Refresh($"Клавиша {OS.GetKeycodeString(code)} занята игрой, выбери другую.");
            return;
        }
        KeyBindings.Set(action, code);
        Settings.Save();
        _waiting = null;
        Refresh($"Готово: {OS.GetKeycodeString(code)}.");
    }
}
