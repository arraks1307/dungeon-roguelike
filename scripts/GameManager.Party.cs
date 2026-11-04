using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Часть GameManager про игру отрядом: свободные ходы, совместные приёмы, живой призрак,
/// подъём союзника, передача вещей и итоги забега. В одиночной игре почти ничего из этого
/// не срабатывает: для этого нужен второй герой рядом.
///
/// Свободные ходы: никто никого не ждёт. Враги ходят после того, как отряд сделал столько
/// действий, сколько в нём живых героев, неважно, кто именно их сделал. Темп врагов
/// от этого не меняется, зато игрок не стоит без дела, пока друг думает.
/// </summary>
public partial class GameManager
{
    private const int GhostActionRounds = 6;  // как часто призрак может помочь
    private const int GhostFov = 4;
    private const int GhostReach = 6;
    private const int ReviveSteps = 2;
    private const float ReviveHealth = 0.25f;
    private const int GiveRange = 2;
    private const int GoldGift = 50;

    /// <summary>Сколько действий отряд уже сделал в этом раунде.</summary>
    private int _roundActions;

    /// <summary>Чей сейчас удар: ему засчитываются урон и победы в итогах забега.</summary>
    private Player _attacker;

    private int ActiveHeroCount => Math.Max(1, LivingHeroes.Count(hero => !hero.IsOffline));

    /// <summary>Действие потрачено: когда отряд сделал свою норму, ходят враги.</summary>
    private void CountPartyAction()
    {
        _roundActions++;
        if (_roundActions >= ActiveHeroCount)
        {
            EndRound();
            return;
        }
        UpdateFov();
        UpdateHud();
    }

    // ---------- Совместные приёмы ----------

    /// <summary>Враг зажат между двумя героями: союзник стоит точно напротив атакующего.</summary>
    private bool IsFlanked(Player hero, Enemy enemy)
    {
        var delta = enemy.GridPos - hero.GridPos;
        var opposite = enemy.GridPos + new Vector2I(Math.Sign(delta.X), Math.Sign(delta.Y));
        return LivingHeroes.Any(other => other != hero && other.GridPos == opposite);
    }

    /// <summary>
    /// Что меняет удар героя: заморозка союзника вдвое усиливает его, а фланг даёт +50%
    /// и обходит щит. Без помощи союзника щит принимает удар на себя.
    /// </summary>
    private int ApplyTeamwork(Player hero, Enemy enemy, int damage)
    {
        if (enemy.FrozenTurns > 0 && enemy.FrozenBy >= 0 && (enemy.FrozenBy != hero.Id || hero.Has(PerkEffect.Shatter)))
        {
            damage *= 2;
            Effects.FloatingText(this, enemy.GridPos, "Вдребезги!", new Color(0.55f, 0.85f, 1f));
        }
        if (!IsFlanked(hero, enemy))
            return ApplyEnemyShield(enemy, damage);
        Effects.FloatingText(this, enemy.GridPos, "С фланга!", InfoColor);
        return damage * 3 / 2;
    }

    /// <summary>Союзник в стойке рядом с целью принимает удар на себя.</summary>
    private Player FindCover(Player target)
    {
        if (target.IsGuarding)
            return target;
        var cover = LivingHeroes.FirstOrDefault(hero => hero != target && hero.IsGuarding && ChebyshevDistance(hero.GridPos, target.GridPos) == 1);
        if (cover == null)
            return target;
        Tell(cover, $"Ты прикрываешь: {target.DisplayName}.", $"{cover.DisplayName} прикрывает: {target.DisplayName}.", InfoColor);
        return cover;
    }

    /// <summary>Вихрь подтягивает врагов с двух клеток вплотную — под удар и под заклинания союзников.</summary>
    private void PullEnemies(Player hero)
    {
        foreach (var enemy in _enemies.Where(enemy => !enemy.IsBoss && ChebyshevDistance(enemy.GridPos, hero.GridPos) == 2).ToList())
        {
            var cell = DungeonMap.Directions.Select(direction => hero.GridPos + direction)
                .Where(c => _map.Get(c) == Tile.Floor && !IsOccupied(c))
                .OrderBy(c => ChebyshevDistance(c, enemy.GridPos))
                .Cast<Vector2I?>()
                .FirstOrDefault();
            if (cell.HasValue && ChebyshevDistance(cell.Value, enemy.GridPos) == 1)
                enemy.MoveTo(cell.Value);
        }
    }

    // ---------- Подъём и призрак ----------

