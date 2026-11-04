using Godot;

namespace Roguelike;

/// <summary>
/// То, что лежит на полу: предмет или кучка золота. Медленно вращается и покачивается,
/// а редкие и эпические вещи ещё и светятся цветом редкости.
/// </summary>
public partial class ItemPickup : Node3D
{
    private const float FloatHeight = 0.3f;
    private static readonly Color GoldColor = new(1f, 0.8f, 0.25f);

    /// <summary>Предмет или null, если это золото.</summary>
    public Item Item { get; private set; }
    public int Gold { get; private set; }

    public Color MapColor => Item?.Color ?? GoldColor;

    private Node3D _model;
    private float _time;

    public static ItemPickup Create(Item item, Vector2I cell)
    {
        var pickup = new ItemPickup { Item = item, Position = Actor.GridToWorld(cell) };

        pickup.CreateModel();
        pickup.BuildModel(item);

        if (item.Rarity >= Rarity.Epic)
            pickup.AddBeam(item.RarityColor);
        if (item.Rarity != Rarity.Common)
        {
            pickup.AddChild(new OmniLight3D
            {
                LightColor = item.RarityColor,
                LightEnergy = item.Rarity == Rarity.Epic ? 1.2f : 0.7f,
                OmniRange = 1.5f,
                Position = new Vector3(0f, 0.4f, 0f),
            });
        }
        return pickup;
    }

    public static ItemPickup CreateGold(int amount, Vector2I cell)
    {
        var pickup = new ItemPickup { Gold = amount, Position = Actor.GridToWorld(cell) };
        pickup.CreateModel();
        pickup.BuildCoins();
        return pickup;
    }

    /// <summary>Световой столб над ценной вещью: её видно издалека.</summary>
    private void AddBeam(Color color)
    {
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(color, 0.5f));
        ramp.SetColor(1, new Color(color, 0f));
        var fade = new GradientTexture2D { Gradient = ramp, Width = 8, Height = 64, FillFrom = new Vector2(0f, 1f), FillTo = Vector2.Zero };
        AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh
            {
                Size = new Vector2(0.45f, 2.4f),
                Material = new StandardMaterial3D
                {
                    AlbedoTexture = fade,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
                },
            },
            Position = new Vector3(0f, 1.2f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    private void CreateModel()    {
        _model = new Node3D { Position = new Vector3(0f, FloatHeight, 0f) };
        AddChild(_model);
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        Rotation = new Vector3(0f, _time * 0.8f, 0f);
        _model.Position = new Vector3(0f, FloatHeight + Mathf.Sin(_time * 2f) * 0.05f, 0f);
    }
}
