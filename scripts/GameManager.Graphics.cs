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
            // ACES — кинематографичная кривая: яркое не выгорает в белое, а тени остаются глубокими
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.9f,
            TonemapWhite = 5f,

            GlowEnabled = true,
            GlowIntensity = 0.5f,
            GlowStrength = 1.1f,
            GlowBloom = 0.06f,
            GlowHdrThreshold = 1f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Screen,

            // Отражения на мокром камне и отражённый свет: включаются на Ультра
            SsrMaxSteps = 48,
            SsrFadeOut = 2f,
            SsrDepthTolerance = 0.4f,
            SsilRadius = 4f,
            SsilIntensity = 1.4f,

            SsaoRadius = 1.2f,
            SsaoIntensity = 2.2f,

            VolumetricFogDensity = 0.026f,
            VolumetricFogAnisotropy = 0.4f, // свет рассеивается больше «вперёд» — ореолы мягче
            VolumetricFogLength = 40f,

            AdjustmentEnabled = true,
            AdjustmentContrast = 1.1f,
            AdjustmentSaturation = 1.15f,
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
        bool ultra = Settings.UltraQuality;
        _environment.SsrEnabled = ultra;
        _environment.SsilEnabled = ultra;
        GetViewport().Msaa3D = ultra ? Viewport.Msaa.Msaa4X : high ? Viewport.Msaa.Msaa2X : Viewport.Msaa.Disabled;
        GetViewport().ScreenSpaceAA = high ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled; // сглаживает тонкие травинки
        foreach (var hero in _heroes)
            hero.SetHighQuality(high);
    }
}
