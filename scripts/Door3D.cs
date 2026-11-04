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

        // Доски, железные полосы и кольцо-ручка. Железо темнеет вместе с дверью в тумане войны
        _iron = new StandardMaterial3D { AlbedoColor = Colors.Black, Roughness = 0.45f, Metallic = 0.5f };
        foreach (float side in new[] { -1f, 1f })
        {
            AddIron(Shapes.Cube(Width - 0.06f, 0.07f, 0.14f), new Vector3(Width / 2f, PanelHeight / 2f + side * 0.27f, 0f));
            AddIron(Shapes.Ball(0.03f), new Vector3(0.08f, PanelHeight / 2f + side * 0.27f, side * 0.08f));
        }
        foreach (float x in new[] { 0.2f, 0.4f, 0.6f, 0.8f })
            _hinge.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.012f, PanelHeight, 0.125f), Material = Groove },
                Position = new Vector3(x, PanelHeight / 2f, 0f),
            });
        AddIron(Shapes.Ring(0.05f, 0.012f), new Vector3(0.84f, 0.5f, 0.08f), new Vector3(90f, 0f, 0f));
    }

    private StandardMaterial3D _iron;
    private static readonly StandardMaterial3D Groove = new() { AlbedoColor = Colors.Black };

    private void AddIron(PrimitiveMesh mesh, Vector3 position, Vector3? turn = null)
    {
        Shapes.Simplify(mesh);
        mesh.Material = _iron;
        _hinge.AddChild(new MeshInstance3D { Mesh = mesh, Position = position, RotationDegrees = turn ?? Vector3.Zero });
    }

    public void SetColor(Color color)
    {
        _material.AlbedoColor = color;
        _iron.AlbedoColor = color.Lerp(new Color(0.55f, 0.55f, 0.6f), color.V).Darkened(0.35f);
    }

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
