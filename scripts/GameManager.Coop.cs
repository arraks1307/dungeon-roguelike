using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Roguelike;

/// <summary>
/// Часть GameManager про совместную игру: чат, метки на карте и общий спуск по лестнице.
/// Чат и метки мир не меняют, поэтому идут мимо потока действий. А вот «отряд собрался
/// у лестницы» — часть мира: решение о спуске принимают все компьютеры одинаково.
/// </summary>
public partial class GameManager
{
    /// <summary>На каком расстоянии от лестницы должен стоять каждый живой герой, чтобы отряд спустился.</summary>
    private const int StairsGatherRadius = 2;
    private const ulong PingCooldownMs = 600;

    /// <summary>Кто-то из отряда уже наступил на лестницу — ждём, пока подойдут остальные.</summary>
    private bool _descentRequested;
    private ulong _lastPingMs;

    private void SetupCoop()
    {
        _hud.ChatSubmitted += OnChatSubmitted;
        _hud.ChatCancelled += CloseChat;
        _net.ChatReceived += OnChatReceived;
        _net.PingReceived += OnPingReceived;
    }

    /// <summary>Герой отряда, которым управляет игрок peerId (null — если он уже ушёл).</summary>
    private Player HeroOfPeer(long peerId)
    {
        foreach (var (heroId, owner) in _heroOwners)
            if (owner == peerId)
                return _heroes.Find(hero => hero.Id == heroId);
        return null;
    }

    // ---------- Чат ----------

    private void OpenChat()
    {
        _state = GameState.Chat;
        _localHero.StopWalking();
        _hud.OpenChat();
    }

    private void CloseChat()
    {
        _hud.CloseChat();
        if (_state != GameState.Chat)
            return;
        _state = GameState.Playing;
        TryShowLevelUp(); // пока писали, мог закончиться раунд с новым уровнем
    }

    private void OnChatSubmitted(string text)
    {
        _net.SendChat(text);
        CloseChat();
    }

    private void OnChatReceived(long peerId, string text)
    {
        if (!_isNetworkGame)
            return;
        var hero = HeroOfPeer(peerId);
        string name = hero?.DisplayName ?? "Игрок";
        var color = hero?.Class.Color.Lightened(0.5f) ?? UiTheme.Text;
        _hud.AddMessage($"{name}: {text}", color);
        if (_netTestRole != null)
            GD.Print($"СЕТЬ: {_netTestRole}: чат: {name}: {text}");
        _sound.Play(Sound.UiClick);
    }

    // ---------- Метки ----------

    /// <summary>Правая кнопка мыши: отметить клетку под курсором для всего отряда.</summary>
    private void TryPing(Vector2 screenPosition)
    {
        if (Time.GetTicksMsec() - _lastPingMs < PingCooldownMs)
            return; // не даём засыпать друзей метками
        var cell = ScreenToCell(screenPosition);
        if (cell == null || !_map.Explored[cell.Value.X, cell.Value.Y] || !_map.IsWalkable(cell.Value))
            return;
        _lastPingMs = Time.GetTicksMsec();
        _net.SendPing(cell.Value);
    }

    /// <summary>
    /// Клетка под курсором: луч из камеры через точку экрана пересекаем с полом (плоскость y = 0).
    /// Клетка (x, y) стоит в мире в точке (x, 0, y), поэтому дальше достаточно округлить.
    /// </summary>
    private Vector2I? ScreenToCell(Vector2 screenPosition)
    {
        var origin = _camera.ProjectRayOrigin(screenPosition);
        var direction = _camera.ProjectRayNormal(screenPosition);
        if (direction.Y >= -0.001f)
            return null; // луч смотрит вверх или вдоль пола — пола он не встретит
        var hit = origin + direction * (-origin.Y / direction.Y);
        var cell = new Vector2I(Mathf.RoundToInt(hit.X), Mathf.RoundToInt(hit.Z));
        return cell.X >= 0 && cell.Y >= 0 && cell.X < _map.Width && cell.Y < _map.Height ? cell : null;
    }

    private void OnPingReceived(long peerId, Vector2I cell)
    {
        if (!_isNetworkGame || _map == null || cell.X < 0 || cell.Y < 0 || cell.X >= _map.Width || cell.Y >= _map.Height)
            return;
        var hero = HeroOfPeer(peerId);
        var color = hero?.Class.Color.Lightened(0.3f) ?? InfoColor;
        Effects.Ping(this, cell, color);
        if (_netTestRole != null)
            GD.Print($"СЕТЬ: {_netTestRole}: метка от {hero?.DisplayName} на клетке {cell}");
        _hud.AddPing(cell, color);
        _sound.Play(Sound.Secret);
        if (hero != null && !IsLocalHero(hero))
            _hud.AddMessage($"{hero.DisplayName} отмечает место на карте.", color);
    }

    // ---------- Общий спуск по лестнице ----------

    /// <summary>
    /// Герой встал на лестницу. Один — спускается сразу; в отряде — ждём, пока все живые
    /// подойдут к лестнице, чтобы никого не утащило на новый этаж посреди боя.
    /// </summary>
    private void OnHeroReachedStairs(Player hero)
    {
        if (InTutorial)
        {
            _sound.Play(Sound.Stairs);
            // Откладываем: сейчас мы ещё внутри хода героя, а обучение заменит и героя, и карту
            Callable.From(FinishTutorial).CallDeferred();
            return;
        }
        if (IsSinglePlayer)
        {
            _sound.Play(Sound.Stairs);
            NextFloor();
            return;
        }
        if (!_descentRequested)
        {
            _descentRequested = true;
            var far = HeroesFarFromStairs();
            if (far.Count > 0)
                _hud.AddMessage($"{hero.DisplayName} у лестницы. Отряд спустится, когда все подойдут к ней " +
                                $"(ждём: {string.Join(", ", far.Select(other => other.DisplayName))}).", InfoColor);
        }
        TryDescend();
    }

    private List<Player> HeroesFarFromStairs() => LivingHeroes
        .Where(hero => ChebyshevDistance(hero.GridPos, _map.StairsPos) > StairsGatherRadius)
        .ToList();

    /// <summary>Если спуск уже запрошен и весь отряд рядом с лестницей — спускаемся. true — новый этаж.</summary>
    private bool TryDescend()
    {
        if (!_descentRequested || _gameOver || _map.Get(_map.StairsPos) != Tile.Stairs || HeroesFarFromStairs().Count > 0)
            return false;
        _hud.AddMessage("Отряд собрался — спускаемся!", InfoColor);
        _sound.Play(Sound.Stairs);
        NextFloor();
        return true;
    }

    /// <summary>Для HUD: кого ждём у лестницы (null — спуск не запрошен).</summary>
    private List<string> StairsWaitingNames() => _descentRequested && !_gameOver && !IsSinglePlayer
        ? HeroesFarFromStairs().Select(hero => hero.DisplayName).ToList()
        : null;
}
