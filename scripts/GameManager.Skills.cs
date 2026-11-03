using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Часть GameManager про классы: способности (клавиши F и G) и выбор улучшений при повышении уровня.
/// Способность применяет конкретный герой; окно улучшений — у своего героя.
/// </summary>
public partial class GameManager
{
    private const int SkillRange = 8;

    private ClassSelectPanel _classSelect;
    private LevelUpPanel _levelUpPanel;

    // ---------- Способности ----------

    /// <summary>Применить способность героя. Возвращает true, если ход потрачен.</summary>
    private bool TryUseSkill(Player hero, int index)
    {
        var skill = hero.Class.Skills[index];
        int cooldown = hero.SkillCooldowns[index];
        if (cooldown > 0)
        {
            if (IsLocalHero(hero))
                _hud.AddMessage($"«{skill.Name}» будет готов через {cooldown} ход(а).");
            return false;
        }

        bool used = skill.Id switch
        {
            SkillId.Whirlwind => Whirlwind(hero),
            SkillId.SecondWind => SecondWind(hero),
            SkillId.AimedShot => AimedShot(hero),
            SkillId.ArrowRain => ArrowRain(hero),
            SkillId.Fireball => Fireball(hero),
            SkillId.FrostNova => FrostNova(hero),
            _ => false,
        };
        if (!used)
            return false;

        hero.StartCooldown(index);
        if (hero.IsInvisible && skill.Id != SkillId.SecondWind)
        {
            hero.BreakInvisibility();
            Tell(hero, "Способность выдаёт тебя — невидимость пропала.", $"{hero.DisplayName} выдаёт себя способностью.");
        }
        return true;
    }

    private bool Whirlwind(Player hero)
    {
        var targets = _enemies.Where(enemy => _map.AreAdjacent(hero.GridPos, enemy.GridPos)).ToList();
        if (targets.Count == 0)
            return NoTargets(hero);
        Tell(hero, "Вихрь! Ты бьёшь всех вокруг.", $"{hero.DisplayName}: вихрь!", InfoColor);
        Effects.Burst(this, hero.GridPos, hero.Class.Color, 30);
        _sound.Play(Sound.Hit);
        if (IsLocalHero(hero))
            _camera.Shake(0.15f);
        foreach (var enemy in targets)
            HitEnemy(enemy, RollDamage(hero, enemy), Colors.White);
        return true;
    }

    private bool SecondWind(Player hero)
    {
        if (!hero.IsHurt)
        {
            if (IsLocalHero(hero))
                _hud.AddMessage("Ты и так полностью здоров.");
            return false;
        }
        int amount = hero.MaxHp * 40 / 100;
        hero.Heal(amount);
        Effects.FloatingText(this, hero.GridPos, $"+{amount}", GoodColor);
        Effects.Burst(this, hero.GridPos, GoodColor, 20);
        _sound.Play(Sound.Heal);
        Tell(hero, $"Второе дыхание: +{amount} здоровья.", $"{hero.DisplayName}: второе дыхание.", GoodColor);
        return true;
    }

    private bool AimedShot(Player hero)
    {
        var target = NearestVisibleEnemy(hero);
        if (target == null)
            return NoTargets(hero);
        Effects.Projectile(this, hero.GridPos, target.GridPos, hero.Class.Color);
        _sound.Play(Sound.Shoot);
        bool crit = _rng.Next(100) < hero.Bonuses.Crit;
        int damage = RollDamage(hero, target) + 5;
        if (crit)
            damage *= 2;
        _hud.AddMessage($"Меткий выстрел{(crit ? " — в яблочко!" : "")}: {target.DisplayName} получает {damage}.");
        HitEnemy(target, damage, crit ? CritColor : Colors.White);
        return true;
    }

    private bool ArrowRain(Player hero)
    {
        var targets = _enemies.Where(enemy => _visible.Contains(enemy.GridPos)).ToList();
        if (targets.Count == 0)
            return NoTargets(hero);
        _hud.AddMessage("Град стрел!", InfoColor);
        _sound.Play(Sound.Shoot);
        foreach (var enemy in targets)
        {
            Effects.Projectile(this, hero.GridPos, enemy.GridPos, hero.Class.Color);
            HitEnemy(enemy, Math.Max(1, hero.Attack - 1 - enemy.Defense), Colors.White);
        }
        return true;
    }

