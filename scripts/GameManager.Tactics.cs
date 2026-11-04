using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Часть GameManager про тактику боя: защитная стойка, опасные удары врагов (их видно заранее),
/// взрывы, шаманы и святыни на этажах. Всё это меняет мир, поэтому случайность берётся только из _rng.
/// </summary>
public partial class GameManager
{
    private const float GuardShare = 0.5f;      // какая доля урона проходит сквозь стойку
    private const float SentinelShare = 0.25f;  // то же с улучшением «Страж»
    private const int ShamanHealBase = 4;
    private const float EliteHp = 1.7f;

    private readonly Dictionary<Vector2I, Shrine> _shrines = new();

    // ---------- Удары врагов, которые видно заранее ----------

    public void EnemyTelegraphs(Enemy enemy, string text)
    {
        Effects.FloatingText(this, enemy.GridPos, "!", DamageColor);
        if (IsVisibleToPlayer(enemy.GridPos))
            _hud.AddMessage(text, DamageColor);
        _sound.Play(Sound.Door);
    }

    /// <summary>Тяжёлый удар по клетке: если герой успел уйти, удар приходится в пустоту.</summary>
    public void EnemyHeavyHit(Enemy enemy, Vector2I cell, float power)
    {
        enemy.PlayAttack(cell);
        var target = LivingHeroes.FirstOrDefault(hero => hero.GridPos == cell);
        if (target == null)
        {
            Effects.FloatingText(this, cell, "мимо", Colors.White);
            _sound.Play(Sound.Step);
            return;
        }
        _camera.Shake(0.2f);
        EnemyAttacksPlayer(enemy, target, ranged: false, power);
    }

    /// <summary>Взрыв подрывника: достаётся всем рядом, и героям, и другим врагам.</summary>
    public void EnemyExplodes(Enemy enemy, float power)
    {
        var center = enemy.GridPos;
        Effects.Burst(this, center, new Color(1f, 0.55f, 0.15f), 60);
        _sound.Play(Sound.Fire);
        _camera.Shake(0.3f);
        if (IsVisibleToPlayer(center))
            _hud.AddMessage($"{enemy.DisplayName} взрывается!", DamageColor);

        foreach (var hero in LivingHeroes.ToList())
            if (ChebyshevDistance(hero.GridPos, center) <= 1 && !_gameOver)
                EnemyAttacksPlayer(enemy, hero, ranged: true, power);
        if (_gameOver)
            return;

        foreach (var other in _enemies.Where(other => other != enemy && ChebyshevDistance(other.GridPos, center) <= 1).ToList())
            HitEnemy(other, enemy.Attack * 2, new Color(1f, 0.55f, 0.15f));
        if (!_gameOver && !enemy.IsDead)
        {
            enemy.TakeDamage(enemy.Hp);
            KillEnemy(enemy);
        }
    }

    /// <summary>Удар босса по площади: достаётся всем, кто не успел отойти.</summary>
    public void EnemySlam(Enemy enemy, int radius, float power)
    {
        Effects.Burst(this, enemy.GridPos, enemy.Type.Color, 60);
        _sound.Play(Sound.Kill);
        _camera.Shake(0.35f);
        foreach (var hero in LivingHeroes.ToList())
            if (ChebyshevDistance(hero.GridPos, enemy.GridPos) <= radius && !_gameOver)
                EnemyAttacksPlayer(enemy, hero, ranged: true, power);
    }

    public Enemy FindHurtAlly(Enemy healer, int range) => _enemies
        .Where(other => other != healer && other.Hp < other.MaxHp && ChebyshevDistance(other.GridPos, healer.GridPos) <= range)
        .OrderBy(other => other.Hp)
        .FirstOrDefault();

    public void ShamanHeals(Enemy shaman, Enemy ally)
    {
        int amount = ShamanHealBase + _floor;
        ally.RestoreHp(amount);
        Effects.Projectile(this, shaman.GridPos, ally.GridPos, GoodColor);
        Effects.FloatingText(this, ally.GridPos, $"+{amount}", GoodColor);
        _sound.Play(Sound.Heal);
    }

    /// <summary>Сколько урона пройдёт сквозь защитную стойку. Блок ближнего удара заряжает способности.</summary>
    private int ApplyGuard(Player hero, int damage, bool ranged)
    {
        if (!hero.IsGuarding)
            return damage;
        float share = hero.Has(PerkEffect.Sentinel) ? SentinelShare : GuardShare;
        Effects.FloatingText(this, hero.GridPos, "Блок", InfoColor);
        _sound.Play(Sound.Equip);
        if (!ranged)
            hero.ChargeSkills();
        return Math.Max(1, (int)MathF.Ceiling(damage * share));
    }

    /// <summary>Поднятый щит врага принимает на себя почти весь удар оружием или стрелой.</summary>
    private int ApplyEnemyShield(Enemy enemy, int damage)
    {
        if (!enemy.ShieldUp)
            return damage;
        Effects.FloatingText(this, enemy.GridPos, "Щит!", Colors.White);
        _sound.Play(Sound.Equip);
        return 1;
    }

