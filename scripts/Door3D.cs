using Godot;

namespace Roguelike;

/// <summary>
/// Дверь: створка на «петле». Петля — отдельный узел на краю клетки; если повернуть его на 90°,
/// створка отъедет к стене, как настоящая дверь. SpansX — створка лежит вдоль оси X (проход сверху вниз).
/// </summary>
public partial class Door3D : Node3D
{
    private const float Width = 1f;
    private const float PanelHeight = 0.9f;

    public bool SpansX { get; set; }

    private Node3D _hinge;
    private StandardMaterial3D _material;
    private bool _open;

    public override void _Ready()
    {
        // Для створки вдоль Z просто поворачиваем всю дверь на 90°
        if (!SpansX)
            RotationDegrees = new Vector3(0f, 90f, 0f);

        _material = new StandardMaterial3D { AlbedoColor = Colors.Black, Roughness = 0.8f };
        _hinge = new Node3D { Position = new Vector3(-Width / 2f, 0f, 0f) };
        AddChild(_hinge);
        _hinge.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(Width, PanelHeight, 0.12f), Material = _material },
            Position = new Vector3(Width / 2f, PanelHeight / 2f, 0f),
        });
    }

    public void SetColor(Color color) => _material.AlbedoColor = color;

    public void Open(bool instant = false)
    {
        if (_open)
            return;
        _open = true;
        var target = new Vector3(0f, -90f, 0f);
        if (instant || Settings.AnimationScale == 0f)
        {
            _hinge.RotationDegrees = target;
            return;
        }
        CreateTween().TweenProperty(_hinge, "rotation_degrees", target, 0.25f * Settings.AnimationScale)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }
}
