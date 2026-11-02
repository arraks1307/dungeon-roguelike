using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Часть GameManager про классы: способности (клавиши F и G) и выбор улучшений при повышении уровня.
/// </summary>
public partial class GameManager
{
    private const int SkillRange = 8;

    private ClassSelectPanel _classSelect;
    private LevelUpPanel _levelUpPanel;
    private int _pendingLevelUps;

    // ---------- Способности ----------

    private void UseSkill(int index)
    {
        if (_state != GameState.Playing)
            return;

        var skill = _player.Class.Skills[index];
        int cooldown = _player.SkillCooldowns[index];
        if (cooldown > 0)
        {
            _hud.AddMessage($"«{skill.Name}» будет готов через {cooldown} ход(а).");
            return;
        }

        bool used = skill.Id switch
        {
            SkillId.Whirlwind => Whirlwind(),
            SkillId.SecondWind => SecondWind(),
            SkillId.AimedShot => AimedShot(),
            SkillId.ArrowRain => ArrowRain(),
            SkillId.Fireball => Fireball(),
            SkillId.FrostNova => FrostNova(),
            _ => false,
        };
        if (!used)
            return;

        _player.StartCooldown(index);
        if (_player.IsInvisible && skill.Id != SkillId.SecondWind)
        {
            _player.BreakInvisibility();
            _hud.AddMessage("Способность выдаёт тебя — невидимость пропала.");
        }
        EndPlayerTurn();
    }

    private bool Whirlwind()
    {
        var targets = _enemies.Where(enemy => _map.AreAdjacent(_player.GridPos, enemy.GridPos)).ToList();
        if (targets.Count == 0)
            return NoTargets();
        _hud.AddMessage("Вихрь! Ты бьёшь всех вокруг.", InfoColor);
        Effects.Burst(this, _player.GridPos, _player.Class.Color, 30);
        _sound.Play(Sound.Hit);
        _camera.Shake(0.15f);
        foreach (var enemy in targets)
            HitEnemy(enemy, RollDamage(_player, enemy), Colors.White);
        return true;
    }

    private bool SecondWind()
    {
        if (!_player.IsHurt)
        {
            _hud.AddMessage("Ты и так полностью здоров.");
            return false;
        }
        int amount = _player.MaxHp * 40 / 100;
        _player.Heal(amount);
        Effects.FloatingText(this, _player.GridPos, $"+{amount}", GoodColor);
        Effects.Burst(this, _player.GridPos, GoodColor, 20);
        _sound.Play(Sound.Heal);
        _hud.AddMessage($"Второе дыхание: +{amount} здоровья.", GoodColor);
        return true;
    }

    private bool AimedShot()
    {
        var target = NearestVisibleEnemy();
        if (target == null)
            return NoTargets();
        Effects.Projectile(this, _player.GridPos, target.GridPos, _player.Class.Color);
        _sound.Play(Sound.Shoot);
        bool crit = _rng.Next(100) < _player.Bonuses.Crit;
        int damage = RollDamage(_player, target) + 3;
        if (crit)
            damage *= 2;
        _hud.AddMessage($"Меткий выстрел{(crit ? " — в яблочко!" : "")}: {target.DisplayName} получает {damage}.");
        HitEnemy(target, damage, crit ? CritColor : Colors.White);
        return true;
    }

    private bool ArrowRain()
    {
        var targets = _enemies.Where(enemy => _visible.Contains(enemy.GridPos)).ToList();
        if (targets.Count == 0)
            return NoTargets();
        _hud.AddMessage("Град стрел!", InfoColor);
        _sound.Play(Sound.Shoot);
        foreach (var enemy in targets)
        {
            Effects.Projectile(this, _player.GridPos, enemy.GridPos, _player.Class.Color);
            HitEnemy(enemy, Math.Max(1, _player.Attack - 1 - enemy.Defense), Colors.White);
        }
        return true;
    }

    private bool Fireball()
    {
        var target = NearestVisibleEnemy();
        if (target == null)
            return NoTargets();
        int damage = 6 + 2 * _player.Level;
        var fire = new Color(1f, 0.45f, 0.1f);
        Effects.Projectile(this, _player.GridPos, target.GridPos, fire);
        Effects.Burst(this, target.GridPos, fire, 40);
        _sound.Play(Sound.Fire);
        _camera.Shake(0.2f);
        _hud.AddMessage($"Огненный шар взрывается: {damage} урона!", InfoColor);
        var victims = _enemies.Where(enemy => ChebyshevDistance(enemy.GridPos, target.GridPos) <= 1).ToList();
        foreach (var enemy in victims)
            HitEnemy(enemy, damage, fire);
        return true;
    }

    private bool FrostNova()
    {
        var targets = _enemies.Where(enemy => ChebyshevDistance(enemy.GridPos, _player.GridPos) <= 2).ToList();
        if (targets.Count == 0)
            return NoTargets();
        int damage = 2 + _player.Level;
        var ice = new Color(0.55f, 0.85f, 1f);
        Effects.Burst(this, _player.GridPos, ice, 50);
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

    private bool NoTargets()
    {
        _hud.AddMessage("Рядом нет подходящей цели.");
        return false;
    }

    private Enemy NearestVisibleEnemy() => _enemies
        .Where(enemy => _visible.Contains(enemy.GridPos) && ChebyshevDistance(enemy.GridPos, _player.GridPos) <= SkillRange)
        .OrderBy(enemy => ChebyshevDistance(enemy.GridPos, _player.GridPos))
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

    /// <summary>Показываем окно улучшений, если остались невыбранные (за один ход можно получить несколько уровней).</summary>
    private void TryShowLevelUp()
    {
        if (_pendingLevelUps <= 0 || _state != GameState.Playing)
            return;
        _state = GameState.LevelUp;
        _levelUpPanel.Open(_player.Level - _pendingLevelUps + 1, ClassCatalog.RollPerks(_rng));
    }

    private void OnPerkChosen(Perk perk)
    {
        if (_state != GameState.LevelUp)
            return;
        _player.ApplyPerk(perk);
        _hud.AddMessage($"Улучшение: {perk.Name}.", InfoColor);
        _pendingLevelUps--;
        _levelUpPanel.Visible = false;
        _state = GameState.Playing;
        UpdateFov(); // «Зоркость» сразу меняет обзор
        UpdateHud();
        TryShowLevelUp();
    }
}
