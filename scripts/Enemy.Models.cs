using Godot;
using static Roguelike.Shapes;

namespace Roguelike;

/// <summary>
/// Модели врагов. Лицо смотрит в сторону +Z, ноги стоят на полу (y = 0).
/// Детали цвета вида делаем через Flesh: у них общий материал с телом, поэтому мороз окрашивает
/// всё существо сразу, а не только туловище.
/// </summary>
public partial class Enemy
{
    private static readonly Color Bone = new(0.9f, 0.87f, 0.78f);
    private static readonly Color Iron = new(0.5f, 0.52f, 0.58f);
    private static readonly Color Steel = new(0.72f, 0.75f, 0.82f);
    private static readonly Color Wood = new(0.4f, 0.26f, 0.14f);
    private static readonly Color Leather = new(0.32f, 0.2f, 0.12f);
    private static readonly Color Gold = new(1f, 0.78f, 0.28f);
    private static readonly Color Soot = new(0.1f, 0.09f, 0.1f);

    protected override float BreathAmount => Type?.Name == "Слизень" ? 3.5f : 1f;

    private void Add(Node3D parent, PrimitiveMesh mesh, Color color, float x, float y, float z, float glow = 0f, float metal = 0f, Vector3? rotation = null) =>
        parent.AddChild(Piece(mesh, color, new Vector3(x, y, z), rotation, glow, metal));

    private static Vector3 Turn(float x, float y = 0f, float z = 0f) => new(x, y, z);

    /// <summary>Светящиеся глаза: в темноте их видно раньше самого существа.</summary>
    private void AddEyes(Node3D parent, float spread, float height, float depth, Color color, float size = 0.035f)
    {
        foreach (float side in new[] { -1f, 1f })
            Add(parent, Ball(size), color, side * spread, height, depth, glow: 2.5f);
    }

    private void BuildModel(EnemyType type)
    {
        switch (type.Name)
        {
            case "Слизень": BuildSlime(type); break;
            case "Гоблин": BuildGoblin(type); break;
            case "Лучник": BuildArcher(type); break;
            case "Призрак": BuildGhost(type); break;
            case "Орк": BuildOrc(type); break;
            case "Скелет": BuildSkeleton(); break;
            case "Мимик": BuildMimic(type); break;
            case "Подрывник": BuildBomber(type); break;
            case "Щитоносец": BuildShieldbearer(type); break;
            case "Громила": BuildBrute(type); break;
            case "Шаман": BuildShaman(type); break;
            case "Жнец": BuildReaper(type); break;
            default: BuildGoblin(type); break;
        }
    }

    /// <summary>Полупрозрачное желе: внутри видно ядро и пузырьки, у основания подтёки.</summary>
    private void BuildSlime(EnemyType type)
    {
        CreateBody(Ball(0.32f, 0.72f), type.Color, 0.23f);
        BaseColor = new Color(type.Color, 0.78f);
        BodyMaterial.AlbedoColor = BaseColor;
        BodyMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        BodyMaterial.Roughness = 0.12f;

        foreach (var (x, z, size) in new[] { (0.24f, 0.12f, 0.11f), (-0.25f, 0.06f, 0.1f), (0.08f, -0.25f, 0.12f), (-0.14f, 0.22f, 0.09f) })
            Body.AddChild(Flesh(Ball(size, 0.6f), new Vector3(x, -0.17f, z)));
        Add(Body, Ball(0.1f), type.Color.Darkened(0.5f), 0.03f, -0.03f, -0.02f, glow: 0.5f);
        Add(Body, Ball(0.03f), type.Color.Lightened(0.6f), -0.13f, 0.08f, 0.12f);
        Add(Body, Ball(0.02f), type.Color.Lightened(0.6f), 0.14f, -0.04f, 0.17f);
        AddEyes(Body, 0.1f, 0.08f, 0.25f, new Color(1f, 0.95f, 0.75f), 0.045f);
    }

