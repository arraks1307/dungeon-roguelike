using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Часть GameManager про обучение: маленький уровень из трёх комнат перед каждой новой игрой.
/// Игрок управляет настоящим героем выбранного класса, но герой здесь временный: ничего
/// из обучения в забег не переносится. Шаг засчитывается по состоянию мира, а не по нажатиям:
/// если человек сделал что-то заранее (подобрал вещь раньше времени), шаг просто проходит сам.
///
/// Мир обучения не сетевой: в сетевой игре каждый проходит его у себя, а настоящий мир
/// создаётся только после того, как все закончат (см. NetSession.SendReady).
/// </summary>
public partial class GameManager
{
    private enum TutorialStep
    {
        Walk,
        Door,
        PickUp,
        Equip,
        Fight,
        Skill,
        Potion,
        Stairs,
    }

    private static readonly int TutorialStepCount = Enum.GetValues<TutorialStep>().Length;

    // Клетки уровня: комнаты идут слева направо, между ними — двери
    private static readonly Vector2I TutorialStart = new(3, 5);
    private static readonly Vector2I TutorialWalkGoal = new(7, 5);
    private static readonly Vector2I TutorialDoor = new(8, 5);
    private static readonly Vector2I TutorialItemCell = new(11, 5);
    private static readonly Vector2I TutorialEnemyCell = new(15, 5);
    private static readonly Vector2I TutorialSecondDoor = new(17, 5);
    private static readonly Vector2I TutorialStairs = new(26, 5);

    private TutorialPanel _tutorialPanel;

    /// <summary>Что сделать после обучения. Не null — значит, идёт обучение.</summary>
    private Action _afterTutorial;
    private TutorialStep _tutorialStep;
    private bool _tutorialSkillUsed;
    private int _tutorialHp;

    private bool InTutorial => _afterTutorial != null;

    /// <summary>В обучении погибнуть нельзя — как и в режиме --god самопроверки.</summary>
    private bool IsInvulnerable => _godMode || InTutorial;

    private void SetupTutorial()
    {
        _tutorialPanel = new TutorialPanel();
        AddChild(_tutorialPanel);
        _hud.TutorialSkipRequested += FinishTutorial;
    }

    /// <summary>Перед новой игрой: предложить обучение, а затем запустить игру (start).</summary>
    private void OfferTutorial(ClassDef playerClass, string name, Action start)
    {
        _tutorialPanel.OpenOffer(
            onTake: () =>
            {
                _tutorialPanel.Visible = false;
                StartTutorial(playerClass, name, start);
            },
            onSkip: () =>
            {
                _tutorialPanel.Visible = false;
                start();
            });
    }

    private void StartTutorial(ClassDef playerClass, string name, Action after)
    {
        _afterTutorial = after;
        _tutorialStep = TutorialStep.Walk;
        _tutorialSkillUsed = false;

        _state = GameState.Playing;
        _gameOver = false;
        _victory = false;
        _difficulty = Difficulties.Normal;
        _floor = 1;
        _kills = 0;
        _turns = 0;
        _descentRequested = false;
        _mainMenu.Visible = false;
        _classSelect.Visible = false;
        _lobby.Visible = false;
        ClearLevelObjects();

        _hud.Visible = true;
        _hud.HideGameOver();
        _hud.ClearMessages();
        _hud.CloseChat();
        _camera.MenuMode = false;

        CreateParty(new List<HeroSpec> { new(playerClass, name) }, 0);
        UiTheme.SetAccent(_localHero.Class.Color.Lightened(0.15f));
        _map = BuildTutorialMap();
        _renderer.Build(_map);
        ApplyTheme();
        _localHero.Teleport(_map.StartPos);
        _camera.SnapToTarget();

        PlaceItem(new Item(ItemCatalog.LifeAmulet), TutorialItemCell);
        Effects.Ping(this, TutorialWalkGoal, InfoColor);
        UpdateFov();
        UpdateHud();
    }

    /// <summary>Три комнаты в ряд. Размер карты обычный: мини-карта и камера работают как в игре.</summary>
    private static DungeonMap BuildTutorialMap()
    {
        var map = new DungeonMap(MapWidth, MapHeight) { StartPos = TutorialStart, StairsPos = TutorialStairs };
        foreach (var room in new[] { new Rect2I(1, 3, 7, 5), new Rect2I(9, 3, 8, 5), new Rect2I(18, 3, 10, 5) })
        {
            map.Rooms.Add(room);
            for (int x = room.Position.X; x < room.End.X; x++)
                for (int y = room.Position.Y; y < room.End.Y; y++)
                    map.Tiles[x, y] = Tile.Floor;
        }
        map.Tiles[TutorialDoor.X, TutorialDoor.Y] = Tile.Door;
        map.Tiles[TutorialSecondDoor.X, TutorialSecondDoor.Y] = Tile.Door;
        map.Tiles[TutorialStairs.X, TutorialStairs.Y] = Tile.Stairs;
        return map;
    }

