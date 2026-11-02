using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Часть GameManager для самопроверки: игра сама делает ходы, сохраняет скриншот и закрывается.
/// Включается только аргументами командной строки после «--», например:
/// -- --screenshot=shot.png --moves=40 --view=inventory --start-floor=3 --god
/// </summary>
public partial class GameManager
{
    private string _screenshotPath;
    private string _screenshotView = "play";
    private int _autoMoves;
    private int _frame;
    private int _startFloor = 1;
    private bool _godMode; // игрок не может погибнуть

    private void ParseCommandLine()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--screenshot="))
                _screenshotPath = arg["--screenshot=".Length..];
            else if (arg.StartsWith("--moves="))
                _autoMoves = int.Parse(arg["--moves=".Length..]);
            else if (arg.StartsWith("--view="))
                _screenshotView = arg["--view=".Length..];
            else if (arg == "--god")
                _godMode = true;
            else if (arg.StartsWith("--start-floor="))
                _startFloor = int.Parse(arg["--start-floor=".Length..]);
        }

        // Самопроверка не должна трогать настоящее сохранение игрока
        if (_screenshotPath != null)
        {
            SaveSystem.FilePath = "user://selftest_save.json";
            SaveSystem.Delete();
        }
    }

    /// <summary>Короткая сводка о мире — чтобы сравнить состояние до сохранения и после загрузки.</summary>
    private string WorldSummary() =>
        $"этаж {_floor}, убито {_kills}, позиция {_player.GridPos}, здоровье {_player.Hp}/{_player.MaxHp}, " +
        $"атака {_player.Attack}, золото {_player.Gold}, сумка {_player.Inventory.Items.Count}, " +
        $"надето {_player.Inventory.Equipped.Count()}, врагов {_enemies.Count}, предметов на полу {_floorItems.Count}, " +
        $"торговец {(_merchant != null ? "есть" : "нет")}, сундуков {_chests.Count}, ловушек {_map.Traps.Count}, " +
        $"тема {_map.Theme.Name}";

    /// <summary>
    /// --view=menu — главное меню, scores — рекорды, pause / inventory / shop — соответствующее окно
    /// после ходов, play (по умолчанию) — просто игра. Во время ходов иногда применяется случайный
    /// предмет из сумки — так проверяются все предметы.
    /// </summary>
    private void RunSelfTest()
    {
        _frame++;
        bool inMenu = _screenshotView is "menu" or "scores";

        if (_frame == 1)
        {
            if (_screenshotView == "scores")
                _mainMenu.ShowScores();
            else if (!inMenu)
                StartNewGame();
        }

        int moveFrames = inMenu ? 0 : _autoMoves * 3;
        if (_frame <= moveFrames)
        {
            if (_frame % 3 == 0)
                OnPlayerAction(AutoPilotDirection());
            if (_frame % 30 == 0 && _state == GameState.Playing && _player.Inventory.Items.Count > 0)
                QuickUse(_rng.Next(_player.Inventory.Items.Count));
            if (_state == GameState.Shop)
                CloseShop(); // автопилот случайно зашёл к торговцу
            return;
        }

        if (_frame == moveFrames + 1 && _state == GameState.Playing)
            OpenTestView();

        if (_frame == moveFrames + 40)
        {
            GD.Print($"Самопроверка: этаж {_floor}, убито {_kills}, здоровье {_player.Hp}, " +
                     $"золото {_player.Gold}, предметов {_player.Inventory.Items.Count}, состояние {_state}");
            GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
            GetTree().Quit();
        }
    }

    private void OpenTestView()
    {
        switch (_screenshotView)
        {
            case "pause":
                Pause();
                break;
            case "inventory":
                // Набиваем сумку случайными вещами, чтобы было что показать
                while (_player.Inventory.Items.Count < 8)
                    _player.Inventory.TryAdd(ItemCatalog.Roll(_rng, _floor + 4));
                OpenInventory();
                break;
            case "shop":
                _player.Gold = 150;
                if (_merchant == null)
                    SpawnMerchant();
                OpenShop();
                break;
            case "settings":
                Pause();
                OpenSettings();
                break;
            case "reveal":
                // Показать этаж целиком: удобно смотреть темы, двери, ловушки и декор
                for (int x = 0; x < _map.Width; x++)
                    for (int y = 0; y < _map.Height; y++)
                        _map.Explored[x, y] = true;
                RevealAllSecrets();
                UpdateFov();
                _camera.Zoom(6); // отдаляем камеру
                break;
            case "saveload":
                // Сохраняем, уходим в меню (мир очищается), загружаем и сравниваем сводки
                string before = WorldSummary();
                SaveRun();
                ShowMainMenu();
                ContinueGame();
                string after = WorldSummary();
                GD.Print($"До сохранения:   {before}");
                GD.Print($"После загрузки:  {after}");
                GD.Print(before == after ? "Сохранение: СОВПАДАЕТ" : "Сохранение: РАЗЛИЧАЕТСЯ");
                break;
        }
    }

    /// <summary>Автопилот: чаще идёт к лестнице, иногда шагает случайно.</summary>
    private Vector2I AutoPilotDirection()
    {
        var randomDirection = DungeonMap.Directions[_rng.Next(DungeonMap.Directions.Length)];
        if (_rng.NextDouble() < 0.3)
            return randomDirection;

        var distances = _map.DistanceField(_map.StairsPos, MapWidth * MapHeight);
        if (!distances.TryGetValue(_player.GridPos, out int best))
            return randomDirection;

        var result = randomDirection;
        foreach (var direction in DungeonMap.Directions)
        {
            var next = _player.GridPos + direction;
            if (distances.TryGetValue(next, out int distance) && distance < best && _map.CanMove(_player.GridPos, next))
            {
                best = distance;
                result = direction;
            }
        }
        return result;
    }
}
