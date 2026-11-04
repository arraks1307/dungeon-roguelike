using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using static Roguelike.Fx;
using static Roguelike.Shapes;

namespace Roguelike;

/// <summary>Точка лагеря, с которой можно поговорить: постройка, житель, костёр или вход в подземелье.</summary>
public record CampStation(string Id, string Name, string Text, string Resident = null, params string[] Upgrades);

/// <summary>
/// Лагерь у входа в подземелье: отдельная поляна, по которой герой ходит по клеткам, как на этаже.
/// Постройки занимают клетки; шаг в такую клетку открывает разговор с ней.
/// Пока житель не спасён, на его месте стоит пустая заброшенная палатка.
/// Сцена только показывает лагерь и знает, куда можно идти; вся логика разговоров в GameManager.Camp.
/// </summary>
public partial class CampScene : Node3D
{
    public const int Width = 21;
    public const int Height = 16;

    private const string GroundShader = @"
shader_type spatial;
render_mode diffuse_burley;

uniform sampler2D detail_tex : repeat_enable, filter_linear_mipmap;
uniform sampler2D normal_tex : hint_normal, repeat_enable, filter_linear_mipmap;
varying vec3 world_pos;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x),
               mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    vec2 p = world_pos.xz;
    float large = noise(p * 0.5) * 0.6 + noise(p * 1.7) * 0.4;
    float blades = noise(p * 11.0);

    // Вытоптанная земля: круг у костра и дорога к пещере
    float camp = 1.0 - smoothstep(3.2, 5.2, distance(p, vec2(10.0, 8.0)) + (large - 0.5) * 2.0);
    float road = 1.0 - smoothstep(0.7, 1.5, abs(p.x - 10.0) + (large - 0.5) * 1.4);
    road *= step(p.y, 8.5);
    float dirt = clamp(max(camp, road), 0.0, 1.0);

    float detail = texture(detail_tex, p * 0.18).r;
    float pebbles = smoothstep(0.62, 0.7, texture(detail_tex, p * 0.9).g) * dirt;
    vec3 grass = mix(vec3(0.035, 0.09, 0.04), vec3(0.09, 0.19, 0.07), large) * (0.6 + detail * 0.9);
    vec3 earth = mix(vec3(0.15, 0.1, 0.065), vec3(0.3, 0.22, 0.14), large) * (0.65 + detail * 0.8);
    earth = mix(earth, vec3(0.34, 0.33, 0.32), pebbles);
    ALBEDO = mix(grass, earth, dirt);
    ROUGHNESS = mix(0.95, 0.72, pebbles);
    NORMAL_MAP = texture(normal_tex, p * 0.35).rgb;
    NORMAL_MAP_DEPTH = mix(0.7, 1.5, dirt);
}
";

    private static readonly Color Bark = new(0.25f, 0.16f, 0.1f);
    private static readonly Color Plank = new(0.45f, 0.3f, 0.17f);
    private static readonly Color Canvas = new(0.62f, 0.55f, 0.42f);
    private static readonly Color Rock = new(0.34f, 0.34f, 0.37f);
    private static readonly Color Iron = new(0.4f, 0.4f, 0.45f);
    private static readonly Color Gold = new(1f, 0.8f, 0.3f);
    private static readonly Color Skin = new(0.92f, 0.76f, 0.62f);
    private static readonly Color Pine = new(0.07f, 0.2f, 0.12f);

    public static readonly CampStation Cave = new("cave", "Вход в подземелье", "");
    public static readonly CampStation Fire = new("fire", "Костёр", "Огонь не гаснет с тех пор, как ушёл Орден. Здесь можно посидеть и перевести дух.");
    public static readonly CampStation Board = new("board", "Доска памяти", "");

    public static readonly CampStation[] Buildings =
    {
        new("training", "Площадка", "Соломенные чучела ждут, когда ты снова их побьёшь.", null, Profile.Vitality, Profile.Power, Profile.Wisdom),
        new("storage", "Склад", "Здесь хранится всё, что удалось вынести из подземелья.", null, Profile.Wealth, Profile.Supplies),
        new("forge", "Кузница", "", "smith", Profile.Forge),
        new("garden", "Сад трав", "", "herbalist", Profile.Garden),
        new("maptable", "Стол картографа", "", "cartographer", Profile.MapTable),
        new("stall", "Лавка Саны", "", "trader", Profile.Stall),
        new("archive", "Архив", "", "chronicler", Profile.Archive),
    };

    /// <summary>Левая верхняя клетка каждой постройки; все постройки занимают квадрат 2×2.</summary>
    private static readonly Dictionary<string, Vector2I> Spots = new()
    {
        ["forge"] = new(3, 3), ["garden"] = new(16, 3),
        ["training"] = new(2, 7), ["archive"] = new(17, 7),
        ["stall"] = new(4, 12), ["maptable"] = new(15, 12),
        ["storage"] = new(7, 12), ["board"] = new(12, 12),
    };

    private static readonly Vector2I FireCell = new(10, 8);

    public static Vector2I StartCell => new(10, 11);

    private readonly Dictionary<Vector2I, CampStation> _stationCells = new();
    private readonly HashSet<Vector2I> _blocked = new();
    private Node3D _props;
    private OmniLight3D _fireLight;
    private readonly List<Label3D> _labels = new();

    /// <summary>Кто ходит по лагерю: подписи проявляются рядом с ним.</summary>
    public Node3D Focus { get; set; }

    private float _time;

    public override void _Ready()
    {
        var groundMaterial = new ShaderMaterial { Shader = new Shader { Code = GroundShader } };
        groundMaterial.SetShaderParameter("detail_tex", Detail);
        groundMaterial.SetShaderParameter("normal_tex", Bump);
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(70f, 60f), Material = groundMaterial },
            Position = new Vector3(Width / 2f, 0f, Height / 2f),
        });
        // Луна: холодный контрастный свет, в тумане от него видны лучи между деревьями
        AddChild(new DirectionalLight3D
        {
            LightColor = new Color(0.5f, 0.62f, 1f), LightEnergy = 0.55f, RotationDegrees = new Vector3(-48f, 38f, 0f),
            ShadowEnabled = Settings.HighQuality, DirectionalShadowMaxDistance = 45f, ShadowBlur = 1.6f, LightVolumetricFogEnergy = 2.2f,
        });
        BuildGrass();
        BuildForest();
        BuildCliff();
        BuildFire();
        BuildLanterns();
        BuildCampDetails();
        BuildFireflies();
        Rebuild();
    }

    public bool IsWalkable(Vector2I cell) =>
        cell.X > 0 && cell.Y > 1 && cell.X < Width - 1 && cell.Y < Height - 1 && !_blocked.Contains(cell);

    public CampStation StationAt(Vector2I cell) => _stationCells.GetValueOrDefault(cell);

    /// <summary>Постройка рядом с клеткой: для подсказки внизу экрана.</summary>
    public CampStation StationNear(Vector2I cell) =>
        DungeonMap.Directions.Select(direction => StationAt(cell + direction)).FirstOrDefault(station => station != null);

    /// <summary>Заново расставить постройки: после спасения жителя или покупки улучшения лагерь меняется.</summary>
    public void Rebuild()
    {
        _props?.QueueFree();
        _props = new Node3D();
        AddChild(_props);
        _stationCells.Clear();
        _blocked.Clear();

        Occupy(Cave, new Vector2I(9, 2), new Vector2I(3, 1));
        Occupy(Fire, FireCell, Vector2I.One);
        Occupy(Board, Spots["board"], new Vector2I(2, 2));
        BuildBoard(Spots["board"]);
        foreach (var station in Buildings)
        {
            Occupy(station, Spots[station.Id], new Vector2I(2, 2));
            BuildStation(station, Spots[station.Id]);
        }
    }

    private void Occupy(CampStation station, Vector2I corner, Vector2I size)
    {
        for (int x = 0; x < size.X; x++)
            for (int y = 0; y < size.Y; y++)
            {
                _stationCells[corner + new Vector2I(x, y)] = station;
                _blocked.Add(corner + new Vector2I(x, y));
            }
    }

    /// <summary>Деталь лагеря. Всё без свечения получает рельефный материал вместо гладкого пластика.</summary>
    private void Add(Node3D parent, PrimitiveMesh mesh, Color color, float x, float y, float z, Vector3? turn = null, float glow = 0f, float metal = 0f, bool shadow = true)
    {
        var piece = Piece(mesh, color, new Vector3(x, y, z), turn, glow, metal, shadow);
        if (glow <= 0f)
            mesh.Material = Surface(color, metal);
        parent.AddChild(piece);
    }

    /// <summary>Скала на севере с тёмной пастью входа в подземелье.</summary>
    private void BuildCliff()
    {
        var rng = new Random(77);
        for (float x = -2f; x < Width + 2f; x += 1.7f)
        {
            if (x > 8.2f && x < 11.3f)
                continue;
            var box = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(2.1f, 3f + (float)rng.NextDouble() * 1.6f, 2.6f), Material = Surface(Rock.Darkened(0.35f), 0f) },
                Position = new Vector3(x, 1.5f, -0.4f - (float)rng.NextDouble() * 0.5f),
                RotationDegrees = new Vector3(0f, (float)rng.NextDouble() * 12f - 6f, 0f),
            };
            AddChild(box);
        }
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(3.6f, 1.5f, 2.6f), Material = Surface(Rock.Darkened(0.35f), 0f) },
            Position = new Vector3(10f, 3.3f, -0.4f),
        });

        Add(this, Cube(3f, 2.6f, 2.8f), new Color(0.01f, 0.01f, 0.015f), 10.05f, 1.3f, -0.1f, shadow: false);
        foreach (float side in new[] { -1f, 1f })
        {
            Add(this, Cube(0.3f, 2.8f, 0.3f), Plank.Darkened(0.3f), 10f + side * 1.5f, 1.4f, 1.35f);
            Add(this, Ball(0.1f), new Color(1f, 0.6f, 0.2f), 10f + side * 1.5f, 2.9f, 1.35f, glow: 5f, shadow: false);
            AddChild(new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.25f), LightEnergy = 1.3f, OmniRange = 4.5f, Position = new Vector3(10f + side * 1.5f, 2.6f, 1.7f) });
        }
        Add(this, Cube(3.4f, 0.32f, 0.3f), Plank.Darkened(0.3f), 10f, 2.72f, 1.35f);
        AddLabel(this, Cave.Name, new Vector3(10f, 3.5f, 1.5f), new Color(1f, 0.75f, 0.45f));
    }

    private void BuildFire()
    {
        var position = Actor.GridToWorld(FireCell);
        var fire = new Node3D { Position = position };
        AddChild(fire);
        for (int i = 0; i < 9; i++)
        {
            float angle = i * Mathf.Tau / 9f;
            Add(fire, Ball(0.13f, 0.75f), Rock, Mathf.Sin(angle) * 0.46f, 0.07f, Mathf.Cos(angle) * 0.46f);
        }
        for (int i = 0; i < 4; i++)
            Add(fire, Rod(0.05f, 0.7f), Bark, 0f, 0.14f, 0f, new Vector3(68f, i * 45f, 0f));

        BuildFlames(fire);
        _fireLight = new OmniLight3D { LightColor = new Color(1f, 0.55f, 0.22f), LightEnergy = 4.5f, OmniRange = 11f, Position = new Vector3(0f, 1.1f, 0f), ShadowEnabled = Settings.HighQuality, ShadowBlur = 2f, LightVolumetricFogEnergy = 2f };
        fire.AddChild(_fireLight);

        // Брёвна-сиденья вокруг костра
        foreach (var (x, z, turn) in new[] { (-1.6f, 0.2f, 12f), (1.6f, 0.3f, -14f), (0.2f, -1.6f, 96f) })
            Add(fire, Rod(0.17f, 1.1f), Bark.Lightened(0.08f), x, 0.17f, z, new Vector3(0f, turn, 90f));
    }

    private void AddLabel(Node3D parent, string text, Vector3 position, Color color)
    {
        var label = new Label3D
        {
            Text = text, FontSize = 30, PixelSize = 0.0075f, Modulate = color, OutlineSize = 10, OutlineModulate = Colors.Black,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, Position = position,
        };
        parent.AddChild(label);
        _labels.Add(label);
    }
    private void BuildBoard(Vector2I corner)
    {
        var node = Place(corner);
        foreach (float x in new[] { -0.75f, 0.75f })
            Add(node, Cube(0.14f, 1.7f, 0.14f), Bark, x, 0.85f, 0f);
        Add(node, Cube(1.8f, 1f, 0.08f), Plank, 0f, 1.2f, 0f);
        Add(node, Cube(2f, 0.1f, 0.3f), Bark, 0f, 1.75f, 0f);
        var papers = new[] { (-0.5f, 1.35f), (0f, 1.05f), (0.5f, 1.3f), (-0.15f, 1.42f) };
        foreach (var (x, y) in papers)
            Add(node, Cube(0.3f, 0.36f, 0.01f), Canvas.Lightened(0.4f), x, y, 0.05f, shadow: false);
        AddLabel(node, Board.Name, new Vector3(0f, 2.3f, 0f), Gold);
    }

    private void BuildStation(CampStation station, Vector2I corner)
    {
        var node = Place(corner);
        var resident = station.Resident == null ? null : Array.Find(Story.Residents, item => item.Id == station.Resident);
        bool alive = resident == null || Profile.Current.Rescued.Contains(resident.Id);
        int level = station.Upgrades.Sum(Profile.Level);
        AddLabel(node, alive ? station.Name : $"{station.Name} (пусто)", new Vector3(0f, 2.4f, 0f), alive ? Gold : new Color(0.7f, 0.7f, 0.7f));

        if (!alive)
        {
            BuildRuin(node);
            return;
        }
        switch (station.Id)
        {
            case "training": BuildTraining(node, level); break;
            case "storage": BuildStorage(node, level); break;
            case "forge": BuildForge(node, level); break;
            case "garden": BuildGarden(node, level); break;
            case "maptable": BuildMapTable(node, level); break;
            case "stall": BuildStall(node, level); break;
            case "archive": BuildArchive(node, level); break;
        }
        if (resident != null)
            AddFigure(node, station.Id, new Vector3(-0.72f, 0f, 0.72f));
    }

    private Node3D Place(Vector2I corner)
    {
        var node = new Node3D { Position = Actor.GridToWorld(corner) + new Vector3(0.5f, 0f, 0.5f) };
        _props.AddChild(node);
        return node;
    }

    /// <summary>Заброшенное место: упавшая палатка и остывшее кострище.</summary>
    private void BuildRuin(Node3D node)
    {
        Add(node, Cube(1.5f, 0.05f, 1.2f), Canvas.Darkened(0.45f), 0f, 0.38f, 0f, new Vector3(0f, 18f, 24f));
        Add(node, Rod(0.04f, 1.2f), Bark, -0.5f, 0.45f, 0.1f, new Vector3(0f, 0f, 55f));
        Add(node, Ball(0.18f, 0.5f), Rock.Darkened(0.2f), 0.5f, 0.06f, 0.4f);
    }

    /// <summary>Двускатная палатка: две наклонные плоскости и конёк.</summary>
    private void BuildTent(Node3D node, Color color)
    {
        foreach (float side in new[] { -1f, 1f })
            Add(node, Cube(0.04f, 1.25f, 1.7f), color, side * 0.45f, 0.58f, -0.15f, new Vector3(0f, 0f, side * 36f));
        Add(node, Rod(0.04f, 1.8f), Bark, 0f, 1.08f, -0.15f, new Vector3(90f, 0f, 0f));
    }

    private void BuildTraining(Node3D node, int level)
    {
        foreach (var x in new[] { -0.55f, 0.55f })
        {
            Add(node, Rod(0.05f, 1.35f), Bark, x, 0.68f, 0f);
            Add(node, Rod(0.04f, 0.8f), Bark, x, 1f, 0f, new Vector3(0f, 0f, 90f));
            Add(node, Pill(0.2f, 0.7f), new Color(0.72f, 0.6f, 0.3f), x, 0.8f, 0f);
            Add(node, Ball(0.13f), Canvas, x, 1.3f, 0f);
        }
        for (int i = 0; i < Math.Min(level, 4); i++)
            Add(node, Cube(0.05f, 0.75f, 0.05f), Iron, -0.8f + i * 0.5f, 0.38f, 0.75f, new Vector3(0f, 0f, 8f), metal: 0.5f);
    }

    private void BuildStorage(Node3D node, int level)
    {
        BuildTent(node, Canvas.Darkened(0.2f));
        for (int i = 0; i < 2 + Math.Min(level, 4); i++)
            Add(node, Cube(0.36f, 0.34f, 0.36f), Plank, -0.7f + i % 3 * 0.48f, 0.17f + i / 3 * 0.34f, 0.72f, new Vector3(0f, i * 17f, 0f));
    }

    private void BuildForge(Node3D node, int level)
    {
        foreach (var (x, z) in new[] { (-0.8f, -0.75f), (0.8f, -0.75f), (-0.8f, 0.5f), (0.8f, 0.5f) })
            Add(node, Cube(0.14f, 1.5f, 0.14f), Bark, x, 0.75f, z);
        Add(node, Cube(2.1f, 0.12f, 1.7f), Plank.Darkened(0.2f), 0f, 1.55f, -0.12f, new Vector3(9f, 0f, 0f));
        Add(node, Cube(1f, 0.8f, 0.8f), Rock, 0.25f, 0.4f, -0.45f);
        Add(node, Cube(0.68f, 0.05f, 0.5f), new Color(1f, 0.4f, 0.1f), 0.25f, 0.82f, -0.45f, glow: 3f + level);
        node.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.45f, 0.15f), LightEnergy = 1.2f + level * 0.5f, OmniRange = 3.5f, Position = new Vector3(0.25f, 1.1f, -0.1f) });
        Add(node, Cube(0.32f, 0.16f, 0.2f), Iron, 0.6f, 0.52f, 0.5f, metal: 0.6f);
        Add(node, Rod(0.12f, 0.42f), Bark, 0.6f, 0.21f, 0.5f);
    }

    private void BuildGarden(Node3D node, int level)
    {
        for (int row = 0; row < 3; row++)
        {
            Add(node, Cube(1.8f, 0.14f, 0.34f), Bark.Darkened(0.1f), 0f, 0.07f, -0.7f + row * 0.6f);
            for (int i = 0; i < 5; i++)
            {
                var color = (row + i + level) % 3 == 0 ? new Color(0.3f, 0.95f, 0.9f) : new Color(0.3f, 0.65f, 0.3f);
                Add(node, Ball(0.1f, 1.4f), color, -0.7f + i * 0.36f, 0.24f, -0.7f + row * 0.6f, glow: color.B > 0.5f ? 1.5f : 0f, shadow: false);
            }
        }
    }

    private void BuildMapTable(Node3D node, int level)
    {
        BuildTent(node, new Color(0.3f, 0.42f, 0.55f));
        Add(node, Cube(1.1f, 0.06f, 0.7f), Plank, 0.35f, 0.6f, 0.68f);
        foreach (var (x, z) in new[] { (-0.1f, 0.42f), (0.8f, 0.42f), (-0.1f, 0.95f), (0.8f, 0.95f) })
            Add(node, Cube(0.06f, 0.58f, 0.06f), Bark, x, 0.29f, z);
        for (int i = 0; i <= level; i++)
            Add(node, Cube(0.42f, 0.01f, 0.32f), Canvas.Lightened(0.35f), 0.15f + i * 0.2f, 0.64f, 0.68f, new Vector3(0f, i * 13f, 0f), shadow: false);
    }

    private void BuildStall(Node3D node, int level)
    {
        foreach (var x in new[] { -0.8f, 0.8f })
            foreach (var z in new[] { -0.3f, 0.5f })
                Add(node, Cube(0.1f, 1.5f, 0.1f), Bark, x, 0.75f, z);
        Add(node, Cube(1.95f, 0.08f, 1.2f), new Color(0.7f, 0.2f, 0.18f), 0f, 1.55f, 0.1f, new Vector3(8f, 0f, 0f));
        Add(node, Cube(1.7f, 0.5f, 0.6f), Plank, 0f, 0.25f, 0.05f);
        for (int i = 0; i < 3 + level * 2; i++)
            Add(node, Ball(0.07f), i % 2 == 0 ? Gold : new Color(0.85f, 0.3f, 0.4f), -0.6f + i % 5 * 0.3f, 0.58f + i / 5 * 0.12f, 0.05f, metal: 0.4f, shadow: false);
    }

    private void BuildArchive(Node3D node, int level)
    {
        BuildTent(node, new Color(0.42f, 0.28f, 0.5f));
        for (int i = 0; i < 2 + level * 2; i++)
            Add(node, Cube(0.42f, 0.09f, 0.3f), new[] { Canvas, new Color(0.5f, 0.2f, 0.2f), new Color(0.2f, 0.3f, 0.5f) }[i % 3],
                0.55f, 0.05f + i * 0.1f, 0.75f, new Vector3(0f, i * 23f, 0f));
    }

    /// <summary>Житель: простая фигура в цвете своего ремесла.</summary>
    private void AddFigure(Node3D parent, string station, Vector3 position)
    {
        var robe = station switch
        {
            "forge" => new Color(0.32f, 0.22f, 0.15f),
            "garden" => new Color(0.3f, 0.55f, 0.32f),
            "maptable" => new Color(0.3f, 0.4f, 0.6f),
            "stall" => new Color(0.7f, 0.22f, 0.2f),
            _ => new Color(0.42f, 0.3f, 0.55f),
        };
        var figure = new Node3D { Position = position, RotationDegrees = new Vector3(0f, 30f, 0f) };
        parent.AddChild(figure);
        Add(figure, Taper(0.15f, 0.28f, 0.8f), robe, 0f, 0.4f, 0f);
        Add(figure, Ball(0.15f), Skin, 0f, 0.93f, 0.02f);
        Add(figure, Ball(0.158f, 0.62f), robe.Darkened(0.35f), 0f, 1f, -0.01f);
        foreach (float side in new[] { -1f, 1f })
        {
            Add(figure, Pill(0.05f, 0.42f), robe, side * 0.22f, 0.55f, 0.05f, new Vector3(-15f, 0f, side * 10f));
            Add(figure, Ball(0.022f), new Color(0.1f, 0.08f, 0.08f), side * 0.06f, 0.95f, 0.16f, shadow: false);
        }
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        // Несколько синусов разной частоты: свет дрожит, как живой, и не повторяется заметно
        float flicker = Mathf.Sin(_time * 9f) * 0.35f + Mathf.Sin(_time * 23f) * 0.2f + Mathf.Sin(_time * 3.1f) * 0.25f;
        _fireLight.LightEnergy = 4.5f + flicker;
        _fireLight.Position = new Vector3(Mathf.Sin(_time * 7f) * 0.05f, 1.1f, Mathf.Cos(_time * 5f) * 0.05f);

        // Подписи проявляются, только когда герой рядом: на красивой картинке не висит текст
        if (Focus == null)
            return;
        foreach (var label in _labels)
        {
            if (!IsInstanceValid(label))
                continue;
            float distance = label.GlobalPosition.DistanceTo(Focus.GlobalPosition);
            float alpha = 1f - Mathf.SmoothStep(3.2f, 5f, distance);
            label.Modulate = new Color(label.Modulate, alpha);
            label.OutlineModulate = new Color(0f, 0f, 0f, alpha);
        }
    }
}