    /// <summary>Вызывается из UpdateHud: после любого действия смотрим, выполнен ли текущий шаг.</summary>
    private void UpdateTutorial()
    {
        if (!InTutorial)
            return;
        while (_tutorialStep != TutorialStep.Stairs && IsTutorialStepDone())
        {
            _tutorialStep++;
            EnterTutorialStep();
            if (IsTestRun)
                GD.Print($"Обучение: шаг {(int)_tutorialStep + 1} — {_tutorialStep}");
        }

        // Враг шага со способностью мог погибнуть от обычного удара — тогда нужен новый
        if (_tutorialStep == TutorialStep.Skill && _enemies.Count == 0)
            SpawnTutorialEnemy(Enemy.Types[1]);

        var (title, text) = TutorialStepText();
        _hud.ShowTutorial((int)_tutorialStep + 1, TutorialStepCount, title, text);
    }

    private bool IsTutorialStepDone()
    {
        var hero = _localHero;
        return _tutorialStep switch
        {
            TutorialStep.Walk => hero.GridPos.X >= TutorialWalkGoal.X,
            TutorialStep.Door => _map.Get(TutorialDoor) != Tile.Door,
            TutorialStep.PickUp => !_floorItems.ContainsKey(TutorialItemCell),
            TutorialStep.Equip => hero.Inventory.Equipped.Any(item => item.Template == ItemCatalog.LifeAmulet),
            TutorialStep.Fight => _enemies.Count == 0,
            TutorialStep.Skill => _tutorialSkillUsed,
            TutorialStep.Potion => hero.Hp > _tutorialHp,
            _ => false,
        };
    }

    private void EnterTutorialStep()
    {
        var hero = _localHero;
        switch (_tutorialStep)
        {
            case TutorialStep.Fight:
                SpawnTutorialEnemy(Enemy.Types[0]);
                break;
            case TutorialStep.Potion:
                // Рану наносим сами: враги в обучении урона не оставляют
                hero.Inventory.TryAdd(new Item(ItemCatalog.HealthPotion));
                hero.TakeDamage(hero.MaxHp / 2);
                Effects.FloatingText(this, hero.GridPos, $"-{hero.MaxHp / 2}", DamageColor);
                _sound.Play(Sound.Hurt);
                _tutorialHp = hero.Hp;
                break;
            case TutorialStep.Stairs:
                Effects.Ping(this, TutorialStairs, InfoColor);
                break;
        }
    }

    private void SpawnTutorialEnemy(EnemyType type)
    {
        // Ближайшая к нужной клетке свободная клетка: герой мог уже стоять там, где ждали врага
        var distances = _map.DistanceField(TutorialEnemyCell, 6);
        var cell = distances.Keys
            .Where(c => _map.Get(c) == Tile.Floor && !IsOccupied(c) && ChebyshevDistance(c, _localHero.GridPos) >= 2)
            .OrderBy(c => distances[c])
            .FirstOrDefault(TutorialEnemyCell);
        var enemy = SpawnEnemy(type, cell);
        enemy.Alerted = true;
        Effects.Burst(this, cell, type.Color, 14);
        UpdateFov();
    }

    private (string Title, string Text) TutorialStepText()
    {
        var hero = _localHero;
        var skill = hero.Class.Skills[0];
        return _tutorialStep switch
        {
            TutorialStep.Walk => ("Ходьба",
                "Шагай клавишами WASD или стрелками, по диагонали — Q E Z C. Одно нажатие — один ход. " +
                "Если держать клавишу, герой идёт сам и остановится, когда случится что-то важное. Дойди до двери справа."),
            TutorialStep.Door => ("Двери",
                "Шагни в дверь, чтобы открыть её. Это тратит ход. Враги ходят только после тебя, так что спешить некуда."),
            TutorialStep.PickUp => ("Предметы",
                "Вещи и золото подбираются сами, если наступить на них. Подбери амулет в этой комнате."),
            TutorialStep.Equip => ("Снаряжение",
                "Нажми I, чтобы открыть инвентарь. Выбери амулет и нажми Enter, чтобы надеть. X выбрасывает вещь, Esc закрывает окно."),
            TutorialStep.Fight => ("Бой",
                "Появился враг! Чтобы ударить, шагни в него. Пробел пропускает ход — можно дать врагу подойти самому."),
            TutorialStep.Skill => ("Способности",
                $"F и G — способности, у каждого героя свои. Подойди к врагу и нажми F: «{skill.Name}» — {skill.Description}. " +
                "После применения способность какое-то время перезаряжается."),
            TutorialStep.Potion => ("Зелья",
                "Герой ранен! Первые пять ячеек сумки срабатывают клавишами 1–5: нажми цифру ячейки с зельем лечения. " +
                "Ячейки видны справа внизу."),
            _ => ("Лестница",
                "Золотая лестница ведёт на следующий этаж: встань на неё, и обучение закончится. " +
                "Клавиши всегда можно посмотреть в паузе (Esc → Управление)." +
                (_net.IsActive ? " В сетевой игре Enter открывает чат, а правая кнопка мыши ставит метку на карте." : "")),
        };
    }

    /// <summary>Обучение окончено или пропущено: уровень забываем, запускаем то, что ждало.</summary>
    private void FinishTutorial()
    {
        if (!InTutorial)
            return;
        var after = _afterTutorial;
        _afterTutorial = null;
        _hud.HideTutorial();
        CloseLocalWindows();
        after();
    }

    /// <summary>Выход в меню из обучения: ничего не запускаем.</summary>
    private void AbandonTutorial()
    {
        _afterTutorial = null;
        _hud.HideTutorial();
        _tutorialPanel.Visible = false;
    }
}
