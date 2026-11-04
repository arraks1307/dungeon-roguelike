using System.Linq;
using System;
using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>
/// Окно инвентаря: слева надетая экипировка, в центре сетка ячеек сумки, справа — описание
/// выбранного предмета с цветным сравнением. Ячейки — кнопки: стрелки ходят по сетке
/// (Godot сам находит соседнюю кнопку), Enter или клик применяет. Клавиши I, Esc и X — в GameManager.
/// </summary>
public partial class InventoryPanel : CanvasLayer
{
    private const int Columns = 5;
    private const int IconSize = 48;
    private static readonly Vector2 SlotSize = new(78f, 78f);

    public event Action<int> UseRequested;

    private Label _goldLabel;
    private VBoxContainer _equipmentList;
    private readonly Button[] _slots = new Button[Inventory.Capacity];
    private readonly Label[] _counts = new Label[Inventory.Capacity];
    private TextureRect _detailIcon;
    private RichTextLabel _details;
    private Player _player;

    public override void _Ready()
    {
        Layer = 5; // поверх HUD, но под меню паузы
        Visible = false;
        var column = Ui.CreateCenteredScreen(this, 0.55f);
        column.AddThemeConstantOverride("separation", 20);

        // Заголовок: слева название, справа золото и заполненность сумки
        var header = new HBoxContainer();
        column.AddChild(header);
        header.AddChild(Ui.CreateTitle("Инвентарь"));
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _goldLabel = Ui.CreateMutedLabel(18);
        _goldLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        header.AddChild(_goldLabel);

        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 28);
        column.AddChild(body);

