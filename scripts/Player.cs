using System;
using Godot;

namespace Roguelike;

/// <summary>
/// Игрок: читает клавиши движения и сообщает GameManager, что хочет сделать.
/// Сам ход (бой, движение, ответ врагов) обрабатывает GameManager.
/// Остальные клавиши (Esc, R, M, колёсико) обрабатывает тоже GameManager.
/// </summary>
public partial class Player : Actor
{
    /// <summary>Направление шага. Vector2I.Zero означает «пропустить ход».</summary>
    public event Action<Vector2I> ActionRequested;

    public int Level { get; private set; }

    public override void _Ready()
    {
        DisplayName = "Ты";
        CreateBody(new CapsuleMesh { Radius = 0.25f, Height = 0.9f }, new Color(0.3f, 0.55f, 1f), 0.45f);

        // «Факел» — тёплый точечный свет, который двигается вместе с игроком
        AddChild(new OmniLight3D
        {
            Position = new Vector3(0f, 1.6f, 0f),
            LightColor = new Color(1f, 0.75f, 0.45f),
            LightEnergy = 1.8f,
            OmniRange = 8f,
            ShadowEnabled = true,
        });

        ResetStats();
    }

    public void ResetStats()
    {
        Level = 1;
        MaxHp = 30;
        Hp = MaxHp;
        Attack = 5;
    }

    public void LevelUp()
    {
        Level++;
        MaxHp += 5;
        Hp = Mathf.Min(Hp + 5, MaxHp);
        Attack += 1;
    }

    public void Heal(int amount) => Hp = Mathf.Min(Hp + amount, MaxHp);

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key)
            return;

        // PhysicalKeycode — физическое положение клавиши, поэтому WASD работает
        // и при русской раскладке (там на этих клавишах «ЦФЫВ»)
        Vector2I? direction = key.PhysicalKeycode switch
        {
            Key.W or Key.Up or Key.Kp8 => new Vector2I(0, -1),
            Key.S or Key.Down or Key.Kp2 => new Vector2I(0, 1),
            Key.A or Key.Left or Key.Kp4 => new Vector2I(-1, 0),
            Key.D or Key.Right or Key.Kp6 => new Vector2I(1, 0),
            Key.Q or Key.Kp7 => new Vector2I(-1, -1),
            Key.E or Key.Kp9 => new Vector2I(1, -1),
            Key.Z or Key.Kp1 => new Vector2I(-1, 1),
            Key.C or Key.Kp3 => new Vector2I(1, 1),
            Key.Space or Key.Kp5 => Vector2I.Zero,
            _ => null,
        };

        if (direction.HasValue)
        {
            ActionRequested?.Invoke(direction.Value);
            GetViewport().SetInputAsHandled();
        }
    }
}
