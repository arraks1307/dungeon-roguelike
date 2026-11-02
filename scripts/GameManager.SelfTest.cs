using System.Collections.Generic;
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
    private ClassDef _testClass; // null — случайный класс
    private DifficultyDef _testDifficulty; // null — по очереди или обычная

    // Симуляция: N забегов подряд без графики, в конце — сводка для настройки баланса
    private const int SimTurnLimit = 5000;
    private const int SimActionsPerFrame = 200;
    private int _simulateGames;
    private int _simGame;
    private int _simActions;
    private readonly List<SimResult> _simResults = new();

    private record SimResult(string Class, string Difficulty, int Floor, int Level, bool Victory, bool Timeout, int Turns);

    private bool IsTestRun => _screenshotPath != null || _simulateGames > 0;

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
            else if (arg.StartsWith("--class="))
                _testClass = arg["--class=".Length..] switch
                {
                    "warrior" => ClassCatalog.Warrior,
                    "ranger" => ClassCatalog.Ranger,
                    "mage" => ClassCatalog.Mage,
                    _ => null,
                };
            else if (arg.StartsWith("--difficulty="))
                _testDifficulty = arg["--difficulty=".Length..] switch
                {
                    "easy" => Difficulties.Easy,
                    "normal" => Difficulties.Normal,
                    "hard" => Difficulties.Hard,
                    _ => null,
                };
            else if (arg.StartsWith("--simulate="))
                _simulateGames = int.Parse(arg["--simulate=".Length..]);
        }

        // Самопроверка не должна трогать настоящее сохранение игрока
        if (IsTestRun)
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
            else if (_screenshotView == "classes")
                OpenClassSelect();
            else if (!inMenu)
                StartNewGame(_testClass ?? ClassCatalog.All[_rng.Next(ClassCatalog.All.Length)],
                    _testDifficulty ?? Difficulties.Normal);
        }

        int moveFrames = inMenu ? 0 : _autoMoves * 3;
        if (_frame <= moveFrames)
        {
            if (_frame % 3 == 0)
                AutoPilotTurn();
            return;
        }

        if (_frame == moveFrames + 1 && _state == GameState.Playing)
            OpenTestView();

        if (_frame == moveFrames + 40)
        {
            GD.Print($"Самопроверка ({_player.Class.Name}, ур. {_player.Level}): этаж {_floor}, убито {_kills}, здоровье {_player.Hp}, " +
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

    /// <summary>
    /// Один ход автопилота. Он не гений, но старается вести себя разумно: выбирает улучшения,
    /// пьёт зелье при низком здоровье, надевает экипировку, пользуется способностями.
    /// </summary>
    private void AutoPilotTurn()
    {
        switch (_state)
        {
            case GameState.LevelUp:
                OnPerkChosen(ClassCatalog.RollPerks(_rng)[0]);
                return;
            case GameState.Shop:
                CloseShop(); // автопилот случайно зашёл к торговцу
                return;
            case GameState.Inventory:
                CloseInventory();
                return;
            case not GameState.Playing:
                return;
        }

        var items = _player.Inventory.Items;
        int potion = items.FindIndex(item => item.Kind == ItemKind.HealthPotion);
        if (_player.Hp < _player.MaxHp * 0.4f && potion >= 0)
        {
            QuickUse(potion);
            return;
        }

        // Надеваем вещь, только если она лучше надетой — иначе автопилот менял бы их туда-обратно вечно
        int upgrade = items.FindIndex(item => item.IsEquipment &&
            GearScore(item) > GearScore(_player.Inventory.GetEquipped(item.Slot)));
        if (upgrade >= 0)
        {
            QuickUse(upgrade);
            return;
        }

        // Враг вплотную — бьём самого раненого, а не убегаем к лестнице
        var adjacent = _enemies
            .Where(enemy => _map.AreAdjacent(_player.GridPos, enemy.GridPos))
            .OrderBy(enemy => enemy.Hp)
            .FirstOrDefault();

        bool enemyNear = _enemies.Any(enemy => _visible.Contains(enemy.GridPos) && ChebyshevDistance(enemy.GridPos, _player.GridPos) <= 3);
        for (int i = 0; i < 2; i++)
        {
            if (enemyNear && _player.SkillCooldowns[i] == 0 && _rng.NextDouble() < 0.5)
            {
                int before = _player.SkillCooldowns[i];
                UseSkill(i);
                if (_player.SkillCooldowns[i] != before || _state != GameState.Playing)
                    return; // способность сработала — ход потрачен
            }
        }

        if (_rng.NextDouble() < 0.05 && items.Count > 0)
        {
            QuickUse(_rng.Next(items.Count)); // иногда пробует случайный предмет
            return;
        }

        OnPlayerAction(adjacent != null ? adjacent.GridPos - _player.GridPos : AutoPilotDirection());
    }

    // ---------- Симуляция для настройки баланса ----------

    /// <summary>Отключаем всё, что нужно только глазам и ушам, — так забеги идут в десятки раз быстрее.</summary>
    private void SetupSimulation()
    {
        Effects.Enabled = false;
        _sound.Muted = true;
        _hud.Silent = true;
        Settings.Current.Animation = AnimationSpeed.Instant; // только в памяти, файл настроек не трогаем
        Settings.Current.CameraShake = false;
    }

    private void StartSimulatedGame()
    {
        // Классы и сложности по очереди, чтобы выборка была ровной
        var playerClass = _testClass ?? ClassCatalog.All[_simGame % ClassCatalog.All.Length];
        var difficulty = _testDifficulty ?? Difficulties.Normal;
        _simActions = 0;
        StartNewGame(playerClass, difficulty);
    }

    private void RunSimulation()
    {
        if (_simGame == 0 && _state == GameState.Menu)
        {
            SetupSimulation();
            StartSimulatedGame();
        }

        for (int i = 0; i < SimActionsPerFrame; i++)
        {
            bool timeout = _turns >= SimTurnLimit || _simActions >= SimTurnLimit * 5;
            if (_state == GameState.GameOver || timeout)
            {
                _simResults.Add(new SimResult(_player.Class.Name, _difficulty.Name, _floor, _player.Level, _victory, timeout, _turns));
                _simGame++;
                if (_simGame >= _simulateGames)
                {
                    PrintSimulationSummary();
                    GetTree().Quit();
                    return;
                }
                StartSimulatedGame();
            }
            _simActions++;
            AutoPilotTurn();
        }
    }

    private void PrintSimulationSummary()
    {
        GD.Print("СИМУЛЯЦИЯ: класс | сложность | забегов | средний этаж | медиана | дошли до 5 | до 10 | победы | ср. уровень | зависли");
        foreach (var group in _simResults.GroupBy(r => (r.Class, r.Difficulty)))
        {
            var floors = group.Select(r => r.Floor).OrderBy(f => f).ToList();
            int n = floors.Count;
            string Percent(int count) => $"{100 * count / n}%";
            GD.Print($"СИМУЛЯЦИЯ: {group.Key.Class} | {group.Key.Difficulty} | {n} | {floors.Average():F1} | {floors[n / 2]} | " +
                     $"{Percent(group.Count(r => r.Floor >= 5))} | {Percent(group.Count(r => r.Floor >= 10))} | " +
                     $"{Percent(group.Count(r => r.Victory))} | {group.Average(r => r.Level):F1} | {group.Count(r => r.Timeout)}");
        }
    }

    /// <summary>Грубая оценка полезности вещи для автопилота (пустой слот — ноль).</summary>
    private static int GearScore(Item item)
    {
        if (item == null)
            return 0;
        var s = item.Stats;
        return s.Attack * 3 + s.Defense * 3 + s.MaxHp / 3 + s.Crit / 5 + s.Lifesteal * 2 +
               s.Thorns + s.Regen * 2 + s.Vision + s.FireDamage * 2;
    }

    /// <summary>
    /// Автопилот ведёт себя как обычный игрок: пока здоровья хватает, идёт драться с видимыми
    /// врагами (опыт и добыча), иначе — к лестнице. Иногда шагает случайно, чтобы не застревать.
    /// </summary>
    private Vector2I AutoPilotDirection()
    {
        var randomDirection = DungeonMap.Directions[_rng.Next(DungeonMap.Directions.Length)];
        if (_rng.NextDouble() < 0.15)
            return randomDirection;

        var prey = _enemies
            .Where(enemy => _visible.Contains(enemy.GridPos) && ChebyshevDistance(enemy.GridPos, _player.GridPos) <= 6)
            .OrderBy(enemy => ChebyshevDistance(enemy.GridPos, _player.GridPos))
            .FirstOrDefault();
        var goal = prey != null && _player.Hp > _player.MaxHp * 0.45f ? prey.GridPos : _map.StairsPos;

        var distances = _map.DistanceField(goal, MapWidth * MapHeight);
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
