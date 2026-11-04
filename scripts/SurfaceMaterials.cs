using Godot;

namespace Roguelike;

/// <summary>
/// Материалы пола и стен: свой шейдер рисует кладку прямо из мировых координат, без файлов картинок.
///
/// Шейдер — маленькая программа для видеокарты: она считает цвет каждой точки поверхности.
/// Здесь она делит поверхность на камни, затемняет швы, даёт каждому камню свой оттенок,
/// а по пятнам шума делает камень мокрым: там он гладкий и ловит блики от факелов.
///
/// Рисунок считается в мировых координатах, поэтому он непрерывно идёт через соседние клетки.
/// Цвет клетки (туман войны) приходит из MultiMesh в COLOR, отдельная стена — через параметр tint.
/// </summary>
public static class SurfaceMaterials
{
    private const string StoneShader = @"
shader_type spatial;
render_mode diffuse_burley;

uniform sampler2D detail_tex : source_color, repeat_enable, filter_linear_mipmap;
uniform sampler2D normal_tex : hint_normal, repeat_enable, filter_linear_mipmap;
uniform vec3 tint = vec3(1.0);
uniform float pattern = 1.0;   // 1 — кладка, 0 — грубый камень без швов (пещеры)
uniform float is_wall = 0.0;
uniform float wetness = 0.3;
uniform float bump = 1.2;
uniform vec3 moss_color = vec3(0.1, 0.2, 0.07);
uniform float moss = 0.3;

varying vec3 world_pos;
varying vec3 world_normal;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = normalize(mat3(MODEL_MATRIX) * NORMAL);
}

void fragment() {
    vec3 n = abs(world_normal);
    bool top = n.y > 0.5;
    vec2 uv = top ? world_pos.xz : (n.x > 0.5 ? world_pos.zy : world_pos.xy);

    // Камни неровные: чуть искажаем сетку шумом
    vec2 warped = uv + (texture(detail_tex, uv * 0.27).rg - 0.5) * 0.09;
    vec2 size = (is_wall > 0.5 && !top) ? vec2(0.5, 0.25) : vec2(0.5, 0.5);
    vec2 grid = warped / size;
    grid.x += mod(floor(grid.y), 2.0) * 0.5;   // ряды смещены, как в настоящей кладке
    vec2 id = floor(grid);
    vec2 f = fract(grid);
    float edge = min(min(f.x, 1.0 - f.x) * size.x, min(f.y, 1.0 - f.y) * size.y);
    float seam = mix(1.0, smoothstep(0.008, 0.03, edge), pattern);
    float stone = mix(1.0, 0.8 + 0.34 * hash(id), pattern);

    float detail = texture(detail_tex, uv * 0.45).r;
    vec3 base = COLOR.rgb * tint * detail * stone * mix(0.45, 1.0, seam) * 1.25;

    // Трещины: тонкие тёмные жилы там, где шум проходит через середину
    float vein = abs(texture(detail_tex, uv * 0.19 + 0.13).b - 0.5);
    base *= mix(0.55, 1.0, smoothstep(0.0, 0.012, vein));

    // Мох растёт в швах и внизу стен: там сыро. Цвет умножен на COLOR, поэтому туман войны его тоже прячет
    float moss_mask = smoothstep(0.5, 0.72, texture(detail_tex, uv * 0.11 + 0.71).r + (1.0 - seam) * 0.22);
    if (is_wall > 0.5 && !top) moss_mask *= 1.0 - smoothstep(0.1, 0.65, world_pos.y);
    base = mix(base, moss_color * COLOR.rgb * 3.2 * detail, moss_mask * moss);
    if (is_wall > 0.5) {
        base *= top ? 0.5 : mix(0.5, 1.05, smoothstep(0.0, 0.7, world_pos.y));
    }
    ALBEDO = base;

    // Мокрые пятна: на полу заметнее, на стенах почти нет. В темноте тумана блик не нужен
    float visible_cell = clamp(length(COLOR.rgb * tint) * 2.5, 0.0, 1.0);
    float wet = smoothstep(0.58, 0.8, texture(detail_tex, uv * 0.13 + 0.37).g) * wetness * ((top && is_wall < 0.5) ? 1.0 : 0.3) * seam;
    ROUGHNESS = mix(0.9, 0.08, wet);
    base = mix(base, base * 0.6, wet); // мокрый камень темнее сухого
    ALBEDO = base;
    SPECULAR = (0.32 + wet * 0.55) * visible_cell;

    NORMAL_MAP = texture(normal_tex, uv * 0.45).rgb;
    NORMAL_MAP_DEPTH = bump * mix(1.6, 1.0, seam) * (1.0 - wet * 0.85); // лужа ровная
}
";

    private static ShaderMaterial _floor;
    private static ShaderMaterial _wall;
    private static Shader _shader;

    public static ShaderMaterial Floor => _floor ??= Create(
        new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.04f, FractalOctaves = 4 },
        darkest: 0.66f, bump: 2.2f, isWall: false);

    /// <summary>Стены: ячеистый шум даёт сколы и трещины на камне.</summary>
    public static ShaderMaterial Wall => _wall ??= Create(
        new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular,
            Frequency = 0.045f,
            CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance2Sub,
            FractalOctaves = 2,
        },
        darkest: 0.6f, bump: 7f, isWall: true);

    /// <summary>Отделка темы: в пещерах грубый мокрый камень, в подземелье и крипте кладка.</summary>
    public static void ApplyTheme(DungeonTheme theme)
    {
        float wetness = theme.Caves ? 0.9f : theme == Themes.Crypt ? 0.35f : 0.6f;
        var mossColor = theme == Themes.Crypt ? new Vector3(0.12f, 0.17f, 0.15f) : new Vector3(0.1f, 0.2f, 0.07f);
        float mossAmount = theme.Caves ? 0.75f : theme == Themes.Crypt ? 0.3f : 0.45f;
        foreach (var material in new[] { Floor, Wall })
        {
            material.SetShaderParameter("pattern", theme.Caves ? 0f : 1f);
            material.SetShaderParameter("wetness", wetness);
            material.SetShaderParameter("moss_color", mossColor);
            material.SetShaderParameter("moss", mossAmount);
        }
    }

    private static ShaderMaterial Create(FastNoiseLite noise, float darkest, float bump, bool isWall)
    {
        _shader ??= new Shader { Code = StoneShader };

        var ramp = new Gradient();
        ramp.SetColor(0, new Color(darkest, darkest, darkest));
        ramp.SetColor(1, Colors.White);

        var material = new ShaderMaterial { Shader = _shader };
        material.SetShaderParameter("detail_tex", new NoiseTexture2D { Noise = noise, Width = 512, Height = 512, Seamless = true, ColorRamp = ramp });
        material.SetShaderParameter("normal_tex", new NoiseTexture2D { Noise = noise, Width = 512, Height = 512, Seamless = true, AsNormalMap = true, BumpStrength = bump });
        material.SetShaderParameter("is_wall", isWall ? 1f : 0f);
        return material;
    }
}
