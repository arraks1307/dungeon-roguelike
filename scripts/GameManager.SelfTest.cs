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
    private int _testParty = 1;  // сколько героев в отряде (все под автопилотом)
    private string _testLoadSave; // файл сохранения (например, от старой версии), с которым стартует проверка
    private GraphicsQuality? _testGraphics; // качество графики только на время проверки
    private DifficultyDef _testDifficulty; // null — по очереди или обычная

    // Симуляция: N забегов подряд без графики, в конце — сводка для настройки баланса
    private const int SimTurnLimit = 5000;
    private const int SimActionsPerFrame = 200;
    private int _simulateGames;
    private int _simGame;
    private int _simActions;
    private readonly List<SimResult> _simResults = new();

    private record SimResult(string Class, string Difficulty, int Floor, int Level, bool Victory, bool Timeout, int Turns);

    private int _tutorialTestShots;

    private bool IsTestRun => _screenshotPath != null || _simulateGames > 0 || _netTestRole != null;

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
            else if (arg.StartsWith("--graphics="))
                _testGraphics = arg["--graphics=".Length..] == "low" ? GraphicsQuality.Low : GraphicsQuality.High;
            else if (arg.StartsWith("--party="))
                _testParty = System.Math.Clamp(int.Parse(arg["--party=".Length..]), 1, MaxPartySize);
            else if (arg == "--net-tutorial")
                _netTestTutorial = true;
            else if (arg == "--net-trade")
                _netTestTrade = true;
            else if (arg == "--net-ui")
                _netTestUi = true;
            else if (arg == "--net-input")
                _netTestInput = true;
            else if (arg.StartsWith("--load-save="))
                _testLoadSave = arg["--load-save=".Length..];
            else if (arg.StartsWith("--party-tune="))
            {
                // --party-tune=здоровье,число,урон — прибавки врагам за каждого героя сверх первого
                var parts = arg["--party-tune=".Length..].Split(',');
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                _partyHpPerHero = float.Parse(parts[0], culture);
                _partyEnemiesPerHero = float.Parse(parts[1], culture);
                _partyDamagePerHero = float.Parse(parts[2], culture);
            }
            else if (arg.StartsWith("--party-weights="))
            {
                // --party-weights=вес2,вес3,вес4 — «вес» героев сверх первого для отряда из 2, 3, 4
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                var weights = arg["--party-weights=".Length..].Split(',').Select(w => float.Parse(w, culture));
                _partyWeights = new[] { 0f }.Concat(weights).ToArray();
            }
            else if (arg.StartsWith("--net-host="))
            {
                _netTestRole = "хост";
                _netTestPlayers = System.Math.Clamp(int.Parse(arg["--net-host=".Length..]), 1, MaxPartySize);
            }
            else if (arg.StartsWith("--net-join="))
            {
                _netTestRole = "клиент";
                _netTestAddress = arg["--net-join=".Length..];
            }
            else if (arg.StartsWith("--net-rounds="))
                _netTestRounds = int.Parse(arg["--net-rounds=".Length..]);
            else if (arg.StartsWith("--net-desync-at="))
                _netTestDesyncAt = int.Parse(arg["--net-desync-at=".Length..]);
            else if (arg.StartsWith("--net-quit-at="))
                _netTestQuitAt = int.Parse(arg["--net-quit-at=".Length..]);
            else if (arg.StartsWith("--net-name="))
                _netTestName = arg["--net-name=".Length..];
        }

        // Самопроверка не должна трогать настоящее сохранение игрока
        if (IsTestRun)
        {
            SaveSystem.FilePath = "user://selftest_save.json";
            SaveSystem.Delete();
        }
    }

    /// <summary>
    /// Отряд для проверки: первый герой — указанного класса (или случайного), остальные — по кругу.
    /// В режиме удержания клавиши свой герой управляется клавиатурой, иначе все — автопилотом.
    /// </summary>
    private List<HeroSpec> TestParty(int seed)
    {
        var party = new List<HeroSpec>();
        for (int i = 0; i < _testParty; i++)
        {
            var playerClass = i == 0 && _testClass != null ? _testClass : ClassCatalog.All[(seed + i) % ClassCatalog.All.Length];
            party.Add(new HeroSpec(playerClass, Bot: _screenshotView != "hold" || i > 0));
        }
        return party;
    }

    /// <summary>Короткая сводка о мире — чтобы сравнить состояние до сохранения и после загрузки.</summary>
    private string WorldSummary() =>
        $"этаж {_floor}, убито {_kills}, позиция {_localHero.GridPos}, здоровье {_localHero.Hp}/{_localHero.MaxHp}, " +
        $"атака {_localHero.Attack}, золото {_localHero.Gold}, сумка {_localHero.Inventory.Items.Count}, " +
        $"надето {_localHero.Inventory.Equipped.Count()}, врагов {_enemies.Count}, предметов на полу {_floorItems.Count}, " +
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
        if (_screenshotView is "tutorial" or "tutorialskip" or "tutorialoffer")
        {
            RunTutorialTest();
            return;
        }
        bool inMenu = _screenshotView is "menu" or "scores" or "classes" or "classconfirm" or "connect" or "lobby";

        if (_frame == 1)
        {
            if (_testLoadSave != null)
            {
                // Сохранение из другой версии игры: кладём в тестовый файл, загружаем кнопкой «Продолжить»
                using (var file = FileAccess.Open(SaveSystem.FilePath, FileAccess.ModeFlags.Write))
                    file.StoreString(System.IO.File.ReadAllText(_testLoadSave));
                ContinueGame();
                bool loaded = _state == GameState.Playing;
                GD.Print(loaded ? $"Старое сохранение: ЗАГРУЖЕНО: {WorldSummary()}" : "Старое сохранение: НЕ ЗАГРУЗИЛОСЬ");
                if (loaded)
                    _localHero.IsBot = true; // дальше играет автопилот: забег должен продолжаться как обычно
            }
            else if (_screenshotView == "connect")
                OpenLobby();
            else if (_screenshotView == "lobby")
            {
                // Комната хоста (только на этом компьютере), состав — как будто друзья уже подключились
                OpenLobby();
                _net.LoopbackOnly = true;
                _net.Host("Данила", 0);
                _lobby.OpenRoom(isHost: true);
                _lobby.UpdateMembers(new List<LobbyMember>
                {
                    new(1, "Данила", 0), new(2, "Аня", 1), new(3, "Боря", 2),
                }, myId: 1);
            }
            else if (_screenshotView == "scores")
                _mainMenu.ShowScores();
            else if (_screenshotView == "classes")
                OpenClassSelect();
            else if (_screenshotView == "classconfirm")
            {
                OpenClassSelect();
                _classSelect.AskConfirm(_testClass ?? ClassCatalog.Mage);
            }
            else if (!inMenu)
                StartRun(TestParty(_rng.Next(ClassCatalog.All.Length)), _testDifficulty ?? Difficulties.Normal);
        }

        int moveFrames = inMenu ? 0 : _autoMoves * 3;
        if (_frame <= moveFrames)
        {
            if (_frame % 3 == 0)
                AutoPilotRound();
            return;
        }

        if (_frame == moveFrames + 1 && _state == GameState.Playing)
            OpenTestView();

        // Проверка умной ходьбы: держим клавишу 1.6 секунды реального времени (не кадров —
        // на мониторе 144 Гц кадры идут в 2.4 раза чаще, чем на 60 Гц), потом отпускаем
        if (_screenshotView == "hold")
        {
            if (_testHeldKey != Key.None && Time.GetTicksMsec() - _holdStartMs >= 1600)
                ReleaseHeldKey();
            if (_testHeldKey != Key.None || _frame < moveFrames + 2)
                return; // ждём, пока клавиша отпущена, и только потом делаем снимок
            _screenshotView = "play"; // дальше — обычный снимок через 40 кадров
            _frame = moveFrames + 1;
        }

        if (_frame == moveFrames + 40)
        {
            string party = string.Join(", ", _heroes.Select(hero => $"{hero.DisplayName} ур.{hero.Level} {(hero.IsGhost ? "призрак" : $"{hero.Hp}/{hero.MaxHp}")}"));
            GD.Print($"Самопроверка (FPS {Engine.GetFramesPerSecond()}): этаж {_floor}, раундов {_turns}, убито {_kills}, " +
                     $"отряд: {party}, состояние {_state}{(_gameOver ? " (забег окончен)" : "")}");
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
            case "controls":
                Pause();
                _pauseMenu.ShowControls();
                break;
            case "inventory":
                // Набиваем сумку случайными вещами, чтобы было что показать
                while (_localHero.Inventory.Items.Count < 8)
                    _localHero.Inventory.TryAdd(ItemCatalog.Roll(_rng, _floor + 4));
                OpenInventory();
                break;
            case "shop":
                _localHero.Gold = 150;
                if (_merchant == null)
                    SpawnMerchant();
                OpenShop();
                break;
            case "settings":
                Pause();
                OpenSettings();
                break;
            case "hold":
                PressAndHoldOpenDirection();
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
    /// Проверка обучения. tutorial — бот проходит все шаги настоящими действиями,
    /// tutorialskip — обучение прерывается клавишей Tab, tutorialoffer — снимок окна с выбором.
    /// В конце должен начаться обычный забег.
    /// </summary>
    private void RunTutorialTest()
    {
        if (_frame == 1)
        {
            StartNewGame(_testClass ?? ClassCatalog.Warrior, Difficulties.Normal);
            if (_screenshotView == "tutorialoffer")
                return;
            _tutorialPanel.Cancel(); // сначала проверяем, что «Пропустить» запускает забег сразу
            GD.Print($"Обучение: Пропустить → этаж {_floor}, обучение идёт: {InTutorial}, сумка {_localHero.Inventory.Items.Count}");
            StartNewGame(_testClass ?? ClassCatalog.Warrior, Difficulties.Normal);
            _tutorialPanel.Hide();
            StartTutorial(_testClass ?? ClassCatalog.Warrior, null, () => StartRun(new List<HeroSpec> { new(_testClass ?? ClassCatalog.Warrior) }, Difficulties.Normal));
            return;
        }

        if (_screenshotView == "tutorialoffer")
        {
            if (_frame == 30)
            {
                GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
                GetTree().Quit();
            }
            return;
        }

        if (!InTutorial)
        {
            if (_frame % 20 == 0)
            {
                GD.Print($"Обучение: завершено, забег идёт: этаж {_floor}, раундов {_turns}, состояние {_state}, сумка {_localHero.Inventory.Items.Count}, амулет надет: {_localHero.Inventory.Equipped.Any(item => item.Template == ItemCatalog.LifeAmulet)}");
                GetTree().Quit();
            }
            return;
        }

        if (_screenshotView == "tutorialskip")
        {
            if (_frame == 20)
            {
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Tab, Keycode = Key.Tab, Pressed = true });
                GD.Print("Обучение: нажат Tab");
            }
            if (_frame > 200)
            {
                GD.Print("Обучение: Tab НЕ СРАБОТАЛ");
                GetTree().Quit(1);
            }
            return;
        }

        if (_tutorialStep == TutorialStep.Fight && _tutorialTestShots++ == 10)
            GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
        if (_frame > 3000)
        {
            GD.Print($"Обучение: ЗАВИСЛО на шаге {_tutorialStep}");
            GetTree().Quit(1);
            return;
        }
        if (_frame % 6 == 0)
            TutorialBotTurn();
    }

    /// <summary>Один ход бота на уровне обучения: делает то, чего требует текущий шаг.</summary>
    private void TutorialBotTurn()
    {
        var hero = _localHero;
        int Slot(ItemTemplate template) => hero.Inventory.Items.FindIndex(item => item.Template == template);
        Vector2I Enemy() => _enemies.Count > 0 ? _enemies[0].GridPos : hero.GridPos;

        switch (_tutorialStep)
        {
            case TutorialStep.Equip:
                if (Slot(ItemCatalog.LifeAmulet) >= 0)
                    PerformHeroAction(hero, HeroAction.UseItem(Slot(ItemCatalog.LifeAmulet)));
                return;
            case TutorialStep.Potion:
                if (Slot(ItemCatalog.HealthPotion) >= 0)
                    PerformHeroAction(hero, HeroAction.UseItem(Slot(ItemCatalog.HealthPotion)));
                return;
            case TutorialStep.Skill when _map.AreAdjacent(hero.GridPos, Enemy()):
                PerformHeroAction(hero, HeroAction.Skill(0));
                return;
        }

        var goal = _tutorialStep switch
        {
            TutorialStep.Walk => TutorialWalkGoal,
            TutorialStep.Door => TutorialDoor,
            TutorialStep.PickUp => TutorialItemCell,
            TutorialStep.Fight or TutorialStep.Skill => Enemy(),
            _ => TutorialStairs,
        };
        var distances = _map.DistanceField(goal, MapWidth * MapHeight);
        var step = DungeonMap.Directions
            .Where(direction => _map.CanMove(hero.GridPos, hero.GridPos + direction) && distances.ContainsKey(hero.GridPos + direction))
            .OrderBy(direction => distances[hero.GridPos + direction])
            .FirstOrDefault();
        PerformHeroAction(hero, HeroAction.Move(step));
    }

    /// <summary>Каждый герой, который ещё не сходил в этом раунде, делает ход автопилотом.</summary>
    private void AutoPilotRound()
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
        }

        foreach (var hero in _heroes.ToList())
        {
            if (_gameOver)
                return;
            if (!hero.IsGhost && !hero.HasActed && hero.IsBot)
                AutoPilotTurn(hero, action => PerformHeroAction(hero, action), _rng);
        }
    }

    /// <summary>
    /// Один ход автопилота за героя. Он не гений, но старается вести себя разумно: пьёт зелье
    /// при низком здоровье, надевает экипировку получше, пользуется способностями, дерётся.
    /// Если выбранное действие не сработало (стена, нет цели), пробует случайный шаг.
    /// tryAction — как выполнить действие (сразу или отправить по сети), rng — чья случайность
    /// решает за бота: в сетевой проверке это не _rng, иначе миры у компьютеров разошлись бы.
    /// </summary>
    private void AutoPilotTurn(Player hero, System.Func<HeroAction, bool> tryAction, System.Random rng)
    {
        bool Try(HeroAction action) => tryAction(action);

        var items = hero.Inventory.Items;
        int potion = items.FindIndex(item => item.Kind == ItemKind.HealthPotion);
        if (hero.Hp < hero.MaxHp * 0.4f && potion >= 0 && Try(HeroAction.UseItem(potion)))
            return;

        // Надеваем вещь, только если она лучше надетой — иначе автопилот менял бы их туда-обратно вечно
        int upgrade = items.FindIndex(item => item.IsEquipment &&
            GearScore(item) > GearScore(hero.Inventory.GetEquipped(item.Slot)));
        if (upgrade >= 0 && Try(HeroAction.UseItem(upgrade)))
            return;

        bool enemyNear = _enemies.Any(enemy => _visible.Contains(enemy.GridPos) && ChebyshevDistance(enemy.GridPos, hero.GridPos) <= 3);
        for (int i = 0; i < 2; i++)
            if (enemyNear && hero.SkillCooldowns[i] == 0 && rng.NextDouble() < 0.5 && Try(HeroAction.Skill(i)))
                return;

        if (rng.NextDouble() < 0.05 && items.Count > 0 && Try(HeroAction.UseItem(rng.Next(items.Count))))
            return; // иногда пробует случайный предмет

        // Враг вплотную — бьём самого раненого, а не убегаем к лестнице
        var adjacent = _enemies
            .Where(enemy => _map.AreAdjacent(hero.GridPos, enemy.GridPos))
            .OrderBy(enemy => enemy.Hp)
            .FirstOrDefault();
        var direction = adjacent != null ? adjacent.GridPos - hero.GridPos : AutoPilotDirection(hero, rng);
        if (Try(HeroAction.Move(direction)))
            return;

        for (int attempt = 0; attempt < 4; attempt++)
            if (Try(HeroAction.Move(DungeonMap.Directions[rng.Next(DungeonMap.Directions.Length)])))
                return;
        Try(HeroAction.Wait); // совсем некуда — пропускаем ход, чтобы раунд не завис
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
        // Классы по очереди, чтобы выборка была ровной
        _simActions = 0;
        StartRun(TestParty(_simGame), _testDifficulty ?? Difficulties.Normal);
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
            if (_gameOver || timeout)
            {
                string label = IsSinglePlayer ? _localHero.Class.Name : $"Отряд ×{_heroes.Count}";
                _simResults.Add(new SimResult(label, _difficulty.Name, _floor, _localHero.Level, _victory, timeout, _turns));
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
            AutoPilotRound();
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

    // ---------- Проверка удержания клавиши ----------

    private Key _testHeldKey = Key.None;
    private ulong _holdStartMs;
    private int _holdStartFrame;
    private int _turnsBeforeHold;
    private bool _enemyVisibleAtHold;

    /// <summary>
    /// Нажимаем «настоящую» клавишу через Input.ParseInputEvent — так событие проходит тот же путь,
    /// что и от клавиатуры, и Input.IsPhysicalKeyPressed видит её зажатой.
    /// Выбираем направление, где впереди больше всего свободных клеток.
    /// </summary>
    private void PressAndHoldOpenDirection()
    {
        var keys = new Dictionary<Vector2I, Key>
        {
            [new Vector2I(0, -1)] = Key.W, [new Vector2I(0, 1)] = Key.S,
            [new Vector2I(-1, 0)] = Key.A, [new Vector2I(1, 0)] = Key.D,
        };
        int FreeCells(Vector2I direction)
        {
            int count = 0;
            var cell = _localHero.GridPos + direction;
            while (_map.IsWalkable(cell) && count < 20)
            {
                count++;
                cell += direction;
            }
            return count;
        }
        var best = keys.Keys.OrderByDescending(FreeCells).First();

        _testHeldKey = keys[best];
        _holdStartMs = Time.GetTicksMsec();
        _holdStartFrame = _frame;
        _turnsBeforeHold = _turns;
        _enemyVisibleAtHold = AnyEnemyVisible();
        GD.Print($"Удержание: клавиша {_testHeldKey}, свободно клеток впереди {FreeCells(best)}, враг виден: {_enemyVisibleAtHold}");
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = _testHeldKey, Keycode = _testHeldKey, Pressed = true });
    }

    private void ReleaseHeldKey()
    {
        if (_testHeldKey == Key.None)
            return;
        // Если окно теряло фокус, Godot сам «отпускает» все клавиши — тогда ходьба прервалась не из-за игры
        GD.Print($"Удержание: клавиша всё ещё зажата: {Input.IsPhysicalKeyPressed(_testHeldKey)}, " +
                 $"кадров за время удержания: {_frame - _holdStartFrame}");
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = _testHeldKey, Keycode = _testHeldKey, Pressed = false });
        GD.Print($"Удержание: за 1.6 с прошло ходов: {_turns - _turnsBeforeHold}, враг виден сейчас: {AnyEnemyVisible()}, " +
                 $"причина остановки: {_lastWalkStopReason ?? "не останавливался"}");
        _testHeldKey = Key.None;
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
    private Vector2I AutoPilotDirection(Player hero, System.Random rng)
    {
        var randomDirection = DungeonMap.Directions[rng.Next(DungeonMap.Directions.Length)];
        if (rng.NextDouble() < 0.15)
            return randomDirection;

        var prey = _enemies
            .Where(enemy => _visible.Contains(enemy.GridPos) && ChebyshevDistance(enemy.GridPos, hero.GridPos) <= 6)
            .OrderBy(enemy => ChebyshevDistance(enemy.GridPos, hero.GridPos))
            .FirstOrDefault();
        // Отряд ждёт у лестницы — идём к ней, а не за добычей
        var goal = prey != null && hero.Hp > hero.MaxHp * 0.45f && !_descentRequested ? prey.GridPos : _map.StairsPos;

        var distances = _map.DistanceField(goal, MapWidth * MapHeight);
        if (!distances.TryGetValue(hero.GridPos, out int best))
            return randomDirection;

        var result = randomDirection;
        foreach (var direction in DungeonMap.Directions)
        {
            var next = hero.GridPos + direction;
            if (distances.TryGetValue(next, out int distance) && distance < best && _map.CanMove(hero.GridPos, next))
            {
                best = distance;
                result = direction;
            }
        }
        return result;
    }
}
