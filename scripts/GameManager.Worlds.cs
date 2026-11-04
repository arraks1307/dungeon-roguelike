using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>
/// Часть GameManager про миры: создание, вход в лагерь выбранного мира и спуски с неизменным героем.
/// Сетевая игра живёт отдельно: там каждый выбирает героя в лобби.
/// </summary>
public partial class GameManager
{
    private WorldPanel _worldPanel;
    private WorldCreatePanel _worldCreatePanel;
    private HeroBuildPanel _heroBuildPanel;
    private WorldInfo _draftWorld;
    private bool _worldLost;

    private bool InWorld => Worlds.Current != null && !_isNetworkGame;

    /// <summary>Этаж финального босса: в мире его задаёт длина подземелья.</summary>
    private int FinalFloor => InWorld ? Worlds.Current.FinalFloor : 15;

    /// <summary>Во сколько раз чаще обычного на этажах случаются события.</summary>
    private double EventScale => InWorld ? Worlds.Current.EventScale : 1.0;

    private void SetupWorlds()
    {
        _worldPanel = new WorldPanel();
        AddChild(_worldPanel);
        _worldPanel.WorldChosen += OpenWorld;
        _worldPanel.CreateRequested += OpenWorldCreate;
        _worldPanel.Closed += CloseWorlds;

        _worldCreatePanel = new WorldCreatePanel();
        AddChild(_worldCreatePanel);
        _worldCreatePanel.Confirmed += world =>
        {
            _draftWorld = world;
            _worldCreatePanel.Visible = false;
            _classSelect.ShowDifficulty = false;
            _classSelect.ConfirmText = "Выбрать этого героя";
            _classSelect.Open();
        };
        _worldCreatePanel.Closed += CloseWorldCreate;

        _heroBuildPanel = new HeroBuildPanel();
        AddChild(_heroBuildPanel);
        _heroBuildPanel.Confirmed += FinishWorldCreation;
        _heroBuildPanel.Closed += CloseHeroBuild;
    }

    private void CloseWorldCreate()
    {
        _draftWorld = null;
        _worldCreatePanel.Visible = false;
        _worldPanel.Open();
    }

    private void CloseHeroBuild()
    {
        _heroBuildPanel.Visible = false;
        _classSelect.Open();
    }

    private void OpenWorlds()    {
        _mainMenu.Visible = false;
        _worldPanel.Open();
    }

    private void CloseWorlds()
    {
        _worldPanel.Visible = false;
        _mainMenu.ShowMain();
    }

    private void OpenWorldCreate()
    {
        _worldPanel.Visible = false;
        _worldCreatePanel.Open();
    }

    /// <summary>Выбран герой: в создании мира дальше идут очки, иначе обычный старт.</summary>
    private void OnClassChosen(ClassDef playerClass, DifficultyDef difficulty)
    {
        if (_draftWorld == null)
        {
            StartNewGame(playerClass, difficulty);
            return;
        }
        _classSelect.Visible = false;
        _draftWorld.ClassIndex = System.Array.IndexOf(ClassCatalog.All, playerClass);
        _heroBuildPanel.Open(_draftWorld);
    }

    private void FinishWorldCreation()
    {
        var world = _draftWorld;
        _draftWorld = null;
        _classSelect.ShowDifficulty = true;
        _classSelect.ConfirmText = "Начать";
        _heroBuildPanel.Visible = false;
        Worlds.Create(world);
        OpenWorld(world);
    }

    private void OpenWorld(WorldInfo world)
    {
        _worldPanel.Visible = false;
        Worlds.Select(world);
        _isNetworkGame = false;
        _worldLost = false;
        EnterCamp();
    }

    /// <summary>Выход из мира в главное меню.</summary>
    private void CloseWorld()
    {
        ShowMainMenu();
        Worlds.Close();
    }

    /// <summary>Куда вернуться после спуска: в лагерь своего мира или в главное меню.</summary>
    private void GoHome()
    {
        ShowMainMenu();
        if (InWorld)
            EnterCamp();
    }

    /// <summary>Спуск из лагеря: начатый продолжается, иначе новый тем же неизменным героем.</summary>
    private void StartDescent()
    {
        var world = Worlds.Current;
        LeaveCamp();
        _state = GameState.Menu;
        _isNetworkGame = false;
        _worldLost = false;
        if (SaveSystem.HasSave)
        {
            ContinueGame();
            return;
        }

        world.Descents++;
        Worlds.Save(world);
        _rng = new GameRandom(unchecked(world.Seed + world.Descents * 7919));
        var party = new List<HeroSpec> { new(world.Class) };
        var difficulty = Difficulties.Get(world.Difficulty);
        if (world.Descents == 1)
            OfferTutorial(world.Class, null, () => StartRun(party, difficulty));
        else
            StartRun(party, difficulty);
    }

    /// <summary>Очки и черта, выбранные при создании мира, действуют на каждом спуске.</summary>
    private void ApplyWorldBuild(Player hero)
    {
        var world = Worlds.Current;
        hero.SetBuild(Worlds.BuildBonus(world));
        hero.Heal(hero.MaxHp);
        hero.Gold += Worlds.StartGold(world);
        for (int i = 0; i < Worlds.StartPotions(world); i++)
            hero.Inventory.TryAdd(new Item(ItemCatalog.HealthPotion));
    }

    /// <summary>Вечная смерть: забег окончен, и мир исчезает вместе с героем.</summary>
    private void LoseWorldIfNeeded()
    {
        if (!_worldLost)
            return;
        Worlds.Delete(Worlds.Current);
    }

    /// <summary>
    /// Этажи мира постоянны: планировка зависит только от зерна и номера этажа.
    /// Враги и добыча берутся из обычного потока случайных чисел и меняются от спуска к спуску.
    /// </summary>
    private System.Random LayoutRandom => InWorld ? new GameRandom(unchecked(Worlds.Current.Seed * 31 + _floor * 104729)) : _rng;
}
