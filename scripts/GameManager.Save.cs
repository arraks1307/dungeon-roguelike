using System;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Часть GameManager про сохранение: собрать снимок мира, записать его и восстановить мир из снимка.
/// Сохраняемся автоматически на каждом новом этаже, при выходе в меню и при закрытии окна.
/// После смерти сохранение удаляется — классическая «перманентная смерть» рогаликов.
/// </summary>
public partial class GameManager
{
    /// <summary>Godot присылает уведомления о событиях окна; ловим попытку закрыть игру.</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            SaveRun();
    }

    private bool IsRunInProgress =>
        _state is GameState.Playing or GameState.Paused or GameState.Inventory or GameState.Shop or GameState.LevelUp;

    private void SaveRun()
    {
        if (!IsRunInProgress || _player.IsDead || _simulateGames > 0)
            return;
        SaveSystem.Save(CreateSaveData());
    }

    private SaveData CreateSaveData()
    {
        var data = new SaveData
        {
            Floor = _floor,
            Kills = _kills,
            PendingLevelUps = _pendingLevelUps,
            Turns = _turns,
            Difficulty = _difficulty.Id,
            Player = _player.CreateSave(),
            Map = MapSave.From(_map),
            Enemies = _enemies.Where(enemy => !enemy.IsDead).Select(enemy => enemy.CreateSave()).ToList(),
            Pickups = _floorItems.Select(pair => new PickupSave
            {
                X = pair.Key.X,
                Y = pair.Key.Y,
                Gold = pair.Value.Gold,
                Item = pair.Value.Item == null ? null : ItemSave.From(pair.Value.Item),
            }).ToList(),
            Chests = _chests.Values.Select(chest => chest.CreateSave()).ToList(),
        };

        if (_merchant != null)
        {
            data.Merchant = new MerchantSave
            {
                X = _merchant.GridPos.X,
                Y = _merchant.GridPos.Y,
                Stock = _merchant.Stock.Select(ItemSave.From).ToList(),
            };
        }
        return data;
    }

    /// <summary>Кнопка «Продолжить»: загрузить сохранение и вернуться в забег.</summary>
    private void ContinueGame()
    {
        var data = SaveSystem.Load();
        if (data == null || !TryRestore(data))
        {
            SaveSystem.Delete();
            ShowMainMenu();
            return;
        }

        _hud.ClearMessages();
        _hud.AddMessage($"Забег продолжается. Этаж {_floor}.", InfoColor);
        UpdateFov();
        UpdateHud();
        TryShowLevelUp(); // если вышли, не успев выбрать улучшение
    }

    private bool TryRestore(SaveData data)
    {
        try
        {
            ClearLevelObjects();
            _floor = data.Floor;
            _kills = data.Kills;
            _pendingLevelUps = data.PendingLevelUps;
            _turns = data.Turns;
            _victory = false;
            _difficulty = Difficulties.Get(data.Difficulty);
            _levelUpPanel.Visible = false;
            _map = data.Map.ToMap();
            _renderer.Build(_map);
            ApplyTheme();

            _state = GameState.Playing;
            _mainMenu.Visible = false;
            _hud.Visible = true;
            _hud.HideGameOver();
            _player.Visible = true;
            _camera.MenuMode = false;
            _player.RestoreSave(data.Player);
            UiTheme.SetAccent(_player.Class.Color.Lightened(0.15f));
            _camera.SnapToTarget();

            foreach (var save in data.Enemies)
            {
                var type = Enemy.FindType(save.Type);
                if (type == null)
                    continue;
                var enemy = SpawnEnemy(type, new Vector2I(save.X, save.Y));
                enemy.RestoreState(save);
                if (save.Boss)
                    _boss = enemy;
            }

            foreach (var save in data.Pickups)
            {
                var cell = new Vector2I(save.X, save.Y);
                var item = save.Item?.ToItem();
                if (item != null)
                    PlaceItem(item, cell);
                else if (save.Gold > 0)
                    PlaceGold(save.Gold, cell);
            }

            foreach (var save in data.Chests)
                PlaceChest(new Vector2I(save.X, save.Y), save.Mimic, save.Secret);

            if (data.Merchant != null)
            {
                var stock = data.Merchant.Stock.Select(s => s.ToItem()).Where(item => item != null).ToList();
                _merchant = Merchant.Create(new Vector2I(data.Merchant.X, data.Merchant.Y), stock);
                AddChild(_merchant);
            }
            return true;
        }
        catch (Exception exception)
        {
            // Сохранение повреждено так, что восстановить мир нельзя — сообщаем и начинаем с меню
            GD.PushWarning($"Не удалось загрузить сохранение: {exception.Message}");
            return false;
        }
    }
}