    /// <summary>Мелкий, ушастый, с большой головой и шипастой дубиной.</summary>
    private void BuildGoblin(EnemyType type)
    {
        CreateBody(Pill(0.17f, 0.44f), type.Color, 0.42f);
        AddLegs(Body, -0.16f, 0.08f, 0.26f, 0.06f, null, Leather);
        var (club, free) = AddArms(Body, 0.12f, 0.2f, 0.28f, 0.05f, null, type.Color);

        Body.AddChild(Flesh(Ball(0.17f), new Vector3(0f, 0.28f, 0.05f)));
        foreach (float side in new[] { -1f, 1f })
            Body.AddChild(Flesh(Cone(0.06f, 0.24f), new Vector3(side * 0.22f, 0.32f, 0f), Turn(0f, 0f, side * -72f)));
        Body.AddChild(Flesh(Cone(0.03f, 0.1f), new Vector3(0f, 0.27f, 0.23f), Turn(90f)));
        Add(Body, Rod(0.18f, 0.2f), Leather, 0f, -0.03f, 0f);
        Add(Body, Cube(0.2f, 0.16f, 0.03f), Leather.Darkened(0.2f), 0f, -0.2f, 0.15f);
        foreach (float side in new[] { -1f, 1f })
            Add(Body, Cone(0.014f, 0.05f), Bone, side * 0.05f, 0.19f, 0.2f);
        AddEyes(Body, 0.07f, 0.33f, 0.2f, new Color(1f, 0.9f, 0.3f));

        Add(club, Taper(0.075f, 0.03f, 0.5f), Wood, 0f, -0.28f, 0.22f, rotation: Turn(90f));
        for (int i = 0; i < 3; i++)
            Add(club, Cone(0.02f, 0.07f), Iron, 0f, -0.2f, 0.28f + i * 0.08f);
        free.AddChild(Piece(Cube(0.06f, 0.06f, 0.06f), Bone, new Vector3(0f, -0.3f, 0f)));
    }

    /// <summary>Лук держит плашмя: так сверху виден весь его изгиб.</summary>
    private void BuildArcher(EnemyType type)
    {
        CreateBody(Pill(0.17f, 0.5f), type.Color, 0.48f);
        AddLegs(Body, -0.2f, 0.08f, 0.28f, 0.06f, type.Color.Darkened(0.5f), Leather);
        var (_, bowArm) = AddArms(Body, 0.15f, 0.2f, 0.3f, 0.048f, null, new Color(0.8f, 0.68f, 0.55f), weaponRest: -0.4f);

        Add(Body, Ball(0.15f), new Color(0.8f, 0.68f, 0.55f), 0f, 0.36f, 0.03f);
        Add(Body, Ball(0.175f), type.Color.Darkened(0.45f), 0f, 0.39f, -0.04f);
        Add(Body, Cone(0.09f, 0.2f), type.Color.Darkened(0.45f), 0f, 0.47f, -0.17f, rotation: Turn(-60f));
        Add(Body, Rod(0.19f, 0.05f), Leather, 0f, -0.12f, 0f);
        Add(Body, Rod(0.055f, 0.4f), Leather, -0.1f, 0.1f, -0.21f, rotation: Turn(14f));
        foreach (float x in new[] { -0.13f, -0.1f, -0.07f })
            Add(Body, Cube(0.02f, 0.1f, 0.02f), new Color(0.9f, 0.9f, 0.85f), x, 0.33f, -0.17f);
        AddEyes(Body, 0.06f, 0.37f, 0.17f, new Color(0.6f, 1f, 0.9f), 0.025f);

        bowArm.AddChild(Arc(Wood, new Vector3(0f, -0.3f, 0.12f), 0.3f, 0.025f, 55f, rotationDegrees: Turn(0f, 0f, 30f)));
        Add(bowArm, Cube(0.5f, 0.006f, 0.006f), Bone, 0f, -0.3f, -0.03f, rotation: Turn(0f, 0f, 30f));
    }

    /// <summary>Парит над полом: вместо ног рваные полы савана, руки тянутся вперёд.</summary>
    private void BuildGhost(EnemyType type)
    {
        CreateBody(Taper(0.17f, 0.27f, 0.6f), type.Color, 0.56f);
        HoverHeight = 0.05f;
        Body.AddChild(Flesh(Ball(0.19f), new Vector3(0f, 0.39f, 0f)));
        foreach (var (x, z) in new[] { (0.17f, 0.1f), (-0.17f, 0.1f), (0.12f, -0.16f), (-0.12f, -0.16f), (0f, 0.2f) })
            Body.AddChild(Flesh(Taper(0.1f, 0.01f, 0.28f), new Vector3(x, -0.4f, z)));
        foreach (float side in new[] { -1f, 1f })
            Body.AddChild(Flesh(Taper(0.03f, 0.075f, 0.42f), new Vector3(side * 0.22f, 0.12f, 0.24f), Turn(70f)));
        MakeTranslucent(0.5f);
        BodyMaterial.EmissionEnabled = true;
        BodyMaterial.Emission = type.Color;
        BodyMaterial.EmissionEnergyMultiplier = 0.6f;

        Add(Body, Ball(0.05f), Soot, 0.075f, 0.42f, 0.15f);
        Add(Body, Ball(0.05f), Soot, -0.075f, 0.42f, 0.15f);
        Add(Body, Ball(0.045f, 1.4f), Soot, 0f, 0.3f, 0.17f);
        AddEyes(Body, 0.075f, 0.42f, 0.19f, new Color(0.55f, 0.9f, 1f), 0.02f);
    }

