using System;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Часть GameManager про прогресс между забегами и подсказки: осколки, бонусы «Наследия»,
/// достижения, запись увиденных врагов и подсказка при наведении мыши.
/// </summary>
public partial class GameManager
{
    private LegacyPanel _legacyPanel;
    private int _bossesThisRun;

    private (Vector2I Cell, int Turn, int Actions)? _tooltipKey;

    private void SetupMeta()
    {
        _legacyPanel = new LegacyPanel();
        AddChild(_legacyPanel);
        _legacyPanel.Closed += CloseLegacy;
        _mainMenu.LegacyRequested += OpenLegacy;
    }

    private void OpenLegacy()
    {
        _mainMenu.Visible = false;
        _legacyPanel.Open();
    }

    private void CloseLegacy()
    {
        _legacyPanel.Visible = false;
        _mainMenu.ShowMain();
    }

    /// <summary>Бонусы «Наследия» на старте одиночного забега.</summary>
    private void ApplyLegacy(Player hero)
    {
        hero.AddPermanentHp(4 * Profile.Level(Profile.Vitality));
        hero.AddPermanentAttack(Profile.Level(Profile.Power));
        hero.Heal(hero.MaxHp);
        hero.Gold += 30 * Profile.Level(Profile.Wealth);
        for (int i = 0; i < Profile.Level(Profile.Supplies); i++)
            hero.Inventory.TryAdd(new Item(ItemCatalog.HealthPotion));
        hero.PendingLevelUps += Profile.Level(Profile.Wisdom);
    }

    /// <summary>Забег окончен: осколки, общая статистика и новые достижения. Возвращает строку для экрана итогов.</summary>
    private string FinishRun()
    {
        if (_metaLocked)
            return "";
        var profile = Profile.Current;
        int difficultyBonus = 100 + 25 * (int)_difficulty.Id;
        int shards = (_floor * 3 + _kills / 3 + (_victory ? 40 : 0)) * difficultyBonus / 100;
        profile.Shards += shards;
        profile.TotalKills += _kills;
        profile.BossesKilled += _bossesThisRun;
        profile.BestFloor = Math.Max(profile.BestFloor, _floor);
        profile.Revives += _localHero.RevivesMade;
        if (_victory)
            profile.Wins++;

        void Check(bool condition, string id, System.Collections.Generic.List<string> names)
        {
            var unlocked = condition ? Profile.Unlock(id) : null;
            if (unlocked != null)
                names.Add(unlocked.Name);
        }
        var fresh = new System.Collections.Generic.List<string>();
        Check(_floor >= 5, "floor5", fresh);
        Check(_floor >= 10, "floor10", fresh);
        Check(_victory, "win", fresh);
        Check(_victory && _difficulty.Id == Difficulty.Nightmare, "nightmare", fresh);
        Check(profile.TotalKills >= 500, "kills", fresh);
        Check(profile.BossesKilled >= 10, "bosses", fresh);
        Check(profile.Revives >= 10, "rescue", fresh);
        Check(_localHero.Gold >= 1000, "rich", fresh);
        Profile.Save();

        string achievements = fresh.Count > 0 ? $"\nДостижения: {string.Join(", ", fresh)}" : "";
        return $"\nОсколки: +{shards} (всего {profile.Shards}){achievements}";
    }

    /// <summary>Показать подсказку о клетке под мышкой. Пересчитываем, только когда что-то изменилось.</summary>
    private void UpdateTooltip()
    {
        if (_state != GameState.Playing || _map == null || _hud == null)
        {
            _hud?.HideTooltip();
            _tooltipKey = null;
            return;
        }
        var mouse = GetViewport().GetMousePosition();
        var cell = ScreenToCell(mouse);
        if (cell == null)
        {
            _hud.HideTooltip();
            _tooltipKey = null;
            return;
        }
        var key = (cell.Value, _turns, _roundActions);
        if (_tooltipKey != key)
        {
            _tooltipKey = key;
            _tooltipText = TooltipFor(cell.Value);
        }
        if (_tooltipText == null)
            _hud.HideTooltip();
        else
            _hud.ShowTooltip(_tooltipText, mouse);
    }

    private string _tooltipText;

    private string TooltipFor(Vector2I cell)
    {
        if (!_map.Explored[cell.X, cell.Y])
            return null;
        var enemy = EnemyAt(cell);
        if (enemy != null && _visible.Contains(cell))
            return $"{enemy.DisplayName}  {enemy.Hp}/{enemy.MaxHp}\nАтака {enemy.Attack}, защита {enemy.Defense}\n{Enemy.Describe(enemy.Type)}";
        var ally = _heroes.Find(hero => hero.GridPos == cell);
        if (ally != null)
            return ally.IsGhost ? $"{ally.DisplayName}: призрак. Шагни в него дважды, чтобы поднять." : $"{ally.DisplayName}  {ally.Hp}/{ally.MaxHp}";
        if (_shrines.TryGetValue(cell, out var shrine))
            return ShrineText(shrine);
        if (_floorItems.TryGetValue(cell, out var pickup))
            return pickup.Item == null ? $"Золото: {pickup.Gold}" : $"{pickup.Item.Name}\n{pickup.Item.ShortEffect()}";
        if (_chests.ContainsKey(cell))
            return "Сундук. Иногда в нём прячется мимик.";
        if (_merchant != null && _merchant.GridPos == cell)
            return "Торговец: товары и услуги.";
        if (_plates.Contains(cell))
            return "Плита: сработает, если на обеих плитах стоит по герою.";
        if (_map.Traps.TryGetValue(cell, out var trap) && trap.Revealed)
            return trap.Kind switch
            {
                TrapKind.Spikes => "Шипы",
                TrapKind.Alarm => "Сирена: разбудит этаж",
                TrapKind.Lava => "Лава: жжёт каждый раз",
                _ => "Руна телепорта",
            };
        return _map.Get(cell) == Tile.Stairs ? "Лестница на следующий этаж" : null;
    }
}
