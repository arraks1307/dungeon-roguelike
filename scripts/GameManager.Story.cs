using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Часть GameManager про сюжет и новые святыни: сделки с подземельем, реликварий с выбором из трёх
/// наград, пленники для лагеря, записи хрониста и достижения за поступки.
/// Содержимое святынь решается в момент появления через общий _rng и хранится в самой святыне,
/// поэтому на всех компьютерах отряда игрок выбирает из одних и тех же вариантов.
/// </summary>
public partial class GameManager
{
    private const double PactChance = 0.5;
    private const double ReliquaryChance = 0.2;
    private const float PactHpPenalty = 0.05f;      // каждая сделка делает бездну сильнее: +5% здоровья врагам
    private const float PactDamagePenalty = 0.03f;  // и +3% урона
    private const double CaptiveChance = 0.3;
    private const double NoteChance = 0.45;
    private const int BloodlustPercent = 15;

    /// <summary>Пишем ли в прогресс этого игрока. В сетевом забеге содержимое не зависит от чьего-то профиля.</summary>
    private bool UsesProfile => !_isNetworkGame && !_metaLocked;

    private void SpawnStoryShrines(bool bossFloor)
    {
        if (!bossFloor && _floor >= 2 && _rng.NextDouble() < PactChance * EventScale && TryFindFreeCell(out var pactCell))
        {
            var deals = Enumerable.Range(0, Pacts.All.Length).OrderBy(_ => _rng.Next()).Take(3).ToArray();
            PlaceShrine(pactCell, ShrineKind.Pact, explained: true).Deals = deals;
        }

        if (!bossFloor && _floor >= 2 && _rng.NextDouble() < ReliquaryChance * EventScale && TryFindFreeCell(out var relicCell))
            PlaceReliquary(relicCell, Enumerable.Range(0, 3).Select(_ => RollEquipment(_floor + 3)).ToList());

        if (_floor >= 3 && _rng.NextDouble() < CaptiveChance * EventScale && TryFindFreeCell(out var captiveCell))
        {
            var free = Enumerable.Range(0, Story.Residents.Length)
                .Where(i => !UsesProfile || !Profile.Current.Rescued.Contains(Story.Residents[i].Id)).ToArray();
            if (free.Length > 0)
                PlaceShrine(captiveCell, ShrineKind.Captive, explained: true).Extra = free[_rng.Next(free.Length)];
        }

        if (_rng.NextDouble() < NoteChance * EventScale && TryFindFreeCell(out var noteCell))
        {
            var unread = Enumerable.Range(0, Story.Notes.Length)
                .Where(i => !UsesProfile || !Profile.Current.Notes.Contains(i)).ToArray();
            if (unread.Length > 0)
                PlaceShrine(noteCell, ShrineKind.Note, explained: true).Extra = UsesProfile ? unread[0] : _floor % Story.Notes.Length;
        }
    }

    private void PlaceReliquary(Vector2I cell, List<Item> offer) =>
        PlaceShrine(cell, ShrineKind.Reliquary, explained: true).Offer.AddRange(offer);

    /// <summary>Босс оставляет реликварий с тремя легендарными вещами вместо одной случайной.</summary>
    private void PlaceBossReward(Enemy boss, Vector2I stairs)
    {
        var cell = boss.GridPos;
        if (cell == stairs || IsOccupied(cell))
        {
            var near = DungeonMap.Directions.Select(d => boss.GridPos + d).Where(c => c != stairs && _map.Get(c) == Tile.Floor && !IsOccupied(c)).ToList();
            if (near.Count > 0)
                cell = near[0];
            else if (!TryFindFreeCell(out cell))
                return;
        }
        var offer = new List<Item>();
        for (int i = 0; i < 12 && offer.Count < 3; i++)
        {
            var item = RollLegendaryItem();
            if (offer.All(other => other.Template != item.Template))
                offer.Add(item);
        }
        PlaceReliquary(cell, offer);
        _hud.AddMessage("На месте босса вспыхивает реликварий: выбери награду.", InfoColor);
    }

    /// <summary>В одиночной игре пока закрыты вещи, которые открываются достижениями; в отряде доступны все.</summary>
    private Item RollLegendaryItem() =>
        ItemCatalog.RollLegendary(_rng, UsesProfile ? template => template.Unlock == null || Profile.Has(template.Unlock) : null);

    /// <summary>Окно выбора у странника, тёмного алтаря или реликвария.</summary>
    private void OpenShrineChoice(Player hero, Shrine shrine)
    {
        if (!IsLocalHero(hero) || hero.IsBot || _state != GameState.Playing)
            return;
        if (shrine.Kind == ShrineKind.Wanderer)
        {
            OpenWanderer(hero, shrine);
            return;
        }
        _wandererCell = shrine.GridPos;
        _state = GameState.Event;
        if (shrine.Kind == ShrineKind.Pact)
        {
            _choicePanel.Open("Тёмный алтарь", "Камень шепчет твоё имя. Сила останется с тобой до конца спуска. Плата тоже, а каждая сделка делает врагов сильнее.",
                shrine.Deals.Select(id => $"{Pacts.All[id].Name}: {Pacts.All[id].Offer}").ToList());
            return;
        }
        _choicePanel.Open("Реликварий", "Створки откроются только один раз. Возьми одну вещь, остальные исчезнут.",
            shrine.Offer.Select(item => $"{item.Name}: {item.Template.Special ?? item.ShortEffect()}").ToList());
    }

