using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>Особое условие этажа: меняет правила на один этаж, и забеги не похожи друг на друга.</summary>
public enum FloorCondition
{
    None,
    Darkness, // короткий обзор
    Lava,     // по этажу разлита лава: она видна и жжёт каждый раз
    Swarm,    // врагов в полтора раза больше
    Treasure, // много золота и сундуков
}

/// <summary>
/// Часть GameManager про разнообразие забега: условия этажей, легендарные вещи и странник.
/// Всё это решается через _rng и общий поток действий, поэтому работает и в сетевой игре.
/// </summary>
public partial class GameManager
{
    private const double ConditionChance = 0.35;
    private const int DarknessPenalty = 3;
    private const double LegendaryBoxChance = 0.1;

    private FloorCondition _condition;
    private ChoicePanel _choicePanel;
    private Vector2I _wandererCell;

    private void SetupWorldEvents()
    {
        _choicePanel = new ChoicePanel();
        AddChild(_choicePanel);
        _choicePanel.Chosen += index =>
        {
            if (_state == GameState.CampDialog)
            {
                OnCampChoice(index);
                return;
            }
            CloseChoice();
            SubmitLocalAction(HeroAction.Wanderer(index, _wandererCell));
        };
        _choicePanel.Cancelled += () =>
        {
            if (_state == GameState.CampDialog)
                CloseCampDialog();
            else
                CloseChoice();
        };
    }

    private void CloseChoice()
    {
        _choicePanel.Visible = false;
        if (_state == GameState.Event)
            _state = GameState.Playing;
    }

    private void RollFloorCondition(bool bossFloor)
    {
        _condition = FloorCondition.None;
        if (_floor >= 3 && !bossFloor && _rng.NextDouble() < ConditionChance * EventScale)
            _condition = (FloorCondition)_rng.Next(1, 5);
    }

    private void ApplyFloorCondition()
    {
        switch (_condition)
        {
            case FloorCondition.Darkness:
                _hud.AddMessage("Здесь почти не видно: факелы еле горят.", DamageColor);
                break;
            case FloorCondition.Lava:
                for (int i = 0; i < 10 + _floor / 2; i++)
                    if (TryFindFreeCell(out var cell))
                        _map.Traps[cell] = new Trap { Kind = TrapKind.Lava, Revealed = true };
                _hud.AddMessage("Этаж пылает: не наступай на лаву.", DamageColor);
                break;
            case FloorCondition.Swarm:
                _hud.AddMessage("Здесь кишат враги!", DamageColor);
                break;
            case FloorCondition.Treasure:
                SpawnGold(6);
                SpawnChests();
                _hud.AddMessage("Сокровищница: золото лежит повсюду.", InfoColor);
                break;
        }
    }

    // ---------- Легендарные вещи ----------

    /// <summary>Что срабатывает при ударе оружием: клинок бури бьёт всех вокруг каждую третью атаку.</summary>
    private void ApplyLegendaryOnHit(Player hero, int damage)
    {
        if (!hero.HasLegendary(LegendaryEffect.Storm) || ++hero.StormCounter % 3 != 0)
            return;
        Effects.Burst(this, hero.GridPos, new Color(0.5f, 0.75f, 1f), 30);
        foreach (var enemy in _enemies.Where(enemy => _map.AreAdjacent(hero.GridPos, enemy.GridPos)).ToList())
            HitEnemy(enemy, Math.Max(1, damage / 2), new Color(0.5f, 0.75f, 1f));
    }

    private static int GreedBonus(Player hero) => hero.HasLegendary(LegendaryEffect.Greed) ? Math.Min(hero.Gold / 100, 8) : 0;

    /// <summary>Амулет феникса раз за этаж спасает от смерти.</summary>
    private bool TryPhoenix(Player hero)
    {
        if (!hero.IsDead || hero.PhoenixUsed || !hero.HasLegendary(LegendaryEffect.Phoenix))
            return false;
        hero.PhoenixUsed = true;
        hero.Heal(hero.MaxHp * 3 / 10);
        Effects.Burst(this, hero.GridPos, new Color(1f, 0.55f, 0.15f), 50);
        _sound.Play(Sound.LevelUp);
        Tell(hero, "Амулет феникса спасает тебя от смерти!", $"Феникс спасает: {hero.DisplayName}!", InfoColor);
        return true;
    }

    /// <summary>Плащ теней: после победы над врагом герой на два хода исчезает.</summary>
    private void ApplyLegendaryOnKill(Player hero)
    {
        if (hero != null && hero.HasLegendary(LegendaryEffect.Shadow))
            hero.ApplyInvisibility(2);
    }

    // ---------- Странник ----------

    private int WandererGold => 30 + _floor * 10;

    private void OpenWanderer(Player hero, Shrine shrine)
    {
        if (!IsLocalHero(hero) || hero.IsBot || _state != GameState.Playing)
            return;
        _wandererCell = shrine.GridPos;
        _state = GameState.Event;
        _choicePanel.Open("Странник", "У костра сидит путник в капюшоне. Он может помочь, но только один раз.", new List<string>
        {
            $"Взять мешочек: {WandererGold} золота",
            "Спросить дорогу: откроется весь этаж",
            "Заключить сделку: четверть здоровья за хорошую вещь",
        });
    }

    private void ApplyWanderer(Player hero, int choice, Vector2I cell)
    {
        if (hero.IsGhost || !_shrines.TryGetValue(cell, out var shrine))
            return;
        if (shrine.Kind == ShrineKind.Pact)
        {
            ApplyPact(hero, choice, shrine);
            return;
        }
        if (shrine.Kind == ShrineKind.Reliquary)
        {
            ApplyReliquary(hero, choice, shrine);
            return;
        }
        if (shrine.Kind != ShrineKind.Wanderer)
            return;        switch (choice)
        {
            case 0:
                hero.Gold += WandererGold;
                Tell(hero, $"Странник дарит {WandererGold} золота.", $"{hero.DisplayName} получает золото от странника.", InfoColor);
                break;
            case 1:
                for (int x = 0; x < _map.Width; x++)
                    for (int y = 0; y < _map.Height; y++)
                        _map.Explored[x, y] = true;
                RevealAllSecrets();
                _hud.AddMessage("Странник рисует карту всего этажа.", InfoColor);
                break;
            default:
                int cost = hero.MaxHp / 4;
                if (hero.Hp <= cost || hero.Inventory.IsFull)
                {
                    if (IsLocalHero(hero))
                        _hud.AddMessage("Сделка невозможна: мало здоровья или полна сумка.", DamageColor);
                    return;
                }
                hero.TakeDamage(cost);
                var item = RollEquipment(_floor + 4);
                hero.Inventory.TryAdd(item);
                Tell(hero, $"Странник отдаёт: {item.Name}.", $"{hero.DisplayName} получает: {item.Name}.", item.RarityColor);
                break;
        }
        _shrines.Remove(cell);
        shrine.QueueFree();
        _sound.Play(Sound.Secret);
        UpdateFov();
        UpdateHud();
    }
}