    /// <summary>Шаг в призрака: за два таких шага союзник поднимается на ноги.</summary>
    private void ReviveStep(Player hero, Player ghost)
    {
        ghost.ReviveProgress++;
        if (ghost.ReviveProgress < ReviveSteps)
        {
            Tell(hero, $"Ты поднимаешь: {ghost.DisplayName}. Ещё шаг!", $"{hero.DisplayName} поднимает: {ghost.DisplayName}.", GoodColor);
            return;
        }
        ghost.Revive(ReviveHealth);
        hero.RevivesMade++;
        Effects.Burst(this, ghost.GridPos, GoodColor, 30);
        _sound.Play(Sound.Heal);
        Tell(ghost, $"{hero.DisplayName} поднимает тебя на ноги!", $"{ghost.DisplayName} снова в строю!", GoodColor);
    }

    /// <summary>
    /// Призрак не выбывает из игры: летает сквозь стены и разведывает этаж. Раз в несколько раундов
    /// он может помочь: F сбивает ближайшего врага, G немного лечит союзника.
    /// Раунды отряда призрак не двигает.
    /// </summary>
    private bool PerformGhostAction(Player ghost, HeroAction action)
    {
        switch (action.Kind)
        {
            case HeroActionKind.Move when action.Direction != Vector2I.Zero:
                var target = ghost.GridPos + action.Direction;
                if (!_map.InBounds(target))
                    return false;
                ghost.ReviveProgress = 0;
                ghost.MoveTo(target);
                UpdateFov();
                UpdateHud();
                return true;

            case HeroActionKind.Skill:
                return GhostHelp(ghost, action.Index);

            default:
                return false;
        }
    }

    private bool GhostHelp(Player ghost, int index)
    {
        if (_turns < ghost.GhostReadyRound)
        {
            if (IsLocalHero(ghost))
                _hud.AddMessage($"Силы призрака вернутся через {ghost.GhostReadyRound - _turns} раунд(а).");
            return false;
        }

        if (index == 0)
        {
            var enemy = _enemies.Where(other => ChebyshevDistance(other.GridPos, ghost.GridPos) <= GhostReach)
                .OrderBy(other => ChebyshevDistance(other.GridPos, ghost.GridPos)).FirstOrDefault();
            if (enemy == null)
                return NoTargets(ghost);
            enemy.Freeze(1);
            Effects.Burst(this, enemy.GridPos, new Color(0.75f, 0.85f, 1f), 14);
            Tell(ghost, $"Ты пугаешь врага: {enemy.DisplayName} пропустит ход.", $"Призрак пугает врага: {enemy.DisplayName}.", InfoColor);
        }
        else
        {
            var ally = LivingHeroes.Where(other => other.IsHurt && ChebyshevDistance(other.GridPos, ghost.GridPos) <= GhostReach)
                .OrderBy(other => other.Hp).FirstOrDefault();
            if (ally == null)
                return NoTargets(ghost);
            int amount = Math.Max(2, ally.MaxHp / 10);
            ally.Heal(amount);
            Effects.FloatingText(this, ally.GridPos, $"+{amount}", GoodColor);
            _sound.Play(Sound.Heal);
        }
        ghost.GhostReadyRound = _turns + GhostActionRounds;
        UpdateHud();
        return true;
    }

    // ---------- Передача вещей ----------

    private Player NearestAlly(Player hero) => LivingHeroes
        .Where(other => other != hero && ChebyshevDistance(other.GridPos, hero.GridPos) <= GiveRange)
        .OrderBy(other => ChebyshevDistance(other.GridPos, hero.GridPos))
        .FirstOrDefault();

    private void GiveItem(Player hero, int index)
    {
        var ally = NearestAlly(hero);
        if (hero.IsGhost || ally == null || index < 0 || index >= hero.Inventory.Items.Count)
        {
            if (IsLocalHero(hero))
                _hud.AddMessage("Рядом нет союзника: подойди на две клетки.");
            return;
        }
        if (!ally.Inventory.CanAdd(hero.Inventory.Items[index]))
        {
            if (IsLocalHero(hero))
                _hud.AddMessage($"Сумка полна: {ally.DisplayName}.");
            return;
        }
        var item = hero.Inventory.TakeOne(index);
        ally.Inventory.TryAdd(item);
        _sound.Play(Sound.Pickup);
        Tell(hero, $"Ты передаёшь: {item.Name} ({ally.DisplayName}).", $"{hero.DisplayName} передаёт {ally.DisplayName}: {item.Name}.", InfoColor);
        if (IsLocalHero(hero) && _state == GameState.Inventory)
            _inventoryPanel.Refresh(hero, index);
        UpdateHud();
    }