    private void ApplyPact(Player hero, int choice, Shrine shrine)
    {
        if (choice < 0 || choice >= shrine.Deals.Length)
            return;
        var pact = Pacts.All[shrine.Deals[choice]];
        hero.AddPact(pact);
        Effects.Burst(this, shrine.GridPos, shrine.Glow, 30);
        _sound.Play(Sound.Summon);
        Tell(hero, $"Сделка заключена: {pact.Name}.", $"{hero.DisplayName} заключает сделку: {pact.Name}.", shrine.Glow);
        if (IsLocalHero(hero) && hero.Pacts.Count >= 5)
            Deed("pact5");
        RemoveShrine(shrine);
    }

    private void ApplyReliquary(Player hero, int choice, Shrine shrine)
    {
        if (choice < 0 || choice >= shrine.Offer.Count)
            return;
        if (hero.Inventory.IsFull)
        {
            if (IsLocalHero(hero))
                _hud.AddMessage("Сумка полна: освободи место и вернись.", DamageColor);
            return;
        }
        var item = shrine.Offer[choice];
        hero.Inventory.TryAdd(item);
        _sound.Play(Sound.Chest);
        Tell(hero, $"Ты забираешь: {item.Name}.", $"{hero.DisplayName} забирает: {item.Name}.", item.RarityColor);
        RemoveShrine(shrine);
    }

    private void RescueCaptive(Player hero, Shrine shrine)
    {
        var resident = Story.Residents[shrine.Extra];
        _hud.AddMessage(resident.Rescue, Story.Tint);
        _sound.Play(Sound.LevelUp);
        Effects.Burst(this, shrine.GridPos, shrine.Glow, 24);
        if (UsesProfile)
        {
            Profile.Current.Rescued.Add(resident.Id);
            Profile.Save();
            _hud.AddMessage($"{resident.Name} уходит в лагерь. Там откроется новая постройка.", InfoColor);
            if (Profile.Current.Rescued.Count >= Story.Residents.Length)
                Deed("allresidents");
        }
        RemoveShrine(shrine);
    }

    private void ReadNote(Player hero, Shrine shrine)
    {
        var (title, text) = Story.Notes[shrine.Extra];
        if (UsesProfile)
        {
            Profile.Current.Notes.Add(shrine.Extra);
            Profile.Save();
            if (Profile.Current.Notes.Count >= Story.Notes.Length)
                Deed("allnotes");
        }
        _sound.Play(Sound.Secret);
        RemoveShrine(shrine);
        if (IsLocalHero(hero) && !hero.IsBot)
            OpenStory(title, text);
    }

    private void RemoveShrine(Shrine shrine)
    {
        _shrines.Remove(shrine.GridPos);
        shrine.QueueFree();
        UpdateFov();
        UpdateHud();
    }

    /// <summary>Показать страницу сюжета. Здесь нечего выбирать: окно закрывается кнопкой «Дальше».</summary>
    private void OpenStory(string title, string text)
    {
        if (_state != GameState.Playing)
        {
            _hud.AddMessage($"{title}: {text}", Story.Tint);
            return;
        }
        _state = GameState.Event;
        _choicePanel.Open(title, text, new List<string>(), "Дальше");
    }

    private void ShowIntro()
    {
        if (!UsesProfile || IsTestRun || InTutorial || Profile.Current.IntroSeen)
            return;
        Profile.Current.IntroSeen = true;
        Profile.Save();
        OpenStory(Story.IntroTitle, Story.Intro);
    }

    private void TellFloorStory()
    {
        var line = Story.FloorLine(_floor);
        if (line != null)
            _hud.AddMessage(line, Story.Tint);
    }

    // ---------- Достижения за поступки ----------

    /// <summary>Отметить достижение прямо во время забега. Смерть его уже не отнимет.</summary>
    private void Deed(string id)
    {
        if (_metaLocked)
            return;
        var achievement = Profile.Unlock(id);
        if (achievement == null)
            return;
        Profile.Save();
        _hud.AddMessage($"Достижение: {achievement.Name} (+{achievement.Reward} осколков).", InfoColor);
        if (achievement.Prize != null)
            _hud.AddMessage($"Открыто: {achievement.Prize}.", InfoColor);
    }

    private void OnBossKilled()
    {
        if (_attacker == null || !IsLocalHero(_attacker))
            return;
        var inventory = _attacker.Inventory;
        if (inventory.GetEquipped(EquipSlot.Armor) == null && inventory.GetEquipped(EquipSlot.Helmet) == null)
            Deed("noarmor");
        if (inventory.GetEquipped(EquipSlot.Weapon) == null)
            Deed("bareboss");
    }