    /// <summary>Широкие плечи, клыки из нижней челюсти, рогатый шлем и двусторонний топор.</summary>
    private void BuildOrc(EnemyType type)
    {
        CreateBody(Pill(0.26f, 0.62f), type.Color, 0.58f);
        AddLegs(Body, -0.24f, 0.12f, 0.34f, 0.085f, Leather, Soot);
        var (axe, _) = AddArms(Body, 0.2f, 0.32f, 0.34f, 0.08f, null, type.Color);

        Body.AddChild(Flesh(Ball(0.18f), new Vector3(0f, 0.38f, 0.06f)));
        Body.AddChild(Flesh(Cube(0.24f, 0.09f, 0.14f), new Vector3(0f, 0.28f, 0.13f)));
        foreach (float side in new[] { -1f, 1f })
        {
            Add(Body, Cone(0.03f, 0.13f), Bone, side * 0.09f, 0.35f, 0.2f);
            Add(Body, Ball(0.13f, 0.75f), Iron, side * 0.31f, 0.26f, 0f, metal: 0.5f);
            Add(Body, Cone(0.04f, 0.14f), Bone, side * 0.34f, 0.37f, 0f, rotation: Turn(0f, 0f, side * -15f));
            Add(Body, Cone(0.05f, 0.2f), Bone, side * 0.2f, 0.56f, 0.03f, rotation: Turn(0f, 0f, side * -35f));
        }
        Add(Body, Ball(0.185f, 0.65f), Iron, 0f, 0.46f, 0.03f, metal: 0.5f);
        Add(Body, Rod(0.27f, 0.08f), Leather, 0f, -0.16f, 0f);
        Add(Body, Ball(0.05f, 0.7f), Gold, 0f, -0.16f, 0.26f, metal: 0.6f);
        Add(Body, Cube(0.05f, 0.62f, 0.03f), Leather, 0f, 0.06f, 0.25f, rotation: Turn(0f, 0f, 38f));
        AddEyes(Body, 0.075f, 0.4f, 0.21f, new Color(1f, 0.35f, 0.2f));

        Add(axe, Rod(0.022f, 0.75f), Wood, 0f, -0.34f, 0.24f, rotation: Turn(90f));
        foreach (float side in new[] { -1f, 1f })
            Add(axe, Taper(0.02f, 0.17f, 0.17f), Steel, side * 0.12f, -0.34f, 0.5f, metal: 0.6f, rotation: Turn(0f, 0f, side * -90f));
    }

    /// <summary>Тело собрано из костей: позвоночник, рёбра, таз. Сквозь рёбра видно пустоту.</summary>
    private void BuildSkeleton()
    {
        CreateBody(Rod(0.04f, 0.46f), Bone.Darkened(0.12f), 0.55f);
        AddLegs(Body, -0.2f, 0.09f, 0.35f, 0.032f, null, Bone.Darkened(0.2f));
        var (sword, _) = AddArms(Body, 0.15f, 0.2f, 0.34f, 0.028f, null, Bone);

        Add(Body, Ball(0.15f), Bone, 0f, 0.34f, 0.02f);
        Add(Body, Cube(0.15f, 0.06f, 0.1f), Bone.Darkened(0.1f), 0f, 0.22f, 0.07f);
        for (int i = 0; i < 4; i++)
            Add(Body, Ring(0.15f - i * 0.012f, 0.014f), Bone, 0f, 0.14f - i * 0.08f, 0f);
        Add(Body, Cube(0.36f, 0.035f, 0.05f), Bone, 0f, 0.17f, 0f);
        Add(Body, Taper(0.12f, 0.08f, 0.09f), Bone.Darkened(0.1f), 0f, -0.2f, 0f);
        Add(Body, Ball(0.045f), Soot, 0.06f, 0.35f, 0.13f);
        Add(Body, Ball(0.045f), Soot, -0.06f, 0.35f, 0.13f);
        AddEyes(Body, 0.06f, 0.35f, 0.16f, new Color(0.4f, 1f, 0.6f), 0.02f);

        var rust = new Color(0.5f, 0.32f, 0.22f);
        Add(sword, Cube(0.055f, 0.02f, 0.55f), rust, 0f, -0.34f, 0.32f, metal: 0.4f);
        Add(sword, Cube(0.18f, 0.03f, 0.03f), Iron, 0f, -0.34f, 0.04f);
    }