    /// <summary>Побочные эффекты улучшений после удара оружием.</summary>
    private void ApplyMeleePerks(Player hero, Enemy target, int damage)
    {
        if (!hero.Has(PerkEffect.Cleave))
            return;
        foreach (var other in _enemies.Where(other => other != target && _map.AreAdjacent(hero.GridPos, other.GridPos)).ToList())
            HitEnemy(other, Math.Max(1, damage / 2), Colors.White);
    }

    private static int PerkBonusDamage(Player hero, Enemy target)
    {
        int bonus = 0;
        if (hero.Has(PerkEffect.Rage))
            bonus += (hero.MaxHp - hero.Hp) * 4 / hero.MaxHp; // +1 за каждую потерянную четверть здоровья
        if (hero.Has(PerkEffect.Hunter) && target.Hp == target.MaxHp)
            bonus += 4;
        return bonus;
    }

    // ---------- Святыни ----------

    private int GoldAltarPrice => 60 + _floor * 15;

    private void SpawnShrines()
    {
        int count = (_rng.NextDouble() < 0.6 ? 1 : 0) + (_floor % 3 == 0 ? 1 : 0);
        for (int i = 0; i < count; i++)
            if (TryFindFreeCell(out var cell))
                PlaceShrine(cell, (ShrineKind)_rng.Next(3), explained: false);
    }

    private void PlaceShrine(Vector2I cell, ShrineKind kind, bool explained)
    {
        var shrine = Shrine.Create(cell, kind);
        shrine.Explained = explained;
        AddChild(shrine);
        shrine.Visible = _map.Explored[cell.X, cell.Y];
        _shrines[cell] = shrine;
    }

    private string ShrineText(Shrine shrine) => shrine.Kind switch
    {
        ShrineKind.Fountain => "Фонтан: лечит половину здоровья.",
        ShrineKind.BloodAltar => IsSinglePlayer ? "Алтарь крови: заберёт треть здоровья и навсегда даст +1 к атаке." : "Алтарь крови: у всего отряда заберёт треть здоровья и навсегда даст +1 к атаке. Нужны голоса всех.",
        _ => $"Золотой алтарь: за {GoldAltarPrice} золота даст хорошую вещь.",
    };

    /// <summary>Шаг в святыню: первый раз — описание, второй — сделка. true — ход потрачен.</summary>
    private bool UseShrine(Player hero, Shrine shrine)
    {
        if (!shrine.Explained)
        {
            shrine.Explained = true;
            if (IsLocalHero(hero))
                _hud.AddMessage($"{ShrineText(shrine)} Шагни ещё раз, чтобы согласиться.", shrine.Glow);
            return false;
        }

        switch (shrine.Kind)
        {
            case ShrineKind.Fountain:
                if (!hero.IsHurt)
                {
                    Tell(hero, "Ты и так полностью здоров.", $"{hero.DisplayName} и так здоров.");
                    return false;
                }
                hero.Heal(hero.MaxHp / 2);
                _sound.Play(Sound.Heal);
                Tell(hero, "Вода затягивает раны.", $"{hero.DisplayName} пьёт из фонтана.", GoodColor);
                break;

            case ShrineKind.BloodAltar:
                // Сделка на весь отряд: кровь отдают все, поэтому решают голосованием
                shrine.Votes.Add(hero.Id);
                int needed = ActiveHeroCount;
                if (shrine.Votes.Count < needed)
                {
                    _hud.AddMessage($"{hero.DisplayName} голосует за сделку с алтарём: {shrine.Votes.Count} из {needed}.", DamageColor);
                    return true;
                }
                if (LivingHeroes.Any(other => other.Hp <= other.MaxHp / 3))
                {
                    shrine.Votes.Clear();
                    _hud.AddMessage("Алтарь убьёт кого-то из отряда: сначала подлечитесь.", DamageColor);
                    return false;
                }
                foreach (var ally in LivingHeroes)
                {
                    int cost = ally.MaxHp / 3;
                    ally.TakeDamage(cost);
                    ally.AddPermanentAttack(1);
                    Effects.FloatingText(this, ally.GridPos, $"-{cost}", DamageColor);
                }
                _sound.Play(Sound.Summon);
                _hud.AddMessage("Алтарь принимает кровь: +1 к атаке навсегда.", DamageColor);
                break;

            default:
                if (hero.Gold < GoldAltarPrice)
                {
                    if (IsLocalHero(hero))
                        _hud.AddMessage($"Нужно {GoldAltarPrice} золота.", DamageColor);
                    return false;
                }
                if (hero.Inventory.IsFull)
                {
                    if (IsLocalHero(hero))
                        _hud.AddMessage("Сумка полна.", DamageColor);
                    return false;
                }
                hero.Gold -= GoldAltarPrice;
                var item = RollEquipment(_floor + 5);
                hero.Inventory.TryAdd(item);
                _sound.Play(Sound.Chest);
                Tell(hero, $"Алтарь дарит: {item.Name}.", $"{hero.DisplayName} получает: {item.Name}.", item.RarityColor);
                break;
        }

        Effects.Burst(this, shrine.GridPos, shrine.Glow, 24);
        _shrines.Remove(shrine.GridPos);
        shrine.QueueFree();
        return true;
    }
}
