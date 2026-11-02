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
    private RichTextLabel _details;
    private VBoxContainer _buyList;
    private VBoxContainer _sellList;

    public override void _Ready()
    {
        Layer = 5;
        Visible = false;
        var column = Ui.CreateCenteredScreen(this, 0.6f);

        column.AddThemeConstantOverride("separation", 18);

        var header = new HBoxContainer();
        column.AddChild(header);
        header.AddChild(Ui.CreateTitle("Торговец"));
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _goldLabel = Ui.CreateMutedLabel(18);
        _goldLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        header.AddChild(_goldLabel);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        column.AddChild(columns);

        _buyList = CreateColumn(columns, "КУПИТЬ");
        _sellList = CreateColumn(columns, "ПРОДАТЬ");

        var detailsCard = new PanelContainer { CustomMinimumSize = new Vector2(300f, 0f) };
        detailsCard.AddThemeStyleboxOverride("panel", UiTheme.Slot(new Color(1f, 1f, 1f, 0.08f), 0.04f));
        columns.AddChild(detailsCard);
        _details = Ui.CreateRichText(280f);
        detailsCard.AddChild(_details);

        _status = Ui.CreateLabel(17);
        column.AddChild(_status);

        column.AddChild(Ui.CreateMutedLabel(14, "Enter / клик — купить или продать  •  стрелки — выбор  •  Esc — уйти"));
    }

    /// <summary>
    /// Колонка с прокруткой: товаров и вещей может быть больше, чем помещается на экран.
    /// FollowFocus — при выборе стрелками ScrollContainer сам прокручивает к выбранной кнопке.
    /// </summary>
    private static VBoxContainer CreateColumn(Node parent, string header)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        parent.AddChild(box);
        box.AddChild(Ui.CreateMutedLabel(15, header));
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(350f, 390f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        box.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(list);
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

        // Название и цена в две строки — так длинные названия вроде «Кольцо меткости остроты» помещаются
        var buyButtons = Fill(_buyList, stock, item => $"{item.Name}\n{item.Price} зол.",
            index => BuyRequested?.Invoke(index), player);
        var sellButtons = Fill(_sellList, player.Inventory.Items, item => $"{item.DisplayName}\n+{item.SellPrice} зол.",
            index => SellRequested?.Invoke(index), player);

        if (buyButtons.Count == 0)
            _buyList.AddChild(Ui.CreateMutedLabel(17, "Всё распродано"));
        if (sellButtons.Count == 0)
            _sellList.AddChild(Ui.CreateMutedLabel(17, "Нечего продать"));

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
            button.CustomMinimumSize = new Vector2(330f, 62f);
            button.AddThemeFontSizeOverride("font_size", 16);
            list.AddChild(button);
            buttons.Add(button);
        }
        return buttons;
    }
}