    private void GiveGold(Player hero)
    {
        var ally = NearestAlly(hero);
        int amount = Math.Min(GoldGift, hero.Gold);
        if (hero.IsGhost || ally == null || amount == 0)
        {
            if (IsLocalHero(hero))
                _hud.AddMessage("Рядом нет союзника или золота.");
            return;
        }
        hero.Gold -= amount;
        ally.Gold += amount;
        _sound.Play(Sound.Pickup);
        Tell(hero, $"Ты передаёшь {amount} золота ({ally.DisplayName}).", $"{hero.DisplayName} передаёт {ally.DisplayName} {amount} золота.", InfoColor);
        UpdateHud();
    }

    // ---------- Плиты на двоих и разделение ----------

    private const double PlatesChance = 0.35;
    private const double SplitChance = 0.15;

    /// <summary>Две плиты на этаже отряда: сработают, только если на обеих стоит по герою.</summary>
    private readonly List<Vector2I> _plates = new();
    private readonly List<Node3D> _plateNodes = new();

    private void SpawnPlates()
    {
        if (IsSinglePlayer || _rng.NextDouble() >= PlatesChance)
            return;
        if (TryFindFreeCell(out var first) && TryFindFreeCell(out var second) && first != second)
        {
            PlacePlate(first);
            PlacePlate(second);
        }
    }

    private void PlacePlate(Vector2I cell)
    {
        var plate = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0.38f, BottomRadius = 0.42f, Height = 0.06f,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.6f, 0.4f, 1f), EmissionEnabled = true,
                    Emission = new Color(0.6f, 0.4f, 1f), EmissionEnergyMultiplier = 0.8f,
                },
            },
            Position = Actor.GridToWorld(cell) + new Vector3(0f, 0.03f, 0f),
        };
        AddChild(plate);
        plate.Visible = _map.Explored[cell.X, cell.Y];
        _plates.Add(cell);
        _plateNodes.Add(plate);
    }

    private void ClearPlates()
    {
        foreach (var node in _plateNodes)
            node.QueueFree();
        _plateNodes.Clear();
        _plates.Clear();
    }

    /// <summary>Герой встал на клетку: проверяем плиты. Обе заняты — отряд получает награду.</summary>
    private void CheckPlates(Player hero)
    {
        if (!_plates.Contains(hero.GridPos))
            return;
        if (!_plates.All(plate => LivingHeroes.Any(other => other.GridPos == plate)))
        {
            _hud.AddMessage("Плита опускается и ждёт второго героя на другой плите.", InfoColor);
            return;
        }

        int gold = 40 + _floor * 8;
        foreach (var ally in LivingHeroes)
        {
            ally.Gold += gold;
            Effects.Burst(this, ally.GridPos, new Color(0.6f, 0.4f, 1f), 24);
        }
        var cell = DungeonMap.Directions.Select(direction => hero.GridPos + direction)
            .FirstOrDefault(c => _map.Get(c) == Tile.Floor && !IsOccupied(c) && !_floorItems.ContainsKey(c), hero.GridPos);
        PlaceItem(RollEquipment(_floor + 3), cell);
        ClearPlates();
        _sound.Play(Sound.Secret);
        _hud.AddMessage($"Плиты сработали! Каждому {gold} золота, а рядом появилась вещь.", InfoColor);
    }

    /// <summary>Иногда этаж разбрасывает отряд: половина героев оказывается далеко от остальных.</summary>
    private void MaybeSplitParty(bool bossFloor)
    {
        if (IsSinglePlayer || bossFloor || _floor < 2 || _rng.NextDouble() >= SplitChance)
            return;
        foreach (var hero in _heroes.Where((_, index) => index % 2 == 1))
            if (TryFindFreeCell(out var cell))
                hero.Teleport(cell);
        _camera.SnapToTarget();
        _hud.AddMessage("Пол уходит из-под ног: отряд разбросало по этажу! Найдите друг друга.", DamageColor);
    }

    // ---------- Итоги и фразы ----------

    /// <summary>Кто что сделал за забег — для экрана итогов отряда.</summary>
    private string PartySummary() => IsSinglePlayer ? "" : "\n" + string.Join("\n", _heroes.Select(hero =>
        $"{hero.DisplayName}: урон {hero.DamageDealt}, победы {hero.KillsMade}, поднял союзников {hero.RevivesMade}"));

    private static readonly string[] QuickPhrases = { "За мной!", "Помоги!", "Жди меня!" };
}