    /// <summary>Сундук с откинутой крышкой: зубы, язык и глаза внутри. Снаружи те же окованные доски.</summary>
    private void BuildMimic(EnemyType type)
    {
        CreateBody(Cube(0.6f, 0.3f, 0.42f), type.Color, 0.15f);
        var lid = new Node3D { Position = new Vector3(0f, 0.15f, -0.21f), RotationDegrees = Turn(-38f) };
        Body.AddChild(lid);
        lid.AddChild(Flesh(Cube(0.62f, 0.12f, 0.44f), new Vector3(0f, 0.06f, 0.22f)));

        for (int i = -2; i <= 2; i++)
        {
            Add(Body, Cone(0.04f, 0.11f), Bone, i * 0.115f, 0.2f, 0.17f);
            Add(lid, Cone(0.035f, 0.1f), Bone, i * 0.115f, -0.05f, 0.37f, rotation: Turn(180f));
        }
        Add(Body, Cube(0.52f, 0.02f, 0.34f), new Color(0.2f, 0.03f, 0.05f), 0f, 0.15f, 0f);
        Add(Body, Pill(0.07f, 0.34f), new Color(0.75f, 0.2f, 0.3f), 0f, 0.17f, 0.19f, rotation: Turn(80f));
        foreach (float x in new[] { -0.2f, 0.2f })
        {
            Add(Body, Cube(0.06f, 0.31f, 0.44f), Iron, x, 0f, 0f, metal: 0.5f);
            Add(lid, Cube(0.06f, 0.13f, 0.45f), Iron, x, 0.06f, 0.22f, metal: 0.5f);
        }
        AddEyes(lid, 0.13f, 0.0f, 0.26f, new Color(1f, 0.35f, 0.2f), 0.05f);
    }

    /// <summary>Ходячая бомба: железный обруч, очки и горящий фитиль на макушке.</summary>
    private void BuildBomber(EnemyType type)
    {
        CreateBody(Ball(0.27f), type.Color, 0.37f);
        AddLegs(Body, -0.21f, 0.11f, 0.16f, 0.05f, Soot, Leather);
        var (bomb, _) = AddArms(Body, 0.03f, 0.27f, 0.2f, 0.04f, Soot, Soot, weaponRest: -1.1f);

        Add(Body, Ring(0.27f, 0.025f), Iron, 0f, 0f, 0f, metal: 0.5f);
        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.Tau / 6f;
            Add(Body, Ball(0.02f), Steel, Mathf.Sin(angle) * 0.28f, 0f, Mathf.Cos(angle) * 0.28f, metal: 0.5f);
        }
        Add(Body, Rod(0.07f, 0.06f), Iron, 0f, 0.27f, 0f, metal: 0.5f);
        Add(Body, Taper(0.012f, 0.02f, 0.2f), new Color(0.3f, 0.25f, 0.2f), 0.02f, 0.38f, 0f, rotation: Turn(0f, 0f, -10f));
        Add(Body, Ball(0.05f), new Color(1f, 0.7f, 0.2f), 0.04f, 0.5f, 0f, glow: 5f);
        foreach (float side in new[] { -1f, 1f })
            Add(Body, Ring(0.065f, 0.016f), Leather, side * 0.095f, 0.09f, 0.235f, rotation: Turn(90f));
        Add(Body, Cube(0.42f, 0.03f, 0.02f), Leather, 0f, 0.09f, 0.22f);
        AddEyes(Body, 0.095f, 0.09f, 0.24f, new Color(1f, 0.95f, 0.5f));

