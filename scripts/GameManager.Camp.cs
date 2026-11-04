using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Часть GameManager про лагерь: вход и выход, ходьба героя и разговоры с постройками.
/// Лагерь работает только на этом компьютере и не трогает игровой мир, поэтому общий поток действий
/// и случайные числа забега здесь не нужны.
/// </summary>
public partial class GameManager
{
    // Низкая камера: виден лес и горизонт, а не только карта сверху
    private static readonly Vector3 CampCamera = new(0f, 6.6f, 8.6f);

    private CampScene _camp;
    private Player _campHero;
    private CanvasLayer _campOverlay;
    private Label _campShards;
    private Label _campHint;
    private CampStation _campStation;
    private Color _menuBackground;

    private bool InCamp => _camp != null;

    private void EnterCamp()
    {
        _mainMenu.Visible = false;
        _state = GameState.Camp;
        _renderer.Visible = false;
        _menuBackground = _environment.BackgroundColor;
        _environment.BackgroundColor = new Color(0.02f, 0.03f, 0.07f);
        _environment.AmbientLightColor = new Color(0.3f, 0.38f, 0.62f);
        _environment.AmbientLightEnergy = 0.5f;
        _environment.TonemapExposure = 1.5f;
        _environment.GlowIntensity = 0.75f;
        _environment.GlowBloom = 0.1f;
        _environment.AdjustmentContrast = 1.16f;
        _environment.AdjustmentSaturation = 1.1f;
        _environment.SsaoEnabled = Settings.HighQuality;
        _environment.VolumetricFogEnabled = Settings.HighQuality;
        _environment.VolumetricFogDensity = 0.03f;
        _environment.VolumetricFogAlbedo = new Color(0.62f, 0.64f, 0.7f);
        _environment.VolumetricFogEmission = new Color(0.02f, 0.03f, 0.06f);

        _camp = new CampScene();
        AddChild(_camp);

        _campHero = new Player { IsLocal = true, IsBot = false };
        AddChild(_campHero);
        _camp.Focus = _campHero;
        var playerClass = Worlds.Current?.Class ?? ClassCatalog.Warrior;
        _campHero.ResetStats(playerClass);
        _campHero.SetHighQuality(Settings.HighQuality);
        _campHero.Teleport(CampScene.StartCell);
        _campHero.ActionRequested += OnCampMove;
        _campHero.WalkRequested += OnCampMove;

        _camera.MenuMode = false;
        _camera.Target = _campHero;
        _camera.SetView(CampCamera);
        // Размытие вдали, как у кинокамеры: взгляд остаётся на герое и костре
        if (Settings.HighQuality)
            _camera.Attributes = new CameraAttributesPractical { DofBlurFarEnabled = true, DofBlurFarDistance = 17f, DofBlurFarTransition = 10f, DofBlurAmount = 0.06f };
        _camera.SnapToTarget();
        UiTheme.SetAccent(playerClass.Color.Lightened(0.15f));
        CreateCampOverlay();
        UpdateCampOverlay();
    }

    /// <summary>Убрать лагерь. Следом ShowMainMenu или новый забег восстановят всё остальное.</summary>
    private void LeaveCamp()
    {
        if (!InCamp)
            return;
        _camera.SetView(null);
        _camera.Attributes = null;
        _camera.MenuMode = true;
        _camera.Target = null;
        _camp.QueueFree();
        _camp = null;
        _campHero.QueueFree();
        _campHero = null;
        _campOverlay.QueueFree();
        _choicePanel.Visible = false;
        _legacyPanel.Visible = false;
        _renderer.Visible = true;
        _environment.BackgroundColor = _menuBackground;
        _environment.TonemapExposure = 1.9f;
        _environment.GlowIntensity = 0.5f;
        _environment.GlowBloom = 0.06f;
        _environment.AdjustmentContrast = 1.1f;
        _environment.AdjustmentSaturation = 1.15f;
        _environment.VolumetricFogDensity = 0.026f;
        _environment.VolumetricFogEmission = Colors.Black;
        ApplyGraphicsQuality();
    }

