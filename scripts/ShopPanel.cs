using System;
using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>
/// Магазин торговца: слева товары (купить), справа твоя сумка (продать за полцены).
/// Между колонками можно ходить стрелками влево/вправо — Godot сам находит соседнюю кнопку.
/// </summary>
public partial class ShopPanel : CanvasLayer
{
    public event Action<int> BuyRequested;
    public event Action<int> SellRequested;

    private Label _goldLabel;
    private Label _status;
    private Label _details;
    private VBoxContainer _buyList;
    private VBoxContainer _sellList;

    public override void _Ready()
    {
        Layer = 5;
        Visible = false;
        var column = Ui.CreateCenteredScreen(this, 0.6f);

        var title = Ui.CreateLabel(34, "Торговец");
        title.AddThemeColorOverride("font_color", Ui.TitleColor);
        column.AddChild(title);

        _goldLabel = Ui.CreateLabel(20);
        column.AddChild(_goldLabel);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 32);
        column.AddChild(columns);

        _buyList = CreateColumn(columns, "Купить");
        _sellList = CreateColumn(columns, "Продать");

        _details = Ui.CreateWrappedLabel(16, 780f);
        _details.CustomMinimumSize = new Vector2(780f, 70f);
        column.AddChild(_details);

        _status = Ui.CreateLabel(17);
        column.AddChild(_status);

        var hint = Ui.CreateLabel(15, "Enter / клик — купить или продать  •  стрелки — выбор  •  Esc — уйти");
        hint.Modulate = new Color(1f, 1f, 1f, 0.6f);
        column.AddChild(hint);
    }

    private static VBoxContainer CreateColumn(Node parent, string header)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        parent.AddChild(box);
        box.AddChild(Ui.CreateLabel(20, header));
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 4);
        box.AddChild(list);
        return list;
    }

    public void Open(List<Item> stock, Player player)
    {
        Visible = true;
        _status.Text = "";
        Refresh(stock, player, focusBuy: true, 0);
    }

    public void ShowStatus(string text, Color color)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", color);
    }

    public void Refresh(List<Item> stock, Player player, bool focusBuy, int focusIndex)
    {
        _goldLabel.Text = $"Твоё золото: {player.Gold}";
        _details.Text = "";

        var buyButtons = Fill(_buyList, stock, item => $"{item.Name} — {item.Price} зол.",
            index => BuyRequested?.Invoke(index), player);
        var sellButtons = Fill(_sellList, player.Inventory.Items, item => $"{item.DisplayName} — {item.SellPrice} зол.",
            index => SellRequested?.Invoke(index), player);

        if (buyButtons.Count == 0)
            _buyList.AddChild(Ui.CreateLabel(17, "Всё распродано"));
        if (sellButtons.Count == 0)
            _sellList.AddChild(Ui.CreateLabel(17, "Нечего продать"));

        // Фокус остаётся в той же колонке, что и был; если она пуста — переходим в другую
        var preferred = focusBuy ? buyButtons : sellButtons;
        var fallback = focusBuy ? sellButtons : buyButtons;
        var target = preferred.Count > 0 ? preferred : fallback;
        if (target.Count > 0)
            Ui.FocusLater(target[Math.Clamp(focusIndex, 0, target.Count - 1)]);
    }

    private List<Button> Fill(VBoxContainer list, List<Item> items, Func<Item, string> text, Action<int> onPressed, Player player)
    {
        foreach (Node child in list.GetChildren())
        {
            list.RemoveChild(child);
            child.QueueFree();
        }

        var buttons = new List<Button>();
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            int index = i;
            var button = Ui.CreateItemButton(item, text(item), () => onPressed(index),
                () => _details.Text = ItemInfo.Describe(item, player.Inventory));
            list.AddChild(button);
            buttons.Add(button);
        }
        return buttons;
    }
}