    private void CheckFloorDeeds()
    {
        if (_floor >= 6 && _localHero.GoldSpent == 0)
            Deed("beggar");
    }

    /// <summary>Сильнейший удар забега: для сводки и достижения.</summary>
    private void NoteHit(Player hero, int damage)
    {
        hero.BiggestHit = Math.Max(hero.BiggestHit, damage);
        if (damage >= 100 && IsLocalHero(hero))
            Deed("overkill");
    }

    // ---------- Легендарные вещи, меняющие правила ----------

    /// <summary>Копьё пронзания бьёт врага позади цели, коса жнеца добивает ослабевших.</summary>
    private void ApplyWeaponRules(Player hero, Enemy enemy, int damage)
    {
        if (hero.HasLegendary(LegendaryEffect.Pierce))
        {
            var behind = EnemyAt(enemy.GridPos + (enemy.GridPos - hero.GridPos));
            if (behind != null)
                HitEnemy(behind, damage, new Color(0.7f, 0.9f, 1f));
        }
        if (hero.HasLegendary(LegendaryEffect.Reaper) && !enemy.IsDead && !enemy.IsBoss && enemy.Hp * 5 < enemy.MaxHp)
        {
            enemy.TakeDamage(enemy.Hp);
            Effects.FloatingText(this, enemy.GridPos, "Жатва!", CritColor);
        }
    }

    private int PotionPower(Player hero, int power) => hero.HasPact(PactRule.WeakPotions) ? power / 2 : power;

    private int GoldFor(Player hero, int gold) => hero.HasPact(PactRule.Poverty) ? Math.Max(1, gold / 2) : gold;

    private int PactCount => _heroes.Sum(hero => hero.Pacts.Count);

    /// <summary>Во сколько раз крепче враги: сила из сделок возвращается бездне, как и записано в сюжете.</summary>
    private float PactEnemyHp => 1f + PactCount * PactHpPenalty
        + _heroes.Sum(hero => hero.Pacts.Count(pact => pact.Rule == PactRule.Bloodlust)) * BloodlustPercent / 100f;

    private float PactEnemyDamage => 1f + PactCount * PactDamagePenalty;

    // ---------- Сводка забега ----------

    private void ShowRunSummary(bool victory, string title, string subtitle, string details)
    {
        var hero = _localHero;
        var stats = new List<string>
        {
            $"Герой: {hero.Class.Name}, уровень {hero.Level}",
            $"Сложность: {_difficulty.Name.ToLower()}",
            $"Этаж: {_floor}   Ходов: {_turns}",
            $"Побеждено врагов: {_kills}",
            $"Лучший удар: {hero.BiggestHit}",
            $"Весь нанесённый урон: {hero.DamageDealt}",
            $"Золото: {hero.Gold} (потрачено {hero.GoldSpent})",
            $"Выпито зелий: {hero.PotionsDrunk}",
        };
        _hud.Summary.Open(victory, title, subtitle, stats, hero.Inventory.Equipped.ToList(),
            hero.Pacts.Select(pact => pact.Name).ToList(), details.Trim(), RestartHint);
    }

    // ---------- Новые способности берсерка ----------

    private bool Rampage(Player hero)
    {
        var target = _enemies.Where(enemy => _map.AreAdjacent(hero.GridPos, enemy.GridPos)).OrderByDescending(enemy => enemy.Hp).FirstOrDefault();
        if (target == null)
            return NoTargets(hero);
        int cost = hero.MaxHp / 10;
        if (hero.Hp <= cost)
        {
            if (IsLocalHero(hero))
                _hud.AddMessage("Слишком мало здоровья для такого удара.", DamageColor);
            return false;
        }
        hero.TakeDamage(cost);
        if (IsInvulnerable)
            hero.Heal(cost);
        Effects.FloatingText(this, hero.GridPos, $"-{cost}", DamageColor);
        hero.PlayAttack(target.GridPos);
        int damage = ApplyTeamwork(hero, target, RollDamage(hero, target) * 3 + PerkBonusDamage(hero, target));
        NoteHit(hero, damage);
        _sound.Play(Sound.Hit);
        Effects.Flash(this, target.GridPos, CritColor, 4f);
        HitEnemy(target, damage, CritColor);
        return true;
    }

    private bool Bloodlust(Player hero)
    {
        int nearby = _enemies.Count(enemy => _map.AreAdjacent(hero.GridPos, enemy.GridPos));
        if (nearby == 0)
            return NoTargets(hero);
        int amount = hero.MaxHp * 15 / 100 * nearby;
        hero.Heal(amount);
        Effects.FloatingText(this, hero.GridPos, $"+{amount}", GoodColor);
        Effects.Burst(this, hero.GridPos, new Color(0.9f, 0.15f, 0.15f), 24);
        _sound.Play(Sound.Heal);
        Tell(hero, $"Жажда битвы: +{amount} здоровья.", $"{hero.DisplayName}: жажда битвы.", GoodColor);
        return true;
    }
}