    private void CreateCampOverlay()
    {
        _campOverlay = new CanvasLayer { Layer = 4 };
        AddChild(_campOverlay);
        var root = new Control { Theme = UiTheme.Main, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _campOverlay.AddChild(root);
        root.AddChild(UiTheme.CreateVignette(0.6f));

        _campShards = Ui.CreateLabel(22);
        _campShards.Position = new Vector2(28f, 20f);
        _campShards.AddThemeColorOverride("font_color", new Color(0.6f, 0.85f, 1f));
        root.AddChild(_campShards);

        _campHint = Ui.CreateLabel(18);
        _campHint.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _campHint.OffsetTop = -56f;
        _campHint.OffsetBottom = -20f;
        _campHint.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(_campHint);
    }

    private void UpdateCampOverlay()
    {
        string world = Worlds.Current != null ? $"{Worlds.Current.Name}  •  {Worlds.HeroSummary(Worlds.Current)}  •  " : "";
        _campShards.Text = $"{world}Осколки: {Profile.Current.Shards}";
        var near = _camp.StationNear(_campHero.GridPos);
        _campHint.Text = near != null
            ? $"{near.Name}: шагни сюда, чтобы подойти"
            : "Стрелки или WASD — идти  •  Шаг в постройку — разговор  •  Esc — в меню";
    }

    private void OnCampMove(Vector2I direction)
    {
        if (_state != GameState.Camp || direction == Vector2I.Zero)
            return;
        var target = _campHero.GridPos + direction;
        var station = _camp.StationAt(target);
        if (station != null)
        {
            _campHero.StopWalking();
            OpenCampStation(station);
            return;
        }
        if (_camp.IsWalkable(target))
        {
            _campHero.MoveTo(target);
            UpdateCampOverlay();
        }
    }

    private void OpenCampStation(CampStation station)
    {
        if (station == CampScene.Cave)
        {
            StartDescent();
            return;
        }
        _state = GameState.CampDialog;
        if (station == CampScene.Board)
        {
            _legacyPanel.Open();
            return;
        }
        _campStation = station;
        ShowCampDialog();
    }

    private void ShowCampDialog()
    {
        var station = _campStation;
        var resident = station.Resident == null ? null : Array.Find(Story.Residents, item => item.Id == station.Resident);
        bool alive = resident == null || Profile.Current.Rescued.Contains(resident.Id);
        if (!alive)
        {
            _choicePanel.Open(station.Name, $"Здесь пусто. {resident.Name} томится в подземелье: найди и спаси, и это место оживёт.", new List<string>(), "Отойти");
            return;
        }
        if (station == CampScene.Fire)
        {
            var profile = Profile.Current;
            _choicePanel.Open(station.Name,
                $"{station.Text}\n\n{(Worlds.Current != null ? $"Герой мира: {Worlds.HeroSummary(Worlds.Current)}\n" : "")}Победы: {profile.Wins}   Лучший этаж: {profile.BestFloor}   Жителей: {profile.Rescued.Count} из {Story.Residents.Length}   Записей: {profile.Notes.Count} из {Story.Notes.Length}",
                new List<string>(), "Отойти");
            return;
        }

        string text = resident != null ? $"{resident.Name}. {resident.Camp}" : station.Text;
        _choicePanel.Open(station.Name, $"{text}\n\nОсколки: {Profile.Current.Shards}", CampUpgrades(station).Select(UpgradeLine).ToList(), "Отойти");
    }

    private static IEnumerable<LegacyUpgrade> CampUpgrades(CampStation station) =>
        station.Upgrades.Select(id => Profile.Upgrades.First(upgrade => upgrade.Id == id));

    private static string UpgradeLine(LegacyUpgrade upgrade)
    {
        int level = Profile.Level(upgrade.Id);
        string cost = level >= upgrade.MaxLevel ? "максимум" : $"{Profile.Price(upgrade)} осколков";
        return $"{upgrade.Name} {level}/{upgrade.MaxLevel}: {upgrade.Description} ({cost})";
    }

    /// <summary>Выбрали улучшение в разговоре: покупаем и показываем окно снова, можно взять ещё.</summary>
    private void OnCampChoice(int index)
    {
        var upgrade = CampUpgrades(_campStation).ElementAt(index);
        if (Profile.TryBuy(upgrade))
        {
            _sound.Play(Sound.LevelUp);
            _camp.Rebuild();
            UpdateCampOverlay();
        }
        else
        {
            _sound.Play(Sound.Hurt);
        }
        ShowCampDialog();
    }

    private void CloseCampDialog()
    {
        _choicePanel.Visible = false;
        _legacyPanel.Visible = false;
        _state = GameState.Camp;
        _camp.Rebuild(); // на доске могли купить постройку
        UpdateCampOverlay();
    }
}
