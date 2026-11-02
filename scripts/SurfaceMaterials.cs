using Godot;

namespace Roguelike;

/// <summary>
/// Материалы пола и стен с «каменными» текстурами, сгенерированными из шума — без файлов картинок.
///
/// FastNoiseLite выдаёт плавный случайный узор, NoiseTexture2D превращает его в картинку:
/// - текстура цвета чуть затемняет камень пятнами (ColorRamp сжимает шум в диапазон светло-серого);
/// - карта нормалей (AsNormalMap) из того же шума заставляет освещение «видеть» рельеф.
///
/// Triplanar — текстура накладывается по мировым координатам, а не по развёртке куба,
/// поэтому узор непрерывно идёт через соседние клетки и не повторяется на каждом блоке.
/// Цвет клетки (туман войны, шахматка) по-прежнему задаёт MultiMesh — он умножается на текстуру.
/// </summary>
public static class SurfaceMaterials
{
    private static StandardMaterial3D _floor;
    private static StandardMaterial3D _wall;

    /// <summary>Пол: мягкие пятна и мелкая неровность.</summary>
    public static StandardMaterial3D Floor => _floor ??= Create(
        new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.04f, FractalOctaves = 3 },
        darkest: 0.74f, bump: 2.5f, scale: 0.4f);

    /// <summary>
    /// Стены: ячеистый шум (Cellular) с режимом Distance2Sub рисует границы между ячейками —
    /// получается похоже на кладку из неровных камней с трещинами-швами.
    /// </summary>
    public static StandardMaterial3D Wall => _wall ??= Create(
        new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular,
            Frequency = 0.045f,
            CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance2Sub,
            FractalOctaves = 1,
        },
        darkest: 0.55f, bump: 9f, scale: 0.45f);

    private static StandardMaterial3D Create(FastNoiseLite noise, float darkest, float bump, float scale)
    {
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(darkest, darkest, darkest));
        ramp.SetColor(1, Colors.White);

        var albedo = new NoiseTexture2D { Noise = noise, Width = 256, Height = 256, Seamless = true, ColorRamp = ramp };
        var normal = new NoiseTexture2D { Noise = noise, Width = 256, Height = 256, Seamless = true, AsNormalMap = true, BumpStrength = bump };

        return new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            VertexColorUseAsAlbedo = true, // цвет клетки из MultiMesh
            AlbedoTexture = albedo,
            NormalEnabled = true,
            NormalTexture = normal,
            Roughness = 0.88f,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(scale, scale, scale),
        };
    }
}
