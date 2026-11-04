using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Часть GameManager про услуги торговца — траты, которые не кончаются: заточка надетых вещей,
/// лечение и таинственный ларец. Золота к середине игры много, а товаров мало, поэтому заточка
/// дорожает с каждым разом: денег всегда есть куда вложить, но вещь не становится всесильной.
/// Услуги — действия из общего потока, как покупка: в сетевой игре их видят все компьютеры.
/// </summary>
public partial class GameManager
{
    private const int UpgradeBasePrice = 60;
    private const float UpgradePriceGrowth = 1.75f;
    private const int HealPricePerHp = 3;
    private const int MysteryBasePrice = 120;
    private const int MysteryPricePerFloor = 25;
    private const int MysteryBonusFloors = 4;   // ларец даёт вещь как будто с более глубокого этажа

    private long _goldSpentOnServices; // сводка для симуляции

    /// <summary>Цена следующей заточки: 60, 105, 185, 320, 560, 980… Округляем до 5, чтобы числа были приятными.</summary>
    private static int UpgradePrice(Item item)
    {
        float price = UpgradeBasePrice * MathF.Pow(UpgradePriceGrowth, item.Upgrade);
        return (int)MathF.Round(price / 5f) * 5;
    }

    private static int HealPrice(Player hero) => (hero.MaxHp - hero.Hp) * HealPricePerHp;

    private int MysteryPrice => MysteryBasePrice + _floor * MysteryPricePerFloor;

    /// <summary>Что показать в колонке «Услуги» своему герою.</summary>
    private List<ShopService> BuildServices()
    {
        var hero = _localHero;
        var services = new List<ShopService>();
        foreach (var slot in Inventory.AllSlots)
        {
            var item = hero.Inventory.GetEquipped(slot);
            if (item == null)
                continue;
            var gain = (Item.UpgradeStats(slot, item.Upgrade + 1) - Item.UpgradeStats(slot, item.Upgrade)).Describe();
            string details = $"[b]Заточка: {item.Name}[/b]\nНавсегда усиливает надетую вещь.\n\n" +
                             $"[color=#{GoodColor.ToHtml(false)}]{string.Join("\n", gain)}[/color]\n\n" +
                             $"Следующая заточка будет дороже.";
            services.Add(new ShopService(HeroAction.Upgrade((int)slot), $"Заточка: {item.Name}\n{UpgradePrice(item)} зол.", details, item));
        }

        string healText = hero.IsHurt ? $"Лечение\n{HealPrice(hero)} зол." : "Лечение\nтебе не нужно";
        services.Add(new ShopService(HeroAction.Treat, healText,
            $"[b]Лечение[/b]\nПолностью восстанавливает здоровье.\n\n{HealPricePerHp} золота за единицу здоровья.", null));
        services.Add(new ShopService(HeroAction.Mystery, $"Таинственный ларец\n{MysteryPrice} зол.",
            "[b]Таинственный ларец[/b]\nСлучайная вещь, как будто с более глубокого этажа. Внутри может быть и обычная.", null));
        return services;
    }

    private void OnShopService(HeroAction action)
    {
        if (_state == GameState.Shop)
            SubmitLocalAction(action);
    }

    /// <summary>Списать золото. Если не хватает — подсказать своему игроку и вернуть false.</summary>
    private bool TryPay(Player hero, int price)
    {
        if (hero.Gold < price)
        {
            if (ShopOpenFor(hero))
                _shopPanel.ShowStatus($"Не хватает золота: нужно {price}, у тебя {hero.Gold}.", DamageColor);
            return false;
        }
        hero.Gold -= price;
        _goldSpentOnServices += price;
        _tradeCount++;
        return true;
    }

    /// <summary>После услуги: обновить окно у того, кто ею воспользовался, и HUD у всех.</summary>
    private void FinishService(Player hero, int focus)
    {
        if (ShopOpenFor(hero))
            _shopPanel.Refresh(_merchant.Stock, hero, focusBuy: true, 0, focus);
        UpdateHud();
    }

    private void UpgradeItem(Player hero, EquipSlot slot)
    {
        var item = hero.Inventory.GetEquipped(slot);
        if (_merchant == null || hero.IsGhost || item == null || !TryPay(hero, UpgradePrice(item)))
            return;
        item.Upgrade++;
        hero.RefreshStats();
        _sound.Play(Sound.Equip);
        if (ShopOpenFor(hero))
            _shopPanel.ShowStatus($"Заточено: {item.Name}.", GoodColor);
        else
            _hud.AddMessage($"{hero.DisplayName} затачивает: {item.Name}.");
        FinishService(hero, Array.IndexOf(Inventory.AllSlots, slot));
    }

    private void HealAtMerchant(Player hero)
    {
        if (_merchant == null || hero.IsGhost || !hero.IsHurt || !TryPay(hero, HealPrice(hero)))
            return;
        hero.Heal(hero.MaxHp);
        _sound.Play(Sound.Heal);
        if (ShopOpenFor(hero))
            _shopPanel.ShowStatus("Раны затянулись.", GoodColor);
        FinishService(hero, hero.Inventory.Equipped.Count());
    }

    private void OpenMysteryBox(Player hero)
    {
        if (_merchant == null || hero.IsGhost)
            return;
        if (hero.Inventory.IsFull)
        {
            if (ShopOpenFor(hero))
                _shopPanel.ShowStatus("Сумка полна — сначала что-нибудь продай.", DamageColor);
            return;
        }
        if (!TryPay(hero, MysteryPrice))
            return;

        var item = RollEquipment(_floor + MysteryBonusFloors);
        hero.Inventory.TryAdd(item);
        _sound.Play(Sound.Chest);
        if (ShopOpenFor(hero))
            _shopPanel.ShowStatus($"В ларце: {item.Name}!", item.RarityColor);
        else
            _hud.AddMessage($"{hero.DisplayName} открывает ларец: {item.Name}.");
        FinishService(hero, hero.Inventory.Equipped.Count() + 1);
    }

    /// <summary>Случайная экипировка: перебрасываем, пока не выпадет не расходник.</summary>
    private Item RollEquipment(int floor)
    {
        Item item;
        do
            item = ItemCatalog.Roll(_rng, floor);
        while (!item.IsEquipment);
        return item;
    }

    /// <summary>
    /// Бот в симуляции (--sim-shop) тратит золото почти как человек: когда на этаже есть торговец,
    /// лечится при тяжёлых ранах и затачивает самую дешёвую вещь, пока хватает денег.
    /// Подходить к торговцу он не умеет, поэтому считаем, что зашёл по пути.
    /// </summary>
    private void BotUseServices()
    {
        if (_merchant == null)
            return;
        foreach (var hero in LivingHeroes.ToList())
        {
            if (hero.Hp < hero.MaxHp / 2 && hero.Gold >= HealPrice(hero))
                HealAtMerchant(hero);
            while (true)
            {
                var cheapest = hero.Inventory.Equipped.OrderBy(UpgradePrice).FirstOrDefault();
                if (cheapest == null || hero.Gold < UpgradePrice(cheapest))
                    break;
                UpgradeItem(hero, cheapest.Slot);
            }
        }
    }
}
