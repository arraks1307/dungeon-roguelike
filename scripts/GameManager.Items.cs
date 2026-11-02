using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Часть GameManager про предметы: подбор, применение, выбрасывание, золото и торговец.
/// partial означает, что это тот же класс, просто описанный в нескольких файлах.
/// </summary>
public partial class GameManager
{
    private const double ItemDropChance = 0.2;
    private const double GoldDropChance = 0.35;
    private const double MerchantChance = 0.4;
    private const int BombRadius = 2;

    private readonly Dictionary<Vector2I, ItemPickup> _floorItems = new();
    private Merchant _merchant;

    // ---------- Предметы и золото на полу ----------

    private void SpawnItems(int count)
    {
        for (int i = 0; i < count; i++)
            if (TryFindFreeCell(out var cell))
                PlaceItem(ItemCatalog.Roll(_rng, _floor), cell);
    }

    private void SpawnGold(int count)
    {
        for (int i = 0; i < count; i++)
            if (TryFindFreeCell(out var cell))
                PlaceGold(_rng.Next(5, 16) + _floor * 2, cell);
    }

    private void PlaceItem(Item item, Vector2I cell) => AddPickup(ItemPickup.Create(item, cell), cell);

    private void PlaceGold(int amount, Vector2I cell) => AddPickup(ItemPickup.CreateGold(amount, cell), cell);

    private void AddPickup(ItemPickup pickup, Vector2I cell)
    {
        AddChild(pickup);
        pickup.Visible = _map.Explored[cell.X, cell.Y];
        _floorItems[cell] = pickup;
    }

    /// <summary>После смерти обычного врага иногда остаётся предмет или золото.</summary>
    private void DropLoot(Enemy enemy)
    {
        if (_floorItems.ContainsKey(enemy.GridPos))
            return;

        double roll = _rng.NextDouble();
        if (roll < ItemDropChance)
        {
            PlaceItem(ItemCatalog.Roll(_rng, _floor), enemy.GridPos);
            _hud.AddMessage($"{enemy.DisplayName} что-то роняет.");
        }
        else if (roll < ItemDropChance + GoldDropChance)
        {
            PlaceGold(_rng.Next(3, 9) + _floor, enemy.GridPos);
        }
    }

    private void TryPickUpItem(Vector2I cell)
    {
        if (!_floorItems.TryGetValue(cell, out var pickup))
            return;

        if (pickup.Item == null)
        {
            _player.Gold += pickup.Gold;
            _hud.AddMessage($"Ты подбираешь {pickup.Gold} золота.", InfoColor);
            Effects.FloatingText(this, cell, $"+{pickup.Gold} зол.", InfoColor);
        }
        else if (_player.Inventory.TryAdd(pickup.Item))
        {
            _hud.AddMessage($"Ты подбираешь: {pickup.Item.Name}.", pickup.Item.RarityColor);
        }
        else
        {
            _hud.AddMessage($"Здесь лежит: {pickup.Item.Name}. Но сумка полна!", DamageColor);
            return;
        }

        _floorItems.Remove(cell);
        pickup.QueueFree();
        _sound.Play(Sound.Pickup);
    }

    // ---------- Инвентарь ----------

    // Пока открыто окно, HUD прячем: иначе журнал и статистика просвечивают сквозь окно
    private void OpenInventory()
    {
        _state = GameState.Inventory;
        _hud.Visible = false;
        _inventoryPanel.Open(_player);
    }

    private void CloseInventory()
    {
        _state = GameState.Playing;
        _inventoryPanel.Visible = false;
        _hud.Visible = true;
        UpdateHud();
    }

    /// <summary>Нажали на предмет в окне инвентаря.</summary>
    private void OnInventoryUse(int index)
    {
        if (_state != GameState.Inventory)
            return;
        if (UseItem(index))
        {
            CloseInventory();
            EndPlayerTurn();
        }
        else
        {
            _inventoryPanel.Refresh(_player, index);
        }
    }

    /// <summary>Клавиши 1–5: применить предмет из ячейки, не открывая инвентарь.</summary>
    private void QuickUse(int index)
    {
        if (index >= _player.Inventory.Items.Count)
            return;
        if (UseItem(index))
            EndPlayerTurn();
        else
            UpdateHud();
    }

