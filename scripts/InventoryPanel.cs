using System;
using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>
/// Окно инвентаря: слева надетая экипировка и описание выбранного предмета,
/// справа сумка. Каждый предмет — кнопка: стрелки двигают выбор, Enter или клик применяет.
/// Клавиши I, Esc и X обрабатывает GameManager.
/// </summary>
public partial class InventoryPanel : CanvasLayer
{
    public event Action<int> UseRequested;

    private Label _title;
    private VBoxContainer _equippedList;
    private Label _details;
    private VBoxContainer _list;
    private readonly List<Button> _buttons = new();
    private Player _player;

    public override void _Ready()
    {
        Layer = 5; // поверх HUD, но под меню паузы
        Visible = false;
        var column = Ui.CreateCenteredScreen(this, 0.6f);

        _title = Ui.CreateLabel(34);
        _title.AddThemeColorOverride("font_color", Ui.TitleColor);
        column.AddChild(_title);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 32);
        column.AddChild(columns);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(400f, 0f) };
        left.AddThemeConstantOverride("separation", 6);
        columns.AddChild(left);
        left.AddChild(Ui.CreateLabel(20, "Надето"));
        _equippedList = new VBoxContainer();
        left.AddChild(_equippedList);
        left.AddChild(new HSeparator());
        _details = Ui.CreateWrappedLabel(16, 400f);
        left.AddChild(_details);

        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 4);
        columns.AddChild(right);
        right.AddChild(Ui.CreateLabel(20, "Сумка"));
        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 4);
        right.AddChild(_list);

        var hint = Ui.CreateLabel(15,
            "Enter / клик — использовать или надеть  •  X — выбросить  •  1–5 — быстрые слоты  •  I / Esc — закрыть");
        hint.Modulate = new Color(1f, 1f, 1f, 0.6f);
        column.AddChild(hint);
    }

    /// <summary>Индекс предмета под фокусом или -1, если фокуса нет.</summary>
    public int FocusedIndex => _buttons.IndexOf(GetViewport().GuiGetFocusOwner() as Button);

    public void Open(Player player)
    {
        Visible = true;
        Refresh(player, 0);
    }

    public void Refresh(Player player, int focusIndex)
    {
        _player = player;
        var inventory = player.Inventory;
        _title.Text = $"Инвентарь ({inventory.Items.Count}/{Inventory.Capacity})   •   Золото: {player.Gold}";

        Clear(_equippedList);
        foreach (var slot in Inventory.AllSlots)
        {
            var item = inventory.GetEquipped(slot);
            var label = Ui.CreateLabel(16, $"{Inventory.SlotName(slot)}: " +
                                           (item == null ? "—" : $"{item.Name} ({item.ShortEffect()})"));
            if (item != null)
                label.AddThemeColorOverride("font_color", item.RarityColor);
            _equippedList.AddChild(label);
        }

        Clear(_list);
        _buttons.Clear();
        _details.Text = "";

        if (inventory.Items.Count == 0)
        {
            _list.AddChild(Ui.CreateLabel(18, "Сумка пуста"));
            return;
        }

        for (int i = 0; i < inventory.Items.Count; i++)
        {
            var item = inventory.Items[i];
            // Своя копия i для лямбды: иначе все кнопки «увидели» бы последнее значение цикла
            int index = i;
            var button = Ui.CreateItemButton(item, $"{i + 1}. {item.DisplayName}",
                () => UseRequested?.Invoke(index),
                () => _details.Text = ItemInfo.Describe(item, _player.Inventory));
            _list.AddChild(button);
            _buttons.Add(button);
        }

        Ui.FocusLater(_buttons[Math.Clamp(focusIndex, 0, _buttons.Count - 1)]);
    }

    private static void Clear(Node container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }
}