        // ----- Экипировка -----
        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 8);
        body.AddChild(left);
        left.AddChild(Ui.CreateMutedLabel(15, "СНАРЯЖЕНИЕ"));
        _equipmentList = new VBoxContainer();
        _equipmentList.AddThemeConstantOverride("separation", 6);
        left.AddChild(_equipmentList);

        // ----- Сетка сумки -----
        var center = new VBoxContainer();
        center.AddThemeConstantOverride("separation", 8);
        body.AddChild(center);
        center.AddChild(Ui.CreateMutedLabel(15, "СУМКА"));
        var grid = new GridContainer { Columns = Columns };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        center.AddChild(grid);
        for (int i = 0; i < Inventory.Capacity; i++)
            grid.AddChild(CreateSlot(i));

        // ----- Описание выбранного -----
        var detailsCard = new PanelContainer { CustomMinimumSize = new Vector2(320f, 280f) };
        detailsCard.AddThemeStyleboxOverride("panel", UiTheme.Slot(new Color(1f, 1f, 1f, 0.08f), 0.04f));
        body.AddChild(detailsCard);
        var detailsColumn = new VBoxContainer();
        detailsColumn.AddThemeConstantOverride("separation", 10);
        detailsCard.AddChild(detailsColumn);
        _detailIcon = new TextureRect { StretchMode = TextureRect.StretchModeEnum.KeepCentered, CustomMinimumSize = new Vector2(0f, 72f) };
        detailsColumn.AddChild(_detailIcon);
        _details = Ui.CreateRichText(300f);
        detailsColumn.AddChild(_details);

        column.AddChild(Ui.CreateMutedLabel(14,
            "Стрелки / мышь — выбор  •  Enter / клик — использовать  •  Esc — закрыть  •  клавиши меняются в настройках"));
    }

    /// <summary>
    /// Ячейка сумки: кнопка с крупной иконкой. В углах — подписи: номер быстрого слота (1–5)
    /// и количество в стопке. Подписи — дочерние Label с якорями к углам кнопки.
    /// </summary>
    private Button CreateSlot(int index)
    {
        var slot = new Button { CustomMinimumSize = SlotSize, ExpandIcon = false, IconAlignment = HorizontalAlignment.Center };
        slot.Pressed += () => UseRequested?.Invoke(index);
        slot.FocusEntered += () => ShowDetails(index < _player?.Inventory.Items.Count ? _player.Inventory.Items[index] : null);
        Ui.AddFeedback(slot);

        if (index < 5)
        {
            var hotkey = Ui.CreateMutedLabel(12, $"{index + 1}");
            hotkey.Position = new Vector2(7f, 3f);
            slot.AddChild(hotkey);
        }
        var count = Ui.CreateLabel(14);
        count.AnchorLeft = count.AnchorRight = 1f;
        count.AnchorTop = count.AnchorBottom = 1f;
        count.OffsetLeft = -40f;
        count.OffsetRight = -7f;
        count.OffsetTop = -24f;
        count.OffsetBottom = -3f;
        count.HorizontalAlignment = HorizontalAlignment.Right;
        slot.AddChild(count);

        _slots[index] = slot;
        _counts[index] = count;
        return slot;
    }

    /// <summary>Индекс предмета под фокусом или -1, если фокус не на заполненной ячейке.</summary>
    public int FocusedIndex
    {
        get
        {
            int index = Array.IndexOf(_slots, GetViewport().GuiGetFocusOwner() as Button);
            return index >= 0 && index < _player.Inventory.Items.Count ? index : -1;
        }
    }

    public void Open(Player player)
    {
        Visible = true;
        Refresh(player, 0);
    }

    public void Refresh(Player player, int focusIndex)
    {
        _player = player;
        var inventory = player.Inventory;
        var sets = inventory.WornSets.Select(set => $"{set.Name} {inventory.SetCount(set)}/{set.Size}");
        _goldLabel.Text = $"Золото: {player.Gold}     Сумка: {inventory.Items.Count}/{Inventory.Capacity}     {string.Join("   ", sets)}";

        RefreshEquipment(inventory);

        for (int i = 0; i < Inventory.Capacity; i++)
        {
            var slot = _slots[i];
            var item = i < inventory.Items.Count ? inventory.Items[i] : null;
            slot.Icon = item == null ? null : ItemIcons.Get(item, IconSize);
            // Пустые ячейки нельзя выбрать — стрелки их перепрыгивают
            slot.FocusMode = item == null ? Control.FocusModeEnum.None : Control.FocusModeEnum.All;
            slot.MouseFilter = item == null ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop;

            // Рамка ячейки показывает редкость; у обычных вещей — едва заметная
            var border = item == null ? new Color(1f, 1f, 1f, 0.05f)
                : item.Rarity == Rarity.Common ? new Color(1f, 1f, 1f, 0.16f)
                : new Color(item.RarityColor, 0.85f);
            slot.AddThemeStyleboxOverride("normal", UiTheme.Slot(border, item == null ? 0.02f : 0.05f, item?.Rarity > Rarity.Common ? 2 : 1));
            slot.AddThemeStyleboxOverride("hover", UiTheme.Slot(border, 0.11f, 2));
            slot.AddThemeStyleboxOverride("pressed", UiTheme.Slot(UiTheme.Accent, 0.16f, 2));
            _counts[i].Text = item is { Count: > 1 } ? $"×{item.Count}" : "";
        }

        if (inventory.Items.Count > 0)
        {
            Ui.FocusLater(_slots[Math.Clamp(focusIndex, 0, inventory.Items.Count - 1)]);
        }
        else
        {
            ShowDetails(null);
            if (_equipmentList.GetChildCount() > 0)
                Ui.FocusLater((Button)_equipmentList.GetChild(0));
        }
    }

    /// <summary>Пять слотов экипировки: иконка, название слота и предмета. Выбрав слот, можно прочитать описание.</summary>
    private void RefreshEquipment(Inventory inventory)
    {
        foreach (Node child in _equipmentList.GetChildren())
        {
            _equipmentList.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var slot in Inventory.AllSlots)
        {
            var item = inventory.GetEquipped(slot);
            var row = new Button
            {
                Text = $"{Inventory.SlotName(slot)}\n{item?.Name ?? "—"}",
                // В пустом слоте — бледный силуэт вещи, которую туда можно надеть
                Icon = item != null ? ItemIcons.Get(item, 48) : ItemIcons.Get(SlotSilhouette(slot), EmptySlotColor, 48),
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(260f, 62f),
            };
            row.AddThemeFontSizeOverride("font_size", 15);
            row.AddThemeColorOverride("font_color", item?.RarityColor ?? UiTheme.TextMuted);
            row.FocusEntered += () => ShowDetails(item);
            Ui.AddFeedback(row);
            _equipmentList.AddChild(row);
        }
    }

    private static readonly Color EmptySlotColor = new(1f, 1f, 1f, 0.14f);

    private static ItemKind SlotSilhouette(EquipSlot slot) => slot switch
    {
        EquipSlot.Weapon => ItemKind.Weapon,
        EquipSlot.Armor => ItemKind.Armor,
        EquipSlot.Helmet => ItemKind.Helmet,
        EquipSlot.Ring => ItemKind.Ring,
        _ => ItemKind.Amulet,
    };

    private void ShowDetails(Item item)
    {
        if (item == null)
        {
            _detailIcon.Texture = null;
            _details.Text = $"[color=#{UiTheme.TextMuted.ToHtml(false)}]Пусто. Выбери предмет, чтобы увидеть описание.[/color]";
            return;
        }
        _detailIcon.Texture = ItemIcons.Get(item, 72);
        bool inBag = _player.Inventory.Items.Contains(item);
        _details.Text = ItemInfo.Describe(item, _player.Inventory, inBag ? ItemInfo.ActionHint(item) : null);
    }
}
