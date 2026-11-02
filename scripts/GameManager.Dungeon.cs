using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Часть GameManager про устройство этажа: темы, двери, ловушки, сундуки и секреты.
/// </summary>
public partial class GameManager
{
    private const double MimicChance = 0.12;
    private const double TrapSpotChance = 0.15;   // шанс заметить ловушку рядом за ход
    private const double SecretSpotChance = 0.25; // шанс заметить потайную дверь рядом за ход

    private readonly Dictionary<Vector2I, Chest> _chests = new();
    private Godot.Environment _environment;

    /// <summary>Освещение этажа зависит от темы: в пещерах — зеленоватое, в крипте — мертвенное.</summary>
    private void ApplyTheme()
    {
        var theme = _map.Theme;
        _environment.AmbientLightColor = theme.Ambient;
        _environment.AmbientLightEnergy = theme.AmbientEnergy;
        // Туман подкрашен в цвет темы: синеватый в подземелье, зеленоватый в пещерах и крипте
        _environment.VolumetricFogAlbedo = theme.Ambient.Lightened(0.4f);
    }

    // ---------- Двери ----------

    public void OpenDoor(Vector2I cell)
    {
        if (_map.Get(cell) != Tile.Door)
            return;
        _map.Tiles[cell.X, cell.Y] = Tile.OpenDoor;
        _renderer.OpenDoor(cell);
        _sound.Play(Sound.Door);
    }

    // ---------- Ловушки ----------

    private void SpawnTraps()
    {
        int count = Math.Min(2 + _floor / 3, 8);
        for (int i = 0; i < count; i++)
        {
            if (!TryFindFreeCell(out var cell))
                return;
            var kind = (TrapKind)_rng.Next(3);
            _map.Traps[cell] = new Trap { Kind = kind };
        }
    }

    /// <summary>Игрок наступил на клетку — если там ловушка, она срабатывает.</summary>
    private void CheckTrap(Vector2I cell)
    {
        if (!_map.Traps.TryGetValue(cell, out var trap))
            return;
        trap.Revealed = true;

        switch (trap.Kind)
        {
            case TrapKind.Spikes:
                int damage = 2 + _floor / 2;
                _player.TakeDamage(damage);
                if (_godMode)
                    _player.Heal(damage);
                Effects.FloatingText(this, cell, $"-{damage}", DamageColor);
                Effects.Burst(this, cell, new Color(0.8f, 0.8f, 0.85f), 10);
                _sound.Play(Sound.Hurt);
                _camera.Shake(0.15f);
                _hud.AddMessage($"Из пола выскакивают шипы! -{damage}.", DamageColor);
                if (_player.IsDead)
                    GameOver("ловушка с шипами");
                break;

            case TrapKind.Alarm:
                _map.Traps.Remove(cell);
                foreach (var enemy in _enemies)
                    enemy.Alerted = true;
                _sound.Play(Sound.Summon);
                _hud.AddMessage("Ловушка-сирена! Все враги этажа знают, где ты.", DamageColor);
                break;

            case TrapKind.Teleport:
                _map.Traps.Remove(cell);
                if (TryFindFreeCell(out var destination, includeStartRoom: true))
                {
                    Effects.Burst(this, cell, new Color(0.4f, 0.6f, 1f), 20);
                    _player.Teleport(destination);
                    _camera.SnapToTarget();
                    _sound.Play(Sound.Teleport);
                    _hud.AddMessage("Руна телепорта уносит тебя неизвестно куда!", InfoColor);
                    TryPickUpItem(destination);
                }
                break;
        }
    }

    /// <summary>
    /// В конце хода игрок осматривается: с некоторым шансом замечает ловушки
    /// и потайные двери в соседних клетках.
    /// </summary>
    private void SearchSurroundings()
    {
        foreach (var direction in DungeonMap.Directions)
        {
            var cell = _player.GridPos + direction;
            if (_map.Traps.TryGetValue(cell, out var trap) && !trap.Revealed && _rng.NextDouble() < TrapSpotChance)
            {
                trap.Revealed = true;
                _hud.AddMessage("Ты замечаешь ловушку.", InfoColor);
            }
            if (_map.Get(cell) == Tile.SecretDoor && _rng.NextDouble() < SecretSpotChance)
                RevealSecretDoor(cell);
        }
    }

    private void RevealSecretDoor(Vector2I cell)
    {
        _map.Tiles[cell.X, cell.Y] = Tile.Door;
        _renderer.RevealSecretDoor(cell);
        _sound.Play(Sound.Secret);
        _hud.AddMessage("В стене обнаруживается потайная дверь!", InfoColor);
    }

    /// <summary>Свиток карты открывает ещё и все ловушки с потайными дверями.</summary>
    private void RevealAllSecrets()
    {
        foreach (var trap in _map.Traps.Values)
            trap.Revealed = true;
        for (int x = 0; x < _map.Width; x++)
            for (int y = 0; y < _map.Height; y++)
                if (_map.Tiles[x, y] == Tile.SecretDoor)
                    RevealSecretDoor(new Vector2I(x, y));
    }

    // ---------- Сундуки ----------

    private void SpawnChests()
    {
        int count = _rng.Next(1, 3);
        for (int i = 0; i < count; i++)
            if (TryFindFreeCell(out var cell))
                PlaceChest(cell, mimic: _floor >= 3 && _rng.NextDouble() < MimicChance, secret: false);

        // В секретной комнате всегда честный сундук с хорошей добычей
        foreach (var room in _map.SecretRooms)
            PlaceChest(DungeonGenerator.Center(room), mimic: false, secret: true);
    }

    private void PlaceChest(Vector2I cell, bool mimic, bool secret)
    {
        var chest = Chest.Create(cell, mimic, secret);
        AddChild(chest);
        chest.Visible = _map.Explored[cell.X, cell.Y];
        _chests[cell] = chest;
    }

    /// <summary>Шагнули в сундук: либо добыча, либо зубастый сюрприз. Тратит ход.</summary>
    private void OpenChest(Chest chest)
    {
        _chests.Remove(chest.GridPos);
        chest.QueueFree();

        if (chest.IsMimic)
        {
            var mimic = SpawnEnemy(Enemy.Mimic, chest.GridPos);
            mimic.Alerted = true;
            _sound.Play(Sound.Summon);
            _camera.Shake(0.2f);
            _hud.AddMessage("Сундук распахивает зубастую пасть — это мимик!", DamageColor);
            return;
        }

        Effects.Burst(this, chest.GridPos, new Color(1f, 0.8f, 0.3f), 24);
        _sound.Play(Sound.Chest);

        int bonusFloors = chest.IsSecret ? 3 : 1;
        int gold = (chest.IsSecret ? 30 : 10) + _floor * 3;
        _player.Gold += gold;
        Effects.FloatingText(this, chest.GridPos, $"+{gold} зол.", InfoColor);
        _hud.AddMessage($"В сундуке {gold} золота и добыча!", InfoColor);

        // Первая вещь ложится на место сундука, вторая (в секретном сундуке) — в соседнюю свободную клетку
        PlaceItem(ItemCatalog.Roll(_rng, _floor + bonusFloors), chest.GridPos);
        if (chest.IsSecret)
        {
            var free = DungeonMap.Directions
                .Select(d => chest.GridPos + d)
                .FirstOrDefault(c => _map.Get(c) == Tile.Floor && !IsOccupied(c) && !_floorItems.ContainsKey(c), chest.GridPos);
            if (free != chest.GridPos)
                PlaceItem(ItemCatalog.Roll(_rng, _floor + bonusFloors), free);
        }
    }
}
