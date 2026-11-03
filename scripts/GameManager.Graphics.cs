using Godot;

namespace Roguelike;

/// <summary>
/// Часть GameManager про «картинку»: окружение (туман, затенение, свечение) и качество графики.
/// </summary>
public partial class GameManager
{
    /// <summary>
    /// Environment — настройки «атмосферы» сцены. Здесь:
    /// - объёмный туман: воздух слегка рассеивает свет, вокруг факелов появляются ореолы;
    /// - SSAO (Screen-Space Ambient Occlusion): затемняет углы и стыки, где в жизни меньше света;
    /// - свечение (glow): яркие точки (пламя, руны) мягко «светятся»;
    /// - небольшая подстройка контраста и насыщенности.
    /// Тяжёлые эффекты включаются только на высоком качестве — см. ApplyGraphicsQuality.
    /// </summary>
    private void SetupEnvironment()
    {
        _environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.015f, 0.015f, 0.025f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.35f, 0.4f, 0.6f),
            AmbientLightEnergy = 0.5f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.05f,

            GlowEnabled = true,
            GlowIntensity = 0.9f,
            GlowBloom = 0.04f,
            GlowHdrThreshold = 0.9f,

            SsaoRadius = 1.2f,
            SsaoIntensity = 2.2f,

            VolumetricFogDensity = 0.018f,
            VolumetricFogAnisotropy = 0.4f, // свет рассеивается больше «вперёд» — ореолы мягче
            VolumetricFogLength = 40f,

            AdjustmentEnabled = true,
            AdjustmentContrast = 1.06f,
            AdjustmentSaturation = 1.1f,
        };
        AddChild(new WorldEnvironment { Environment = _environment });

        Settings.Applied += ApplyGraphicsQuality;
        ApplyGraphicsQuality();
    }

    /// <summary>Включаем или выключаем то, что сильнее всего нагружает видеокарту.</summary>
    private void ApplyGraphicsQuality()
    {
        bool high = Settings.HighQuality;
        _environment.SsaoEnabled = high;
        _environment.VolumetricFogEnabled = high;
        GetViewport().Msaa3D = high ? Viewport.Msaa.Msaa2X : Viewport.Msaa.Disabled;
        foreach (var hero in _heroes)
            hero.SetHighQuality(high);
    }
}
