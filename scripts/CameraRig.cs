using Godot;

namespace Roguelike;

/// <summary>
/// Камера: плавно следует за целью, умеет приближаться колёсиком,
/// а в главном меню медленно плывёт над подземельем.
/// </summary>
public partial class CameraRig : Camera3D
{
    // Камера почти сверху (~65°), чтобы стены не загораживали игрока
    private static readonly Vector3 BaseOffset = new(0f, 17f, 8f);
    private const float MinZoom = 0.5f;
    private const float MaxZoom = 1.6f;
    private const float ZoomStep = 0.1f;
    private const float FollowSpeed = 8f;

    private float _zoom = 1f;
    private double _menuTime;

    public Node3D Target { get; set; }

    /// <summary>Режим заставки: камера летает над центром карты вместо слежения за целью.</summary>
    public bool MenuMode { get; set; }
    public Vector3 MenuCenter { get; set; }

    public override void _Ready()
    {
        Fov = 50f;
        Position = BaseOffset;
        LookAt(Vector3.Zero); // поворот задаём один раз, дальше камера только сдвигается
    }

    /// <summary>direction &lt; 0 — приблизить, &gt; 0 — отдалить.</summary>
    public void Zoom(int direction) => _zoom = Mathf.Clamp(_zoom + direction * ZoomStep, MinZoom, MaxZoom);

    public void SnapToTarget() => Position = GoalPosition();

    public override void _Process(double delta)
    {
        if (MenuMode)
            _menuTime += delta;
        // Плавное следование: каждый кадр сокращаем расстояние до цели на долю, зависящую от delta
        Position = Position.Lerp(GoalPosition(), 1f - Mathf.Exp(-FollowSpeed * (float)delta));
    }

    private Vector3 GoalPosition()
    {
        if (MenuMode)
        {
            float t = (float)_menuTime;
            var drift = new Vector3(Mathf.Sin(t * 0.15f) * 10f, 0f, Mathf.Sin(t * 0.1f) * 5f);
            return MenuCenter + drift + BaseOffset * 1.2f;
        }
        return Target.Position + BaseOffset * _zoom;
    }
}