        Add(bomb, Ball(0.08f), Soot, 0f, -0.25f, 0.04f);
    }

    /// <summary>Закован в латы, из забрала светится только щель. Щит создаётся в CreateShield.</summary>
    private void BuildShieldbearer(EnemyType type)
    {
        CreateBody(Pill(0.23f, 0.6f), type.Color, 0.56f);
        AddLegs(Body, -0.23f, 0.11f, 0.33f, 0.075f, Iron, Soot);
        var (spear, _) = AddArms(Body, 0.2f, 0.28f, 0.32f, 0.065f, null, Iron);

        Add(Body, Taper(0.21f, 0.25f, 0.36f), Steel, 0f, 0.03f, 0f, metal: 0.6f);
        Add(Body, Rod(0.255f, 0.05f), Leather, 0f, -0.16f, 0f);
        Add(Body, Ball(0.17f), Steel, 0f, 0.4f, 0.02f, metal: 0.6f);
        Add(Body, Cube(0.04f, 0.09f, 0.3f), new Color(0.75f, 0.15f, 0.12f), 0f, 0.59f, -0.02f);
        foreach (float side in new[] { -1f, 1f })
            Add(Body, Ball(0.12f, 0.75f), Steel, side * 0.28f, 0.25f, 0f, metal: 0.6f);
        Add(Body, Cube(0.22f, 0.025f, 0.02f), new Color(1f, 0.65f, 0.3f), 0f, 0.41f, 0.17f, glow: 2.5f);

        Add(spear, Rod(0.018f, 1.05f), Wood, 0f, -0.32f, 0.3f, rotation: Turn(90f));
        Add(spear, Taper(0f, 0.055f, 0.22f), Steel, 0f, -0.32f, 0.92f, metal: 0.6f, rotation: Turn(90f));
    }

    /// <summary>Гора мышц с крошечной головой: длинные руки и кувалда больше него самого.</summary>
    private void BuildBrute(EnemyType type)
    {
        CreateBody(Pill(0.33f, 0.74f), type.Color, 0.64f);
        AddLegs(Body, -0.32f, 0.15f, 0.32f, 0.11f, Leather, Soot);
        var (hammer, _) = AddArms(Body, 0.25f, 0.4f, 0.46f, 0.105f, null, type.Color.Lightened(0.1f));

        Body.AddChild(Flesh(Ball(0.14f), new Vector3(0f, 0.42f, 0.12f)));
        foreach (float side in new[] { -1f, 1f })
        {
            Add(Body, Ball(0.17f, 0.7f), Iron, side * 0.38f, 0.32f, 0f, metal: 0.5f);
            for (int i = 0; i < 2; i++)
                Add(Body, Cone(0.04f, 0.16f), Bone, side * (0.33f + i * 0.1f), 0.47f, 0f);
        }
        Add(Body, Rod(0.34f, 0.09f), Leather, 0f, -0.2f, 0f);
        Add(Body, Ball(0.07f), Bone, 0f, -0.2f, 0.33f);
        Add(Body, Cube(0.3f, 0.26f, 0.03f), Leather.Darkened(0.25f), 0f, -0.34f, 0.28f);
        AddEyes(Body, 0.06f, 0.44f, 0.24f, new Color(1f, 0.25f, 0.15f), 0.028f);

        Add(hammer, Rod(0.03f, 0.95f), Wood, 0f, -0.46f, 0.3f, rotation: Turn(90f));
        Add(hammer, Cube(0.42f, 0.28f, 0.28f), Iron.Darkened(0.2f), 0f, -0.46f, 0.76f, metal: 0.5f);
        foreach (float side in new[] { -1f, 1f })
            Add(hammer, Cube(0.03f, 0.3f, 0.3f), Steel, side * 0.21f, -0.46f, 0.76f, metal: 0.6f);
    }

    /// <summary>Сгорбленный, в маске-черепе и перьях; самоцвет на посохе светится зелёным.</summary>
    private void BuildShaman(EnemyType type)
    {
        CreateBody(Taper(0.14f, 0.33f, 0.8f), type.Color.Darkened(0.2f), 0.4f);
        var (staff, _) = AddArms(Body, 0.25f, 0.2f, 0.3f, 0.05f, null, new Color(0.45f, 0.6f, 0.4f), weaponRest: -0.3f);

        Add(Body, Ball(0.15f), new Color(0.45f, 0.6f, 0.4f), 0f, 0.46f, 0.09f);
        Add(Body, Ball(0.135f, 1.15f), Bone, 0f, 0.46f, 0.13f);
        var feathers = new[] { new Color(0.9f, 0.3f, 0.2f), new Color(0.3f, 0.7f, 0.9f), new Color(0.95f, 0.8f, 0.3f) };
        for (int i = 0; i < 3; i++)
            Add(Body, Cube(0.05f, 0.24f, 0.015f), feathers[i], (i - 1) * 0.08f, 0.66f, 0.03f, rotation: Turn(-8f, 0f, (1 - i) * 18f));
        Add(Body, Ring(0.16f, 0.012f), Bone, 0f, 0.25f, 0.02f, rotation: Turn(18f));
        for (int i = -2; i <= 2; i++)
            Add(Body, Cone(0.018f, 0.06f), Bone, i * 0.05f, 0.18f, 0.19f, rotation: Turn(180f));
        Add(Body, Ball(0.03f), Soot, 0.05f, 0.48f, 0.26f);
        Add(Body, Ball(0.03f), Soot, -0.05f, 0.48f, 0.26f);
        AddEyes(Body, 0.05f, 0.48f, 0.275f, new Color(0.5f, 1f, 0.7f), 0.016f);

        Add(staff, Rod(0.02f, 1.1f), Wood, 0f, -0.22f, 0.05f, rotation: Turn(10f));
        Add(staff, Ring(0.08f, 0.012f), Bone, 0f, 0.32f, 0.15f, rotation: Turn(90f));
        Add(staff, Gem(0.065f), new Color(0.4f, 1f, 0.6f), 0f, 0.32f, 0.15f, glow: 3.5f);
    }

    /// <summary>Парящая тёмная фигура с косой: появляется после первой победы.</summary>
    private void BuildReaper(EnemyType type)
    {
        CreateBody(Taper(0.17f, 0.3f, 0.95f), type.Color, 0.56f);
        HoverHeight = 0.04f;
        var (scythe, _) = AddArms(Body, 0.3f, 0.23f, 0.36f, 0.05f, null, Bone);
        Add(Body, Ball(0.2f), type.Color.Darkened(0.4f), 0f, 0.56f, -0.03f);
        Add(Body, Ball(0.13f), Bone, 0f, 0.55f, 0.06f);
        Add(Body, Cone(0.1f, 0.26f), type.Color.Darkened(0.4f), 0f, 0.68f, -0.15f, rotation: Turn(-55f));
        Add(Body, Ring(0.2f, 0.012f), new Color(0.5f, 1f, 0.7f), 0f, -0.3f, 0f, glow: 2f);
        AddEyes(Body, 0.05f, 0.56f, 0.17f, new Color(0.5f, 1f, 0.7f), 0.024f);
        Add(scythe, Rod(0.018f, 1.2f), Wood.Darkened(0.3f), 0f, -0.36f, 0.3f, rotation: Turn(90f));
        scythe.AddChild(Arc(Steel, new Vector3(-0.28f, -0.36f, 0.86f), 0.3f, 0.03f, 60f));
    }

    private void BuildOrcKing(EnemyType type)    {
        CreateBody(Pill(0.42f, 1.02f), type.Color, 0.84f);
        AddLegs(Body, -0.42f, 0.19f, 0.42f, 0.125f, Leather, Soot);
        var (axe, _) = AddArms(Body, 0.34f, 0.5f, 0.52f, 0.12f, null, type.Color.Lightened(0.08f));
        Cape = AddCape(Body, new Vector3(0f, 0.42f, -0.36f), 0.8f, 1.05f, new Color(0.55f, 0.08f, 0.1f));

        Body.AddChild(Flesh(Ball(0.26f), new Vector3(0f, 0.62f, 0.1f)));
        Body.AddChild(Flesh(Cube(0.36f, 0.13f, 0.2f), new Vector3(0f, 0.47f, 0.2f)));
        Add(Body, Rod(0.25f, 0.14f), Gold, 0f, 0.84f, 0.08f, glow: 0.4f, metal: 0.7f);
        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.Tau / 6f;
            Add(Body, Cone(0.055f, 0.17f), Gold, Mathf.Sin(angle) * 0.22f, 0.98f, 0.08f + Mathf.Cos(angle) * 0.22f, glow: 0.4f, metal: 0.7f);
        }
        Add(Body, Gem(0.05f), new Color(0.9f, 0.1f, 0.15f), 0f, 0.85f, 0.33f, glow: 2f);
        foreach (float side in new[] { -1f, 1f })
        {
            Add(Body, Cone(0.05f, 0.2f), Bone, side * 0.13f, 0.56f, 0.33f);
            Add(Body, Ball(0.22f, 0.7f), new Color(0.3f, 0.2f, 0.12f), side * 0.48f, 0.46f, 0f);
            Add(Body, Cone(0.06f, 0.22f), Bone, side * 0.52f, 0.66f, 0f, rotation: Turn(0f, 0f, side * -15f));
        }
        Add(Body, Rod(0.43f, 0.12f), Leather, 0f, -0.3f, 0f);
        Add(Body, Rod(0.12f, 0.05f), Gold, 0f, -0.3f, 0.42f, metal: 0.7f, rotation: Turn(90f));
        AddEyes(Body, 0.11f, 0.66f, 0.34f, new Color(1f, 0.4f, 0.15f), 0.045f);

        Add(axe, Rod(0.04f, 1.25f), Wood, 0f, -0.52f, 0.42f, rotation: Turn(90f));
        foreach (float side in new[] { -1f, 1f })
            Add(axe, Taper(0.03f, 0.32f, 0.3f), Steel, side * 0.2f, -0.52f, 0.86f, metal: 0.6f, rotation: Turn(0f, 0f, side * -90f));
    }

    private void BuildNecromancer(EnemyType type)
    {
        var skull = new Color(0.9f, 0.88f, 0.8f);
        var soul = new Color(0.6f, 1f, 0.5f);
        CreateBody(Taper(0.17f, 0.46f, 1.3f), type.Color, 0.65f);
        var (staff, _) = AddArms(Body, 0.48f, 0.27f, 0.42f, 0.06f, null, skull, weaponRest: -0.3f);

        Add(Body, Ball(0.25f), type.Color.Darkened(0.35f), 0f, 0.76f, -0.05f);
        Add(Body, Ball(0.18f), skull, 0f, 0.76f, 0.05f);
        Add(Body, Cube(0.17f, 0.08f, 0.12f), skull.Darkened(0.1f), 0f, 0.62f, 0.11f);
        for (int i = -2; i <= 2; i++)
            Add(Body, Cone(0.05f, 0.34f), type.Color.Darkened(0.4f), i * 0.13f, 0.72f, -0.2f - Mathf.Abs(i) * -0.05f, rotation: Turn(-14f, 0f, i * -14f));
        Add(Body, Ring(0.24f, 0.02f), Gold, 0f, 0.5f, 0f, metal: 0.6f);
        Add(Body, Cube(0.14f, 1.05f, 0.02f), type.Color.Lightened(0.15f), 0f, -0.08f, 0.31f, rotation: Turn(-16f));
        AddEyes(Body, 0.07f, 0.78f, 0.2f, soul, 0.03f);

        Add(staff, Rod(0.025f, 1.75f), new Color(0.25f, 0.17f, 0.1f), 0f, -0.3f, 0.06f, rotation: Turn(8f));
        Add(staff, Ball(0.12f), skull, 0f, 0.6f, 0.13f);
        Add(staff, Gem(0.08f), soul, 0f, 0.83f, 0.14f, glow: 3f);

        Spinner = new Node3D { Position = new Vector3(0f, 0.2f, 0f) };
        Body.AddChild(Spinner);
        for (int i = 0; i < 3; i++)
        {
            float angle = i * Mathf.Tau / 3f;
            Add(Spinner, Ball(0.06f), soul, Mathf.Sin(angle) * 0.72f, 0.25f * Mathf.Sin(angle * 2f), Mathf.Cos(angle) * 0.72f, glow: 3f);
        }
        AddChild(new OmniLight3D { LightColor = soul, LightEnergy = 0.9f, OmniRange = 3.5f, Position = new Vector3(0f, 1.5f, 0f) });
    }

    /// <summary>Рогатый исполин с крыльями: под трещинами в груди просвечивает огонь.</summary>
    private void BuildOverlord(EnemyType type)
    {
        var red = new Color(1f, 0.2f, 0.05f);
        var horn = new Color(0.16f, 0.12f, 0.1f);
        CreateBody(Pill(0.58f, 1.5f), type.Color, 1.15f);
        AddLegs(Body, -0.6f, 0.28f, 0.55f, 0.18f, null, horn);
        var (fist, other) = AddArms(Body, 0.5f, 0.72f, 0.75f, 0.17f, null, horn);

        Body.AddChild(Flesh(Ball(0.36f), new Vector3(0f, 0.86f, 0.14f)));
        foreach (float side in new[] { -1f, 1f })
        {
            Add(Body, Taper(0.13f, 0.17f, 0.34f), horn, side * 0.36f, 1.14f, 0.1f, rotation: Turn(0f, 0f, side * -55f));
            Add(Body, Cone(0.13f, 0.44f), horn, side * 0.56f, 1.42f, 0.1f, rotation: Turn(0f, 0f, side * -8f));
            Add(Body, Ball(0.3f, 0.7f), horn, side * 0.72f, 0.66f, 0f, metal: 0.3f);
            for (int i = 0; i < 3; i++)
                Add(Body, Cone(0.07f, 0.28f), horn, side * (0.58f + i * 0.14f), 0.94f, 0f);

            // Крыло: кость вдоль верхнего края и перепонка
            var wing = new Node3D { Position = new Vector3(side * 0.34f, 0.55f, -0.5f), RotationDegrees = Turn(0f, side * -22f, side * -18f) };
            Body.AddChild(wing);
            wing.AddChild(Piece(Cube(1.3f, 0.09f, 0.09f), horn, new Vector3(side * 0.66f, 0.2f, 0f)));
            for (int i = 0; i < 3; i++)
                wing.AddChild(Piece(Cone(0.26f, 0.9f - i * 0.16f), type.Color.Lightened(0.08f), new Vector3(side * (0.34f + i * 0.42f), -0.23f + i * 0.03f, 0f), new Vector3(0f, 0f, 180f - side * (i - 1) * 8f)));
        }
        Add(Body, Ring(0.3f, 0.05f), red, 0f, 0.1f, 0.5f, glow: 3f, rotation: Turn(90f));
        Add(Body, Gem(0.15f), new Color(1f, 0.6f, 0.15f), 0f, 0.1f, 0.54f, glow: 5f);
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.Tau / 4f + 0.5f;
            Add(Body, Cube(0.32f, 0.03f, 0.03f), red, Mathf.Cos(angle) * 0.4f, 0.1f + Mathf.Sin(angle) * 0.4f, 0.5f, glow: 3f, rotation: Turn(0f, 0f, Mathf.RadToDeg(angle)));
        }
        AddEyes(Body, 0.15f, 0.92f, 0.46f, red, 0.07f);
        foreach (var arm in new[] { fist, other })
            for (int i = -1; i <= 1; i++)
                Add(arm, Cone(0.05f, 0.24f), Bone, i * 0.12f, -0.97f, 0.05f, rotation: Turn(180f));

        AddChild(new OmniLight3D { LightColor = red, LightEnergy = 1.5f, OmniRange = 4f, Position = new Vector3(0f, 1.2f, 0.8f) });
    }

    /// <summary>Ростовой щит: железная оковка, крест и умбон. Висит перед телом, пока поднят.</summary>
    private void CreateShield()
    {
        _shield = Piece(Cube(0.62f, 0.76f, 0.06f), new Color(0.48f, 0.54f, 0.68f), new Vector3(0f, 0.02f, 0.42f), metal: 0.5f);
        _shield.AddChild(Piece(Cube(0.68f, 0.05f, 0.08f), Steel, new Vector3(0f, 0.37f, 0f), metal: 0.6f));
        _shield.AddChild(Piece(Cube(0.68f, 0.05f, 0.08f), Steel, new Vector3(0f, -0.37f, 0f), metal: 0.6f));
        _shield.AddChild(Piece(Cube(0.05f, 0.76f, 0.08f), Steel, new Vector3(-0.32f, 0f, 0f), metal: 0.6f));
        _shield.AddChild(Piece(Cube(0.05f, 0.76f, 0.08f), Steel, new Vector3(0.32f, 0f, 0f), metal: 0.6f));
        _shield.AddChild(Piece(Cube(0.08f, 0.56f, 0.08f), Gold, new Vector3(0f, 0f, 0.01f), metal: 0.6f));
        _shield.AddChild(Piece(Cube(0.42f, 0.08f, 0.08f), Gold, new Vector3(0f, 0.08f, 0.01f), metal: 0.6f));
        _shield.AddChild(Piece(Ball(0.09f, 0.6f), Steel, new Vector3(0f, 0.08f, 0.05f), metal: 0.6f));
        Body.AddChild(_shield); // на теле: поворачивается вместе с ним
        SetShield(true);
    }
}
