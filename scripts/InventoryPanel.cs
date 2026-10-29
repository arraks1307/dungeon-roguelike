using System;
using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>
/// Окно инвентаря. Каждый предмет — кнопка: стрелки двигают выбор,
/// Enter или клик применяет/надевает. Клавиши I, Esc и X обрабатывает GameManager.
/// </summary>
public partial class InventoryPanel : CanvasLayer
{
    public event Action<int> UseRequested;

    private Label _title;
    private Label _weaponLabel;
    private Label _armorLabel;
    private VBoxContainer _list;
    private readonly List<Button> _buttons = new();

    public override void _Ready()
    {
        Layer = 5; // поверх HUD, но под меню паузы
        Visible = false;
        var column = Ui.CreateCenteredScreen(this, 0.55f);

        _title = Ui.CreateLabel(36);
        _title.AddThemeColorOverride("font_color", Ui.TitleColor);
        column.AddChild(_title);

        _weaponLabel = Ui.CreateLabel(18);
        column.AddChild(_weaponLabel);
        _armorLabel = Ui.CreateLabel(18);
        column.AddChild(_armorLabel);

        _list = new VBoxContainer { CustomMinimumSize = new Vector2(560f, 0f) };
        _list.AddThemeConstantOverride("separation", 4);
        column.AddChild(_list);

        var hint = Ui.CreateLabel(15, "Enter / клик — использовать или надеть  •  X — выбросить  •  I / Esc — закрыть");
        hint.Modulate = new Color(1f, 1f, 1f, 0.6f);
        column.AddChild(hint);
    }

    /// <summary>Индекс предмета под фокусом или -1, если фокуса нет.</summary>
    public int FocusedIndex => _buttons.IndexOf(GetViewport().GuiGetFocusOwner() as Button);

    public void Open(Inventory inventory)
    {
        Visible = true;
        Refresh(inventory, 0);
    }

    public void Refresh(Inventory inventory, int focusIndex)
    {
        _title.Text = $"Инвентарь ({inventory.Items.Count}/{Inventory.Capacity})";
        _weaponLabel.Text = "Оружие: " + DescribeEquipped(inventory.Weapon);
        _armorLabel.Text = "Броня: " + DescribeEquipped(inventory.Armor);

        foreach (Node child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }
        _buttons.Clear();

        if (inventory.Items.Count == 0)
        {
            _list.AddChild(Ui.CreateLabel(18, "Сумка пуста"));
            return;
        }

        for (int i = 0; i < inventory.Items.Count; i++)
        {
            var item = inventory.Items[i];
            int index = i; // своя копия для лямбды, иначе все кнопки получили бы последнее значение i
            var button = Ui.CreateButton($"{i + 1}. {item.Name} — {item.Describe()}", () => UseRequested?.Invoke(index));
            button.Alignment = HorizontalAlignment.Left;
            button.CustomMinimumSize = new Vector2(560f, 40f);
            button.AddThemeFontSizeOverride("font_size", 18);
            button.AddThemeColorOverride("font_color", item.Color.Lightened(0.3f));
            _list.AddChild(button);
            _buttons.Add(button);
        }

        Ui.FocusLater(_buttons[Math.Clamp(focusIndex, 0, _buttons.Count - 1)]);
    }

    private static string DescribeEquipped(Item item) => item == null ? "нет" : $"{item.Name} ({item.Describe()})";
}