    /// <summary>
    /// Применить или надеть предмет из сумки.
    /// Возвращает true, если действие состоялось и тратит ход.
    /// </summary>
    private bool UseItem(int index)
    {
        var inventory = _player.Inventory;
        if (index < 0 || index >= inventory.Items.Count)
            return false;

        var item = inventory.Items[index];
        int power = item.Template.Power;

        if (item.IsEquipment)
        {
            _player.Equip(index);
            _hud.AddMessage($"Ты экипируешь: {item.Name}.", item.RarityColor);
            _sound.Play(Sound.Equip);
            return true;
        }

        switch (item.Kind)
        {
            case ItemKind.HealthPotion:
                if (!_player.IsHurt)
                {
                    _hud.AddMessage("Ты и так полностью здоров.");
                    return false;
                }
                inventory.TakeOne(index);
                _player.Heal(power);
                _hud.AddMessage($"Ты выпиваешь {item.Name}: +{power} здоровья.", GoodColor);
                Effects.FloatingText(this, _player.GridPos, $"+{power}", GoodColor);
                Effects.Burst(this, _player.GridPos, GoodColor, 12);
                _sound.Play(Sound.Heal);
                return true;

            case ItemKind.StrengthPotion:
                inventory.TakeOne(index);
                _player.ApplyStrength(power);
                _hud.AddMessage($"Мышцы наливаются силой: +{power} к атаке на {ItemCatalog.BuffDuration} ходов.", InfoColor);
                Effects.Burst(this, _player.GridPos, item.Color, 16);
                _sound.Play(Sound.LevelUp);
                return true;

            case ItemKind.InvisibilityPotion:
                inventory.TakeOne(index);
                _player.ApplyInvisibility(power);
                _hud.AddMessage($"Ты становишься невидимым на {power} ходов. Атака раскроет тебя.", InfoColor);
                _sound.Play(Sound.Teleport);
                return true;

            case ItemKind.TeleportScroll:
                if (!TryFindFreeCell(out var destination, includeStartRoom: true))
                    return false;
                inventory.TakeOne(index);
                Effects.Burst(this, _player.GridPos, item.Color, 20);
                _player.Teleport(destination);
                Effects.Burst(this, destination, item.Color, 20);
                _camera.SnapToTarget();
                _hud.AddMessage("Вспышка — и ты в другом месте!", InfoColor);
                _sound.Play(Sound.Teleport);
                TryPickUpItem(destination);
                return true;

            case ItemKind.FireScroll:
            case ItemKind.FreezeScroll:
                var targets = _enemies.Where(enemy => _visible.Contains(enemy.GridPos)).ToList();
                if (targets.Count == 0)
                {
                    _hud.AddMessage("Рядом нет врагов — свиток лучше поберечь.");
                    return false;
                }
                inventory.TakeOne(index);
                if (item.Kind == ItemKind.FireScroll)
                    DamageEnemies(targets, power + _floor, item.Color, "Огненная волна!");
                else
                    FreezeEnemies(targets, power, item.Color);
                return true;

            case ItemKind.Bomb:
                var nearby = _enemies.Where(enemy => ChebyshevDistance(enemy.GridPos, _player.GridPos) <= BombRadius).ToList();
                if (nearby.Count == 0)
                {
                    _hud.AddMessage("Рядом нет врагов — бомбу лучше поберечь.");
                    return false;
                }
                inventory.TakeOne(index);
                Effects.Burst(this, _player.GridPos, new Color(1f, 0.6f, 0.2f), 50);
                DamageEnemies(nearby, power + _floor, new Color(1f, 0.6f, 0.2f), "Взрыв!");
                return true;

            case ItemKind.MapScroll:
                inventory.TakeOne(index);
                for (int x = 0; x < _map.Width; x++)
                    for (int y = 0; y < _map.Height; y++)
                        _map.Explored[x, y] = true;
                RevealAllSecrets();
                _hud.AddMessage("Карта этажа открывается перед тобой — со всеми ловушками и тайниками.", InfoColor);
                _sound.Play(Sound.Pickup);
                return true;
        }
        return false;
    }

