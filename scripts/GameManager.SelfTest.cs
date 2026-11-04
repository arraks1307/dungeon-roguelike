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
    private bool _metaLocked;          // в проверках прогресс не считается, кроме проверки самого прогресса
    private bool _simShop;             // в симуляции боты тратят золото на услуги торговца
    private int _simGoldScale = 1;     // боты подбирают мало золота: множитель, чтобы приблизиться к живому игроку
    private string _soundReportFolder; // куда записать WAV и напечатать числа о звуках
    private bool _soundDemo;           // проиграть все звуки подряд, чтобы послушать
    private Vector3 _lastWalkPosition;
    private ulong _lastWalkMoveMs;
    private ulong _longestWalkGapMs;
    private int _walkMoves;

    private bool IsTestRun => _screenshotPath != null || _simulateGames > 0 || _netTestRole != null;

    /// <summary>Отчёт о звуках или их прослушивание. true — обычная игра не запускается.</summary>
    private bool RunSoundTools()
    {
        if (_soundReportFolder != null)
        {
            foreach (var line in _sound.Report())
                GD.Print(line);
            _sound.ExportWav(_soundReportFolder);
            GetTree().Quit();
            return true;
        }
        if (!_soundDemo)
            return false;
        PlaySoundDemo();
        return true;
    }

    /// <summary>Все звуки по очереди, с названием на экране. Громкость — 80%, настройки не меняются.</summary>
    private async void PlaySoundDemo()
    {
        AudioServer.SetBusMute(0, false);
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(0.8f));
        int music = AudioServer.GetBusIndex(SoundManager.MusicBus);
        AudioServer.SetBusMute(music, true); // сначала звуки по отдельности, музыка — в конце
        _mainMenu.Visible = false;
        var title = Ui.CreateLabel(44);
        title.SetAnchorsPreset(Control.LayoutPreset.Center);
        var layer = new CanvasLayer();
        AddChild(layer);
        layer.AddChild(title);
        foreach (var sound in SoundManager.All)
        {
            for (int repeat = 0; repeat < 2; repeat++)
            {
                title.Text = sound.ToString();
                GD.Print($"ЗВУК: {sound}");
                _sound.Play(sound);
                await ToSignal(GetTree().CreateTimer(1.3), SceneTreeTimer.SignalName.Timeout);
            }
        }
        title.Text = "Музыка";
        AudioServer.SetBusMute(music, false);
        AudioServer.SetBusVolumeDb(music, Mathf.LinearToDb(0.6f));
        await ToSignal(GetTree().CreateTimer(MusicSynth.LoopSeconds + 5f), SceneTreeTimer.SignalName.Timeout);
        GetTree().Quit();
    }

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
                    "priest" => ClassCatalog.Priest,
                    "rogue" => ClassCatalog.Rogue,
                    "berserk" => ClassCatalog.Berserk,
                    _ => null,
                };
            else if (arg.StartsWith("--difficulty="))
                _testDifficulty = arg["--difficulty=".Length..] switch
                {
                    "easy" => Difficulties.Easy,
                    "normal" => Difficulties.Normal,
                    "hard" => Difficulties.Hard,
                    "nightmare" => Difficulties.Nightmare,
                    _ => null,
                };
            else if (arg.StartsWith("--simulate="))
                _simulateGames = int.Parse(arg["--simulate=".Length..]);
            else if (arg.StartsWith("--graphics="))
                _testGraphics = arg["--graphics=".Length..] switch
                {
                    "low" => GraphicsQuality.Low,
                    "ultra" => GraphicsQuality.Ultra,
                    _ => GraphicsQuality.High,
                };
            else if (arg.StartsWith("--party="))
                _testParty = System.Math.Clamp(int.Parse(arg["--party=".Length..]), 1, MaxPartySize);
            else if (arg.StartsWith("--sound-report="))
                _soundReportFolder = arg["--sound-report=".Length..];
            else if (arg == "--sound-demo")
                _soundDemo = true;
            else if (arg.StartsWith("--sim-gold="))
                _simGoldScale = int.Parse(arg["--sim-gold=".Length..]);
            else if (arg == "--sim-shop")
                _simShop = true;
            else if (arg == "--net-resume")
                _netTestResume = true;
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
            SaveSystem.NetFilePath = "user://selftest_netsave.json";
            Profile.FilePath = "user://selftest_profile.json";
            Profile.Delete();
            _metaLocked = _screenshotView != "world";
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
        bool inMenu = _screenshotView is "legacy" or "menu" or "scores" or "classes" or "classconfirm" or "connect" or "lobby";

        if (_frame == 1)
        {
            if (_screenshotView == "legacy")
            {
                Profile.Current.Shards = 240;
                Profile.Current.Seen.UnionWith(new[] { "Слизень", "Гоблин", "Громила", "Щитоносец" });
                Profile.Current.Achievements.Add("floor5");
                OpenLegacy();
            }
            else if (_testLoadSave != null)
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
            else if (_screenshotView == "camp")
            {
                foreach (var resident in Story.Residents.Take(3))
                    Profile.Current.Rescued.Add(resident.Id); // только в памяти: профиль в проверке не сохраняется
                EnterCamp();
                RunCampTest();
            }
            else if (_screenshotView == "keys")
            {
                var before = KeyBindings.Get(GameKey.Skill1);
                KeyBindings.Set(GameKey.Skill1, Key.G); // занято: должен получиться обмен
                GD.Print($"Клавиши: способность 1 = {KeyBindings.Name(GameKey.Skill1)}, способность 2 = {KeyBindings.Name(GameKey.Skill2)}, J свободна {KeyBindings.Find(Key.J) == null}, Esc закреплён {KeyBindings.IsReserved(Key.Escape)}");
                KeyBindings.Reset();
                GD.Print($"Клавиши: после сброса {KeyBindings.Name(GameKey.Skill1)} (было {before})");
                _mainMenu.Visible = false;
                OpenSettings();
                _settingsMenu.GetNode<KeyBindPanel>("KeyBindPanel").Open();
            }
            else if (_screenshotView == "worldflow")                RunWorldFlowTest();
            else if (_screenshotView == "worldcreate")
            {
                _mainMenu.Visible = false;
                _worldCreatePanel.Open();
            }
            else if (_screenshotView == "herobuild")
            {
                _mainMenu.Visible = false;
                _heroBuildPanel.Open(new WorldInfo { Name = "Проверка", ClassIndex = 2, Points = new[] { 3, 1, 1, 0, 0, 2, 0 }, Trait = 2 });
            }
            else if (_screenshotView == "campexit")            {
                // Настоящий путь: мир, лагерь, пещера, спуск
                Worlds.Folder = "user://test_worlds";
                var world = new WorldInfo { Name = "Спуск", Seed = 5, ClassIndex = 1, Descents = 1 };
                Worlds.Create(world);
                OpenWorld(world);
                OpenCampStation(CampScene.Cave);
                GD.Print($"Лагерь: спуск: герой {_localHero?.Class.Name}, этаж {_floor}, спусков {world.Descents}");
                Worlds.Delete(world);
                GD.Print($"Лагерь: выход: лагерь {InCamp}, состояние {_state}, выбор героя открыт {_classSelect.Visible}, виден мир {_renderer.Visible}");
            }
            else if (_screenshotView == "connect")
                OpenLobby();            else if (_screenshotView == "lobby")
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
            TrackWalkGaps();
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

    /// <summary>Снимок: все виды врагов вокруг героя, у которого почти кончилось здоровье.</summary>
    private void ShowEnemyGallery()
    {
        var hero = _localHero;
        var cells = new Queue<Vector2I>(Enumerable.Range(-5, 11)
            .SelectMany(x => Enumerable.Range(-5, 11).Select(y => hero.GridPos + new Vector2I(x, y)))
            .Where(c => _map.Get(c) == Tile.Floor && !IsOccupied(c))
            .OrderBy(c => (c - hero.GridPos).LengthSquared()));
        foreach (var type in Enemy.Types)
            if (cells.Count > 0)
            {
                var enemy = SpawnEnemy(type, cells.Dequeue());
                Effects.Stain(this, enemy.GridPos, type.Color);
            }
        var boss = _enemies.FirstOrDefault(enemy => enemy.IsBoss);
        if (boss != null && cells.Count > 0)
            boss.Teleport(cells.Dequeue());
        hero.TakeDamage(hero.Hp - 3);
        UpdateFov();
        UpdateHud();
    }

    /// <summary>Снимок: иконки всех предметов сеткой поверх игры.</summary>
    private void ShowIconGallery()
    {
        var grid = new GridContainer { Columns = 12, Position = new Vector2(60f, 110f) };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);
        foreach (var template in ItemCatalog.All.Concat(ItemCatalog.Legendaries).Concat(ItemCatalog.SetPieces))
            grid.AddChild(new TextureRect { Texture = ItemIcons.Get(new Item(template), 96) });
        var layer = new CanvasLayer { Layer = 50 };
        layer.AddChild(new ColorRect { Color = new Color(0.1f, 0.1f, 0.13f), Size = new Vector2(1920f, 1080f) });
        layer.AddChild(grid);
        AddChild(layer);
    }

    /// <summary>Снимок: вещи, золото, сундук, алтарь и торговец рядом с героем.</summary>
    private void ShowPropGallery()
    {
        var hero = _localHero;
        var cells = new Queue<Vector2I>(Enumerable.Range(-5, 11)
            .SelectMany(x => Enumerable.Range(-5, 11).Select(y => hero.GridPos + new Vector2I(x, y)))
            .Where(c => _map.Get(c) == Tile.Floor && !IsOccupied(c) && c != hero.GridPos)
            .OrderBy(c => (c - hero.GridPos).LengthSquared()));
        AddChild(Chest.Create(cells.Dequeue(), false, false));
        AddChild(Shrine.Create(cells.Dequeue(), default));
        AddChild(Merchant.Create(cells.Dequeue(), new List<Item>()));
        AddPickup(ItemPickup.CreateGold(10, cells.Peek()), cells.Dequeue());
        foreach (var kind in System.Enum.GetValues<ItemKind>())
        {
            Item item = null;
            for (int i = 0; i < 400 && item?.Kind != kind; i++)
                item = ItemCatalog.Roll(_rng, 12);
            if (item?.Kind == kind && cells.Count > 0)
                AddPickup(ItemPickup.Create(item, cells.Peek()), cells.Dequeue());
        }
        UpdateFov();
        UpdateHud();
    }

    private void OpenTestView()
    {
        switch (_screenshotView)
        {
            case "pause":
                Pause();
                break;
            case "world":
                RunWorldTest();
                break;
            case "coop":
                RunCoopTest();
                break;
            case "tactics":
                RunTacticsTest();
                break;
            case "charge":
                RunChargeTest();
                break;
            case "services":
                RunServicesTest();
                break;
            case "enemies":
                ShowEnemyGallery();
                break;
            case "equip":
                _localHero.Inventory.TryAdd(new Item(ItemCatalog.SetPieces[0], Rarity.Set));
                OpenInventory();
                OnInventoryUse(_localHero.Inventory.Items.Count - 1);
                GD.Print($"Экипировка: окно {_state}, видно {_inventoryPanel.Visible}, броня {_localHero.Inventory.GetEquipped(EquipSlot.Armor)?.Name}");
                break;
            case "summary":
                _localHero.AddPact(Pacts.All[0]);
                GameOver("Орк");
                break;
            case "story":
                _state = GameState.Event; // в проверках герой под автопилотом, поэтому окно открываем напрямую
                _choicePanel.Open("Тёмный алтарь", "Камень шепчет твоё имя. Сила останется с тобой до конца забега. Плата тоже.",
                    new[] { 0, 2, 5 }.Select(id => $"{Pacts.All[id].Name}: {Pacts.All[id].Offer}").ToList());
                break;
            case "icons":
                ShowIconGallery();
                break;
            case "props":
                ShowPropGallery();
                break;
            case "fade":
                _hud.FadeFromBlack(); // снимок через 40 кадров покажет середину затемнения
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
                _localHero.Gold = 2000;
                while (_localHero.Inventory.Items.Count < 3)
                    _localHero.Inventory.TryAdd(ItemCatalog.Roll(_rng, _floor + 4));
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

        if (_tutorialStep == TutorialStep.Fight && _tutorialTestShots++ == 10 && DisplayServer.GetName() != "headless")
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

    /// <summary>Наборы: бонус появляется со второй части, растёт с третьей и исчезает вместе с вещами.</summary>
    private void RunSetTest(Player hero)
    {
        var pieces = ItemCatalog.SetPieces.Where(piece => piece.Set == ItemCatalog.GuardSet).ToArray();
        var log = new List<string> { $"защита {hero.Defense}, здоровье {hero.MaxHp}" };
        foreach (var piece in pieces)
        {
            hero.Inventory.Items.Clear();
            hero.Inventory.TryAdd(new Item(piece, Rarity.Set));
            hero.Equip(0);
            log.Add($"{hero.Inventory.SetCount(ItemCatalog.GuardSet)} части: защита {hero.Defense}, здоровье {hero.MaxHp}, шипы {hero.Bonuses.Thorns}");
        }
        var restored = ItemSave.From(new Item(pieces[0], Rarity.Set)).ToItem();
        GD.Print($"Набор: {string.Join(" → ", log)}; сохранение: {restored?.Name}, {restored?.Rarity}");
    }

    /// <summary>Мир: герой не меняется, этажи постоянны, прогресс раздельный, вечная смерть удаляет мир.</summary>
    private void RunWorldFlowTest()
    {
        Worlds.Folder = "user://test_worlds";
        foreach (var old in Worlds.LoadAll())
            Worlds.Delete(old);

        var first = new WorldInfo { Name = "Первый", Seed = 42, ClassIndex = 2, LengthIndex = 0, Points = new[] { 2, 1, 0, 0, 0, 1, 1 }, Trait = 2 };
        var second = new WorldInfo { Name = "Второй", Seed = 43, Permadeath = true };
        Worlds.Create(first);
        System.Threading.Thread.Sleep(1100); // номер мира включает секунды
        Worlds.Create(second);

        Worlds.Select(first);
        Profile.Current.Shards = 77;
        Profile.Save();
        int firstLayout = LayoutHash(5, first.Seed);
        int repeat = LayoutHash(5, first.Seed);
        OpenWorldForTest(first);
        var hero = _localHero;
        GD.Print($"Миры: герой {hero.Class.Name}, атака {hero.Attack}, защита {hero.Defense}, здоровье {hero.MaxHp}, золото {hero.Gold}, очков потрачено {Worlds.Spent(first)}, спусков {first.Descents}");
        GD.Print($"Миры: планировка одинакова {firstLayout == repeat}, боссы: этаж 10 {Enemy.BossForFloor(10, 10).Name}, этаж 15 в длинном {Enemy.BossForFloor(15, 20).Name}, обычный финал {Enemy.BossForFloor(15).Name}");
        ShowMainMenu();
        Worlds.Close();

        Worlds.Select(second);
        GD.Print($"Миры: второй мир осколки {Profile.Current.Shards}, файл {Profile.FilePath}");
        OpenWorldForTest(second);
        _metaLocked = true;
        GameOver("проверка");
        GD.Print($"Миры: вечная смерть: потерян {_worldLost}, мир открыт {Worlds.Current != null}, осталось миров {Worlds.LoadAll().Count}");
        _worldLost = false;
        Worlds.Delete(first);
        ShowMainMenu();
    }

    private void OpenWorldForTest(WorldInfo world)
    {
        Worlds.Select(world);
        world.Descents++;
        _isNetworkGame = false;
        StartRun(new List<HeroSpec> { new(world.Class) }, Difficulties.Get(world.Difficulty));
    }

    private static int LayoutHash(int floor, int seed)
    {
        var map = DungeonGenerator.Generate(MapWidth, MapHeight, new GameRandom(unchecked(seed * 31 + floor * 104729)), Themes.IndexFor(floor));
        return map.StairsPos.X * 1000 + map.StairsPos.Y + map.Rooms.Count * 100000;
    }

    /// <summary>Лагерь: герой ходит, упирается в костёр и открывает разговор, Esc-путь возвращает в лагерь.</summary>
    private void RunCampTest()
    {
        var start = _campHero.GridPos;
        OnCampMove(new Vector2I(0, -1));
        OnCampMove(new Vector2I(0, -1));
        var walked = _campHero.GridPos;
        OnCampMove(new Vector2I(0, -1));
        var state = _state;
        CloseCampDialog();
        _campHero.Teleport(new Vector2I(4, 5));
        OnCampMove(new Vector2I(0, -1));
        var forge = _state;
        CloseCampDialog();
        _campHero.Teleport(CampScene.StartCell);
        UpdateCampOverlay();
        GD.Print($"Лагерь: шаги {start} → {walked}, костёр: {state}, кузница: {forge}, вернулись: {_state}, стена: {_camp.IsWalkable(new Vector2I(0, 5))}");
    }

    /// <summary>Сделка, реликварий, пленник, запись и то, что всё это переживает сохранение.</summary>
    private void RunStoryTest(Player hero)
    {
        Vector2I Free() => DungeonMap.Directions.Select(d => hero.GridPos + d).First(c => _map.Get(c) == Tile.Floor && !IsOccupied(c));

        int attack = hero.Attack, hp = hero.MaxHp;
        var pact = PlaceShrine(Free(), ShrineKind.Pact, true);
        pact.Deals = new[] { 0, 3, 5 };
        var save = pact.CreateSave();
        ApplyWanderer(hero, 0, pact.GridPos);
        GD.Print($"Сюжет: сделка: атака {attack} → {hero.Attack}, здоровье {hp} → {hero.MaxHp}, сделок {hero.Pacts.Count}, алтарь исчез {!_shrines.ContainsKey(save.X == pact.GridPos.X ? pact.GridPos : default)}, вариантов сохранено {save.Deals.Length}");

        var loaded = new PlayerSave();
        var data = hero.CreateSave();
        GD.Print($"Сюжет: сохранение героя: сделки [{string.Join(", ", data.Pacts)}]");

        var relic = PlaceShrine(Free(), ShrineKind.Reliquary, true);
        relic.Offer.AddRange(ItemCatalog.Legendaries.Take(3).Select(template => new Item(template, Rarity.Legendary)));
        hero.Inventory.Items.Clear();
        ApplyWanderer(hero, 1, relic.GridPos);
        GD.Print($"Сюжет: реликварий: в сумке {hero.Inventory.Items.FirstOrDefault()?.Name}");

        var captive = PlaceShrine(Free(), ShrineKind.Captive, true);
        captive.Extra = 0;
        RescueCaptive(hero, captive);
        var note = PlaceShrine(Free(), ShrineKind.Note, true);
        note.Extra = 2;
        ReadNote(hero, note);
        CloseChoice();
        GD.Print($"Сюжет: лагерь: спасены [{string.Join(", ", Profile.Current.Rescued)}], записи [{string.Join(", ", Profile.Current.Notes)}], кузница открыта {Profile.IsOpen(Profile.Upgrades.First(u => u.Id == Profile.Forge))}");

        hero.AddPact(Pacts.All[2]);
        GD.Print($"Сюжет: слабые зелья: {PotionPower(hero, 12)}; легендарок доступно {ItemCatalog.Legendaries.Count(t => t.Unlock == null || Profile.Has(t.Unlock))} из {ItemCatalog.Legendaries.Length}");
    }

    /// <summary>Легендарные вещи, разбойник, условия этажа, странник, наследие.</summary>
    private void RunWorldTest()
    {
        var hero = _localHero;
        RunSetTest(hero);
        RunStoryTest(hero);
        Vector2I Free() => DungeonMap.Directions.Select(d => hero.GridPos + d).First(c => _map.Get(c) == Tile.Floor && _map.CanMove(hero.GridPos, c) && !IsOccupied(c));
        Enemy Place(string name)
        {
            foreach (var old in _enemies.ToList())
            {
                _enemies.Remove(old);
                old.QueueFree();
            }
            return SpawnEnemy(Enemy.FindType(name), Free());
        }
        void Wear(LegendaryEffect effect)
        {
            var template = ItemCatalog.Legendaries.First(t => t.Effect == effect);
            hero.Inventory.TryAdd(new Item(template, Rarity.Legendary));
            hero.Equip(hero.Inventory.Items.FindIndex(item => item.Template == template));
            hero.RefreshStats();
        }

        Wear(LegendaryEffect.Phoenix);
        hero.Heal(hero.MaxHp);
        var killer = Place("Мимик");
        hero.TakeDamage(hero.Hp - 1);
        EnemyAttacksPlayer(killer, hero, ranged: false);
        GD.Print($"Мир: феникс: здоровье {hero.Hp}/{hero.MaxHp}, призрак {hero.IsGhost}, использован {hero.PhoenixUsed}");

        Wear(LegendaryEffect.Storm);
        var left = Place("Мимик");
        var extra = SpawnEnemy(Enemy.FindType("Мимик"), Free());
        int side = extra.Hp;
        PerformHeroAction(hero, HeroAction.Move(left.GridPos - hero.GridPos));
        int afterFirst = extra.Hp;
        hero.StormCounter = 2; // следующий удар — третий
        var second = Place("Мимик");
        var neighbour = SpawnEnemy(Enemy.FindType("Мимик"), Free());
        side = neighbour.Hp;
        PerformHeroAction(hero, HeroAction.Move(second.GridPos - hero.GridPos));
        GD.Print($"Мир: клинок бури: обычный удар не задел соседа ({afterFirst} из {extra.MaxHp}), третий задел: {side} → {neighbour.Hp}");

        Wear(LegendaryEffect.Greed);
        hero.Gold = 500;
        GD.Print($"Мир: алчность при 500 золота: +{GreedBonus(hero)}");

        Wear(LegendaryEffect.Shadow);
        var weak = Place("Слизень");
        weak.TakeDamage(weak.Hp - 1);
        PerformHeroAction(hero, HeroAction.Move(weak.GridPos - hero.GridPos));
        GD.Print($"Мир: плащ теней: после победы невидим {hero.IsInvisible}");

        var rogue = new Player();
        AddChild(rogue);
        rogue.ResetStats(ClassCatalog.Rogue);
        GD.Print($"Мир: разбойник: способности {rogue.Class.Skills[0].Name}, {rogue.Class.Skills[1].Name}, сумка {rogue.Inventory.Items.Count}");
        rogue.QueueFree();

        var cell = Free();
        PlaceShrine(cell, ShrineKind.Wanderer, explained: false);
        int gold = hero.Gold;
        ApplyWanderer(hero, 0, cell);
        GD.Print($"Мир: странник: золото {gold} → {hero.Gold}, остался {_shrines.ContainsKey(cell)}");

        _condition = FloorCondition.Lava;
        var lava = Free();
        _map.Traps[lava] = new Trap { Kind = TrapKind.Lava, Revealed = true };
        int hp = hero.Hp;
        foreach (var old in _enemies.ToList())
        {
            _enemies.Remove(old);
            old.QueueFree();
        }
        hero.Heal(hero.MaxHp);
        hp = hero.Hp;
        PerformHeroAction(hero, HeroAction.Move(lava - hero.GridPos));
        GD.Print($"Мир: лава: здоровье {hp} → {hero.Hp}");
        _condition = FloorCondition.None;

        var enemy = Place("Слизень");
        GD.Print($"Мир: подсказка: {TooltipFor(enemy.GridPos)?.Replace('\n', ' ')}");

        Profile.Current.Shards = 100;
        var vitality = Profile.Upgrades[0];
        GD.Print($"Мир: покупка: {Profile.TryBuy(vitality)}, уровень {Profile.Level(vitality.Id)}, осколков {Profile.Current.Shards}");
        _floor = 12;
        _kills = 30;
        GD.Print($"Мир: итоги: {FinishRun().Replace('\n', ' ')}");
        Profile.Load();
        GD.Print($"Мир: после перезагрузки: осколков {Profile.Current.Shards}, лучший этаж {Profile.Current.BestFloor}, достижений {Profile.Current.Achievements.Count}");
        var check = new Player();
        AddChild(check);
        check.ResetStats(ClassCatalog.Warrior);
        int baseHp = check.MaxHp;
        ApplyLegacy(check);
        GD.Print($"Мир: наследие: здоровье {baseHp} → {check.MaxHp}");
        GetTree().Quit();
    }

    /// <summary>Приёмы отряда настоящими действиями. Нужен отряд из двух героев: --party=2.</summary>
    private void RunCoopTest()
    {
        var a = _heroes[0];
        var b = _heroes[1];
        var room = _map.Rooms[0];
        var center = room.Position + room.Size / 2;
        var left = center + Vector2I.Left;
        var right = center + Vector2I.Right;
        var far = center + Vector2I.Up * 2;
        Enemy Place(string name)
        {
            foreach (var old in _enemies.ToList())
            {
                _enemies.Remove(old);
                old.QueueFree();
            }
            a.Teleport(left);
            b.Teleport(far);
            a.Heal(a.MaxHp);
            b.Heal(b.MaxHp);
            _roundActions = 0;
            var enemy = SpawnEnemy(Enemy.FindType(name), center);
            return enemy;
        }
        int Hit(Enemy enemy)
        {
            int before = enemy.Hp;
            PerformHeroAction(a, HeroAction.Move(enemy.GridPos - a.GridPos));
            return before - enemy.Hp;
        }

        var enemy1 = Place("Мимик");
        int plain = Hit(enemy1);
        enemy1 = Place("Мимик");
        b.Teleport(right);
        GD.Print($"Отряд: удар один {plain}, с фланга {Hit(enemy1)}");

        var shield = Place("Щитоносец");
        int blocked = Hit(shield);
        shield = Place("Щитоносец");
        b.Teleport(right);
        GD.Print($"Отряд: щит поднят {shield.ShieldUp}: один {blocked}, с фланга {Hit(shield)}");

        var frozen = Place("Мимик");
        frozen.Freeze(3, b.Id);
        GD.Print($"Отряд: заморозка союзника: удар {Hit(frozen)} (обычный {plain})");

        var attacker = Place("Гоблин");
        b.Teleport(left + Vector2I.Down);
        PerformHeroAction(b, HeroAction.Wait);
        int hpA = a.Hp, hpB = b.Hp;
        EnemyAttacksPlayer(attacker, a, ranged: false);
        GD.Print($"Отряд: прикрытие: цель {hpA} → {a.Hp}, прикрывший {hpB} → {b.Hp}");

        Place("Слизень");
        foreach (var old in _enemies.ToList())
        {
            _enemies.Remove(old);
            old.QueueFree();
        }
        int turns = _turns;
        _roundActions = 0;
        PerformHeroAction(a, HeroAction.Wait);
        int afterOne = _turns;
        PerformHeroAction(a, HeroAction.Wait);
        GD.Print($"Отряд: свободные ходы: один герой сделал 2 действия, раундов +{_turns - turns} (после первого +{afterOne - turns})");

        b.Teleport(left + Vector2I.Right);
        b.TakeDamage(b.Hp);
        OnHeroDied(b, "тест");
        var wall = DungeonMap.Directions.Select(d => b.GridPos + d).FirstOrDefault(c => !_map.IsWalkable(c), b.GridPos);
        int cellCount = 0;
        var flown = b.GridPos;
        for (int i = 0; i < 12; i++)
        {
            PerformHeroAction(b, HeroAction.Move(Vector2I.Up));
            if (!_map.IsWalkable(b.GridPos))
                cellCount++;
        }
        GD.Print($"Отряд: призрак пролетел {flown} → {b.GridPos}, клеток внутри стен {cellCount}, раундов это не добавило");
        b.Teleport(left + Vector2I.Right);
        PerformHeroAction(a, HeroAction.Move(Vector2I.Right));
        bool ghostAfterOne = b.IsGhost;
        PerformHeroAction(a, HeroAction.Move(Vector2I.Right));
        GD.Print($"Отряд: подъём: призрак после первого шага {ghostAfterOne}, после второго {b.IsGhost}, здоровье {b.Hp}/{b.MaxHp}, поднял {a.RevivesMade}");

        a.Gold = 120;
        int goldB = b.Gold, bagA = a.Inventory.Items.Count, bagB = b.Inventory.Items.Count;
        GiveGold(a);
        GiveItem(a, 0);
        GD.Print($"Отряд: передача: золото {goldB} → {b.Gold}, сумки {bagA}/{bagB} → {a.Inventory.Items.Count}/{b.Inventory.Items.Count}");

        ClearPlates();
        var plateA = left;
        var plateB = left + Vector2I.Down * 1;
        a.Teleport(plateA + Vector2I.Up);
        PlacePlate(plateA);
        PlacePlate(right);
        b.Teleport(right);
        int goldBefore = a.Gold;
        PerformHeroAction(a, HeroAction.Move(Vector2I.Down));
        GD.Print($"Отряд: плиты: осталось {_plates.Count}, золото {goldBefore} → {a.Gold}");

        GD.Print($"Отряд: итоги: {PartySummary().Replace('\n', ';')}");
        GetTree().Quit();
    }

    /// <summary>Новые правила боя: каждый враг разыгрывается настоящими ходами, числа печатаются.</summary>
    private void RunTacticsTest()
    {
        var hero = _localHero;
        Vector2I Free(int skip = 0) => DungeonMap.Directions
            .Select(d => hero.GridPos + d).Where(c => _map.Get(c) == Tile.Floor && _map.CanMove(hero.GridPos, c) && !IsOccupied(c)).Skip(skip).First();
        Enemy Place(string name, Vector2I? at = null)
        {
            foreach (var old in _enemies.ToList())
            {
                _enemies.Remove(old);
                old.QueueFree();
            }
            var enemy = SpawnEnemy(Enemy.FindType(name), at ?? Free());
            enemy.Alerted = true;
            hero.Heal(hero.MaxHp);
            return enemy;
        }
        void Wait() => PerformHeroAction(hero, HeroAction.Wait);

        var brute = Place("Громила");
        Wait();
        int hp = hero.Hp;
        var away = DungeonMap.Directions.Where(d => _map.CanMove(hero.GridPos, hero.GridPos + d) && !IsOccupied(hero.GridPos + d) && _map.Get(hero.GridPos + d) == Tile.Floor
            && !_map.AreAdjacent(brute.GridPos, hero.GridPos + d) && hero.GridPos + d != brute.GridPos).FirstOrDefault();
        PerformHeroAction(hero, HeroAction.Move(away));
        GD.Print($"Тактика: Громила: отошёл, здоровье {hp} → {hero.Hp} (должно не измениться)");

        foreach (bool guard in new[] { false, true })
        {
            brute = Place("Громила");
            Wait();
            hp = hero.Hp;
            PerformHeroAction(hero, guard ? HeroAction.Wait : HeroAction.Move(brute.GridPos - hero.GridPos));
            GD.Print($"Тактика: Громила попал, стойка {guard}: здоровье {hp} → {hero.Hp}");
        }

        var bomber = Place("Подрывник");
        Wait();
        hp = hero.Hp;
        Wait();
        GD.Print($"Тактика: Подрывник взорвался в стойке: здоровье {hp} → {hero.Hp}, враг жив: {!bomber.IsDead}, врагов {_enemies.Count}");

        var shield = Place("Щитоносец");
        for (int i = 0; i < 4; i++)
        {
            int before = shield.Hp;
            bool up = shield.ShieldUp;
            if (shield.IsDead)
                break;
            PerformHeroAction(hero, HeroAction.Move(shield.GridPos - hero.GridPos));
            GD.Print($"Тактика: Щитоносец: щит {up}, здоровье {before} → {shield.Hp}");
        }

        var goblin = Place("Гоблин");
        var shaman = SpawnEnemy(Enemy.FindType("Шаман"), Free(0));
        shaman.Alerted = true;
        goblin.TakeDamage(5);
        hp = goblin.Hp;
        Wait();
        GD.Print($"Тактика: Шаман лечит гоблина: {hp} → {goblin.Hp}/{goblin.MaxHp}");

        var elite = Place("Гоблин");
        int plainHp = elite.MaxHp, plainAttack = elite.Attack;
        elite.MakeElite(EliteHp);
        GD.Print($"Тактика: {elite.DisplayName}: здоровье {plainHp} → {elite.MaxHp}, атака {plainAttack} → {elite.Attack}, опыт {elite.ExperienceReward}");

        Place("Слизень", _map.StartPos == hero.GridPos ? null : null);
        foreach (var enemy in _enemies.ToList())
        {
            _enemies.Remove(enemy);
            enemy.QueueFree();
        }
        foreach (ShrineKind kind in System.Enum.GetValues<ShrineKind>())
        {
            var cell = Free();
            PlaceShrine(cell, kind, explained: false);
            hero.TakeDamage(5);
            hero.Gold = 500;
            int attack = hero.Attack, life = hero.Hp, gold = hero.Gold, bag = hero.Inventory.Items.Count;
            bool first = PerformHeroAction(hero, HeroAction.Move(cell - hero.GridPos));
            bool second = PerformHeroAction(hero, HeroAction.Move(cell - hero.GridPos));
            GD.Print($"Тактика: {kind}: первый шаг тратит ход {first}, второй {second}, здоровье {life} → {hero.Hp}, атака {attack} → {hero.Attack}, золото {gold} → {hero.Gold}, сумка {bag} → {hero.Inventory.Items.Count}, осталось на этаже {_shrines.ContainsKey(cell)}");
        }

        foreach (var playerClass in ClassCatalog.All)
        {
            hero.ResetStats(playerClass);
            GD.Print($"Тактика: улучшения {playerClass.Name}: {string.Join(", ", ClassCatalog.RollPerks(_rng, hero).Select(perk => perk.Name))}");
        }

        var saved = Place("Гоблин");
        saved.MakeElite(EliteHp);
        PlaceShrine(Free(), ShrineKind.Fountain, explained: true);
        string before2 = WorldSummary();
        SaveRun();
        int shrines = _shrines.Count;
        ShowMainMenu();
        ContinueGame();
        GD.Print($"Тактика: загрузка: святынь {shrines} → {_shrines.Count}, элитных {_enemies.Count(enemy => enemy.IsElite)}, сводка {(before2 == WorldSummary() ? "СОВПАДАЕТ" : "РАЗЛИЧАЕТСЯ")}");
        GetTree().Quit();
    }

    /// <summary>Правило зарядки: ходы не заряжают, удар и вторая боевая способность — заряжают.</summary>
    private void RunChargeTest()
    {
        var hero = _localHero;
        foreach (var enemy in _enemies.ToList())
        {
            _enemies.Remove(enemy);
            enemy.QueueFree();
        }
        var cell = DungeonMap.Directions.Select(d => hero.GridPos + d).First(c => _map.Get(c) == Tile.Floor && _map.CanMove(hero.GridPos, c));
        var target = SpawnEnemy(Enemy.Types.First(type => type.Name == "Мимик"), cell);
        target.Alerted = true;
        string Cooldowns() => $"F={hero.SkillCooldowns[0]} G={hero.SkillCooldowns[1]}";
        bool Act(string name, HeroAction action)
        {
            bool spent = PerformHeroAction(hero, action);
            GD.Print($"Зарядка: {name}: потрачен ход {spent}, {Cooldowns()}");
            return spent;
        }

        GD.Print($"Зарядка: {hero.Class.Name}, старт {Cooldowns()}, значения F={hero.Class.Skills[0].Cooldown} G={hero.Class.Skills[1].Cooldown}");
        Act("F", HeroAction.Skill(0));
        Act("F ещё раз (не заряжена)", HeroAction.Skill(0));
        hero.TakeDamage(5);
        Act("G", HeroAction.Skill(1));
        for (int i = 0; i < 3; i++)
            Act("ожидание", HeroAction.Wait);
        if (!target.IsDead)
            Act("удар", HeroAction.Move(target.GridPos - hero.GridPos));
        GetTree().Quit();
    }

    /// <summary>Услуги торговца теми же путями, что и кнопки окна, затем сохранение и загрузка.</summary>
    private void RunServicesTest()
    {
        var hero = _localHero;
        hero.Gold = 3000;
        if (hero.Inventory.Equipped.FirstOrDefault() == null)
        {
            hero.Inventory.TryAdd(new Item(ItemCatalog.Sword));
            hero.Equip(hero.Inventory.Items.Count - 1);
        }
        if (_merchant == null)
            SpawnMerchant();
        OpenShop();

        var gear = hero.Inventory.Equipped.First();
        int attack = hero.Attack, maxHp = hero.MaxHp, defense = hero.Defense;
        for (int i = 0; i < 4; i++)
        {
            int price = UpgradePrice(gear), gold = hero.Gold;
            OnShopService(HeroAction.Upgrade((int)gear.Slot));
            GD.Print($"Услуги: заточка за {price}: {gear.Name}, списано {gold - hero.Gold}");
        }
        GD.Print($"Услуги: атака {attack} → {hero.Attack}, защита {defense} → {hero.Defense}, здоровье {maxHp} → {hero.MaxHp}");

        hero.TakeDamage(hero.MaxHp / 2);
        int hp = hero.Hp, before = hero.Gold;
        OnShopService(HeroAction.Treat);
        GD.Print($"Услуги: лечение {hp} → {hero.Hp}/{hero.MaxHp}, списано {before - hero.Gold}");

        before = hero.Gold;
        int bag = hero.Inventory.Items.Count;
        OnShopService(HeroAction.Mystery);
        GD.Print($"Услуги: ларец: сумка {bag} → {hero.Inventory.Items.Count}, списано {before - hero.Gold}, вещь: {hero.Inventory.Items[^1].Name}");

        int stock = _merchant.Stock.Count;
        before = hero.Gold;
        OnShopBuy(0);
        GD.Print($"Услуги: покупка: товаров {stock} → {_merchant.Stock.Count}, списано {before - hero.Gold}");
        stock = _merchant.Stock.Count;
        before = hero.Gold;
        OnShopSell(0);
        GD.Print($"Услуги: продажа: товаров {stock} → {_merchant.Stock.Count}, получено {hero.Gold - before}");

        hero.Gold = 0;
        before = gear.Upgrade;
        OnShopService(HeroAction.Upgrade((int)gear.Slot));
        GD.Print($"Услуги: без денег заточка {before} → {gear.Upgrade} (должна остаться прежней)");

        CloseShop();
        string saved = WorldSummary();
        SaveRun();
        ShowMainMenu();
        ContinueGame();
        GD.Print($"Услуги: после загрузки {(saved == WorldSummary() ? "СОВПАДАЕТ" : "РАЗЛИЧАЕТСЯ")}, надето: {string.Join(", ", _localHero.Inventory.Equipped.Select(item => item.Name))}");
        GetTree().Quit();
    }

    /// <summary>Каждый герой, который ещё не сходил в этом раунде, делает ход автопилотом.</summary>
    private void AutoPilotRound()
    {
        switch (_state)
        {
            case GameState.LevelUp:
                OnPerkChosen(ClassCatalog.RollPerks(_rng, _localHero)[0]);
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
            if (_simShop)
                BotUseServices();
            AutoPilotRound();
        }
    }

    private void PrintSimulationSummary()
    {
        GD.Print("СИМУЛЯЦИЯ: класс | сложность | забегов | средний этаж | медиана | дошли до 5 | до 10 | победы | ср. уровень | зависли");
        if (_simShop)
            GD.Print($"СИМУЛЯЦИЯ: на услуги потрачено в среднем {_goldSpentOnServices / _simResults.Count} золота за забег");
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

    /// <summary>Замер плавности: самая долгая пауза между движениями героя за время ходьбы.</summary>
    private void TrackWalkGaps()
    {
        if (_testHeldKey == Key.None)
            return;
        ulong now = Time.GetTicksMsec();
        if (_localHero.Position == _lastWalkPosition)
            return;
        // Первый шаг и паузу HoldDelay не считаем: герой ещё не идёт непрерывно
        if (_walkMoves++ > 20)
            _longestWalkGapMs = System.Math.Max(_longestWalkGapMs, now - _lastWalkMoveMs);
        _lastWalkPosition = _localHero.Position;
        _lastWalkMoveMs = now;
    }

    private void ReleaseHeldKey()
    {
        if (_testHeldKey == Key.None)
            return;
        GD.Print($"Удержание: самая долгая остановка в ходьбе {_longestWalkGapMs} мс, кадров с движением {_walkMoves}");
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
