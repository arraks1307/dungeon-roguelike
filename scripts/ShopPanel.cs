using System;
using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>Услуга торговца: что сделать, надпись на кнопке, подробности и (если есть) вещь для иконки.</summary>
public record ShopService(HeroAction Action, string Text, string Details, Item Icon);

/// <summary>
/// Магазин торговца: товары (купить), твоя сумка (продать за полцены) и услуги.
/// Между колонками можно ходить стрелками влево/вправо — Godot сам находит соседнюю кнопку.
/// </summary>
public partial class ShopPanel : CanvasLayer
{
    public event Action<int> BuyRequested;
    public event Action<int> SellRequested;
    public event Action<HeroAction> ServiceRequested;

    /// <summary>Откуда брать список услуг: цены зависят от героя и этажа, их считает GameManager.</summary>
    public Func<List<ShopService>> Services { get; set; }

    private Label _goldLabel;
    private Label _status;
    private RichTextLabel _details;
    private VBoxContainer _buyList;
    private VBoxContainer _sellList;
    private VBoxContainer _serviceList;

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
        columns.AddThemeConstantOverride("separation", 18);
        column.AddChild(columns);

        _buyList = CreateColumn(columns, "КУПИТЬ");
        _sellList = CreateColumn(columns, "ПРОДАТЬ");
        _serviceList = CreateColumn(columns, "УСЛУГИ");

        var detailsCard = new PanelContainer { CustomMinimumSize = new Vector2(230f, 0f) };
        detailsCard.AddThemeStyleboxOverride("panel", UiTheme.Slot(new Color(1f, 1f, 1f, 0.08f), 0.04f));
        columns.AddChild(detailsCard);
        _details = Ui.CreateRichText(210f);
        detailsCard.AddChild(_details);

        _status = Ui.CreateLabel(17);
        column.AddChild(_status);

        column.AddChild(Ui.CreateMutedLabel(14, "Enter / клик — выбрать  •  стрелки — перемещение  •  Esc — уйти"));
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
            CustomMinimumSize = new Vector2(280f, 390f),
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

    /// <summary>focusService — на какую услугу вернуть выбор после её покупки (-1 — услуги не трогаем).</summary>
    public void Refresh(List<Item> stock, Player player, bool focusBuy, int focusIndex, int focusService = -1)
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

        var serviceButtons = FillServices();
        if (focusService >= 0 && serviceButtons.Count > 0)
        {
            Ui.FocusLater(serviceButtons[Math.Clamp(focusService, 0, serviceButtons.Count - 1)]);
            return;
        }

        // Фокус остаётся в той же колонке, что и был; если она пуста — переходим в другую
        var preferred = focusBuy ? buyButtons : sellButtons;
        var fallback = focusBuy ? sellButtons : buyButtons;
        var target = preferred.Count > 0 ? preferred : fallback;
        if (target.Count > 0)
            Ui.FocusLater(target[Math.Clamp(focusIndex, 0, target.Count - 1)]);
    }

    private List<Button> FillServices()
    {
        foreach (Node child in _serviceList.GetChildren())
        {
            _serviceList.RemoveChild(child);
            child.QueueFree();
        }

        var buttons = new List<Button>();
        foreach (var service in Services?.Invoke() ?? new List<ShopService>())
        {
            var button = Ui.CreateButton(service.Text, () => ServiceRequested?.Invoke(service.Action));
            button.Alignment = HorizontalAlignment.Left;
            if (service.Icon != null)
                button.Icon = ItemIcons.Get(service.Icon, 48);
            button.CustomMinimumSize = new Vector2(260f, 62f);
            button.AddThemeFontSizeOverride("font_size", 15);
            button.ClipText = true;
            button.FocusEntered += () => _details.Text = service.Details;
            _serviceList.AddChild(button);
            buttons.Add(button);
        }
        return buttons;
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
            button.CustomMinimumSize = new Vector2(260f, 62f);
            button.AddThemeFontSizeOverride("font_size", 15);
            button.ClipText = true;
            list.AddChild(button);
            buttons.Add(button);
        }
        return buttons;
    }
}