    private void DamageEnemies(List<Enemy> targets, int damage, Color color, string title)
    {
        _hud.AddMessage($"{title} Враги получают {damage} урона.", InfoColor);
        _sound.Play(Sound.Fire);
        _camera.Shake(0.25f);
        foreach (var enemy in targets)
        {
            enemy.TakeDamage(damage);
            Effects.FloatingText(this, enemy.GridPos, damage.ToString(), color);
            Effects.Burst(this, enemy.GridPos, color, 24);
            if (enemy.IsDead)
                KillEnemy(enemy);
        }
    }

    private void FreezeEnemies(List<Enemy> targets, int turns, Color color)
    {
        _hud.AddMessage($"Ледяной ветер! Враги заморожены на {turns} ходов.", InfoColor);
        _sound.Play(Sound.Teleport);
        foreach (var enemy in targets)
        {
            enemy.Freeze(turns);
            Effects.Burst(this, enemy.GridPos, color, 16);
        }
    }

    /// <summary>Выбросить одну штуку предмета под ноги. Не тратит ход.</summary>
    private void DropItem(int index)
    {
        var inventory = _player.Inventory;
        if (index < 0 || index >= inventory.Items.Count)
            return;
        if (_floorItems.ContainsKey(_player.GridPos))
        {
            _hud.AddMessage("Здесь уже что-то лежит.");
            return;
        }

        var item = inventory.TakeOne(index);
        PlaceItem(item, _player.GridPos);
        _hud.AddMessage($"Ты выбрасываешь: {item.Name}.");
        _inventoryPanel.Refresh(_player, index);
        UpdateFov();
        UpdateHud();
    }

    private static int ChebyshevDistance(Vector2I a, Vector2I b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    // ---------- Торговец ----------

    /// <summary>Торговец бывает со 2-го этажа: всегда сразу после босса, иначе — с шансом.</summary>
    private bool ShouldSpawnMerchant() =>
        _floor >= 2 && (_floor % BossFloorInterval == 1 || _rng.NextDouble() < MerchantChance);

    private void SpawnMerchant()
    {
        if (!TryFindFreeCell(out var cell))
            return;
        var stock = Enumerable.Range(0, Merchant.StockSize)
            .Select(_ => ItemCatalog.Roll(_rng, _floor + 1)) // товары чуть лучше, чем на полу
            .ToList();
        _merchant = Merchant.Create(cell, stock);
        AddChild(_merchant);
    }

    private void OpenShop()
    {
        _state = GameState.Shop;
        _hud.Visible = false;
        _shopPanel.Open(_merchant.Stock, _player);
        _sound.Play(Sound.Pickup);
    }

    private void CloseShop()
    {
        _state = GameState.Playing;
        _shopPanel.Visible = false;
        _hud.Visible = true;
        UpdateHud();
    }

    private void BuyItem(int index)
    {
        if (_state != GameState.Shop || index < 0 || index >= _merchant.Stock.Count)
            return;

        var item = _merchant.Stock[index];
        if (_player.Gold < item.Price)
        {
            _shopPanel.ShowStatus($"Не хватает золота: нужно {item.Price}, у тебя {_player.Gold}.", DamageColor);
        }
        else if (!_player.Inventory.CanAdd(item))
        {
            _shopPanel.ShowStatus("Сумка полна — сначала что-нибудь продай.", DamageColor);
        }
        else
        {
            _player.Gold -= item.Price;
            _player.Inventory.TryAdd(item);
            _merchant.Stock.RemoveAt(index);
            _shopPanel.ShowStatus($"Куплено: {item.Name}.", GoodColor);
            _sound.Play(Sound.Pickup);
        }
        _shopPanel.Refresh(_merchant.Stock, _player, focusBuy: true, index);
    }

    private void SellItem(int index)
    {
        if (_state != GameState.Shop || index < 0 || index >= _player.Inventory.Items.Count)
            return;

        var item = _player.Inventory.TakeOne(index);
        _player.Gold += item.SellPrice;
        _merchant.Stock.Add(item); // проданное можно выкупить обратно по полной цене
        _shopPanel.ShowStatus($"Продано: {item.Name} (+{item.SellPrice} зол.).", InfoColor);
        _sound.Play(Sound.Pickup);
        _shopPanel.Refresh(_merchant.Stock, _player, focusBuy: false, index);
    }
}