    private bool Fireball(Player hero)
    {
        var target = NearestVisibleEnemy(hero);
        if (target == null)
            return NoTargets(hero);
        int damage = 6 + 2 * hero.Level;
        var fire = new Color(1f, 0.45f, 0.1f);
        Effects.Projectile(this, hero.GridPos, target.GridPos, fire);
        Effects.Burst(this, target.GridPos, fire, 40);
        _sound.Play(Sound.Fire);
        _camera.Shake(0.2f);
        _hud.AddMessage($"Огненный шар взрывается: {damage} урона!", InfoColor);
        var victims = _enemies.Where(enemy => ChebyshevDistance(enemy.GridPos, target.GridPos) <= 1).ToList();
        foreach (var enemy in victims)
            HitEnemy(enemy, damage, fire);
        return true;
    }

    private bool FrostNova(Player hero)
    {
        var targets = _enemies.Where(enemy => ChebyshevDistance(enemy.GridPos, hero.GridPos) <= 2).ToList();
        if (targets.Count == 0)
            return NoTargets(hero);
        int damage = 2 + hero.Level;
        var ice = new Color(0.55f, 0.85f, 1f);
        Effects.Burst(this, hero.GridPos, ice, 50);
        _sound.Play(Sound.Teleport);
        _hud.AddMessage($"Ледяная нова: {damage} урона и заморозка!", InfoColor);
        foreach (var enemy in targets)
        {
            HitEnemy(enemy, damage, ice);
            if (!enemy.IsDead)
                enemy.Freeze(3);
        }
        return true;
    }

    private bool NoTargets(Player hero)
    {
        if (IsLocalHero(hero))
            _hud.AddMessage("Рядом нет подходящей цели.");
        return false;
    }

    private Enemy NearestVisibleEnemy(Player hero) => _enemies
        .Where(enemy => _visible.Contains(enemy.GridPos) && ChebyshevDistance(enemy.GridPos, hero.GridPos) <= SkillRange)
        .OrderBy(enemy => ChebyshevDistance(enemy.GridPos, hero.GridPos))
        .FirstOrDefault();

    /// <summary>Нанести урон врагу с цифрой и частицами; убить, если здоровье кончилось.</summary>
    private void HitEnemy(Enemy enemy, int damage, Color color)
    {
        if (enemy.IsDead)
            return;
        enemy.TakeDamage(damage);
        Effects.FloatingText(this, enemy.GridPos, damage.ToString(), color);
        Effects.Burst(this, enemy.GridPos, color, 10);
        if (enemy.IsDead)
            KillEnemy(enemy);
    }

    // ---------- Выбор класса и улучшений ----------

    private void OpenClassSelect()
    {
        _mainMenu.Visible = false;
        _classSelect.Open();
    }

    private void CloseClassSelect()
    {
        _classSelect.Visible = false;
        _mainMenu.ShowMain();
    }

    /// <summary>
    /// Раздать улучшения за новые уровни. Ботам (самопроверка) — сразу случайное;
    /// своему герою — окно выбора, если сейчас не открыто никакое другое окно.
    /// </summary>
    private void TryShowLevelUp()
    {
        foreach (var hero in _heroes.Where(hero => hero.IsBot))
        {
            while (hero.PendingLevelUps > 0)
            {
                hero.ApplyPerk(ClassCatalog.RollPerks(_rng)[0]);
                hero.PendingLevelUps--;
            }
        }

        var local = _localHero;
        if (local == null || local.IsBot || local.PendingLevelUps <= 0 || _state != GameState.Playing)
            return;
        _state = GameState.LevelUp;
        _levelUpPanel.Open(local.Level - local.PendingLevelUps + 1, ClassCatalog.RollPerks(_rng));
    }

    private void OnPerkChosen(Perk perk)
    {
        if (_state != GameState.LevelUp)
            return;
        _localHero.ApplyPerk(perk);
        _hud.AddMessage($"Улучшение: {perk.Name}.", InfoColor);
        _localHero.PendingLevelUps--;
        _levelUpPanel.Visible = false;
        _state = GameState.Playing;
        UpdateFov(); // «Зоркость» сразу меняет обзор
        UpdateHud();
        TryShowLevelUp();
    }
}
