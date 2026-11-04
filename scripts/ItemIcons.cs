using System;
using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>Рисунок иконки. Auto — выбрать по виду предмета.</summary>
public enum IconShape
{
    Auto, Sword, Dagger, Rapier, Axe, Scythe,
    Vest, Chain, Plate, Cloak, Robe,
    Cap, Helm, Hood,
    Ring, Amulet,
    RoundFlask, BigFlask, ConeFlask, SlimVial,
    TeleportScroll, FireScroll, FreezeScroll, MapScroll,
    Bomb,
}

/// <summary>
/// Иконки предметов, нарисованные кодом. Рисунок считается вдвое крупнее нужного и затем уменьшается:
/// так края получаются гладкими (это называется суперсэмплинг). Свет падает слева сверху:
/// у каждой фигуры плавный переход цвета, а вокруг всего рисунка тёмный контур.
/// Готовые иконки хранятся в словаре, чтобы не рисовать одно и то же заново.
/// </summary>
public static class ItemIcons
{
    private const int Canvas = 96;       // размер холста в точках
    private const float Scale = 2f;      // рисуем в координатах 0–48, на холст они попадают вдвое крупнее

    private static readonly Dictionary<(IconShape, Color, Color, int), Texture2D> Cache = new();

    private static readonly Color Brass = new(0.95f, 0.75f, 0.3f);
    private static readonly Color Wood = new(0.5f, 0.31f, 0.16f);
    private static readonly Color Strap = new(0.36f, 0.22f, 0.13f);
    private static readonly Color Parchment = new(0.93f, 0.87f, 0.7f);
    private static readonly Color Cork = new(0.62f, 0.42f, 0.24f);
    private static readonly Color GlassTint = new(0.8f, 0.9f, 1f, 0.28f);
    private static readonly Color Shine = new(1f, 1f, 1f, 0.75f);

    public static Texture2D Get(Item item, int size = 48)
    {
        var shape = item.Template.Icon == IconShape.Auto ? ShapeFor(item.Kind) : item.Template.Icon;
        return Render(shape, item.Color, item.Template.Accent ?? Colors.White, size);
    }

    /// <summary>Иконка без предмета, например бледный силуэт в пустом слоте.</summary>
    public static Texture2D Get(ItemKind kind, Color color, int size = 48) => Render(ShapeFor(kind), color, Colors.White, size);

    private static IconShape ShapeFor(ItemKind kind) => kind switch
    {
        ItemKind.Weapon => IconShape.Sword,
        ItemKind.Armor => IconShape.Plate,
        ItemKind.Helmet => IconShape.Helm,
        ItemKind.Ring => IconShape.Ring,
        ItemKind.Amulet => IconShape.Amulet,
        ItemKind.HealthPotion => IconShape.RoundFlask,
        ItemKind.StrengthPotion => IconShape.ConeFlask,
        ItemKind.InvisibilityPotion => IconShape.SlimVial,
        ItemKind.TeleportScroll => IconShape.TeleportScroll,
        ItemKind.FireScroll => IconShape.FireScroll,
        ItemKind.FreezeScroll => IconShape.FreezeScroll,
        ItemKind.MapScroll => IconShape.MapScroll,
        _ => IconShape.Bomb,
    };

    private static Texture2D Render(IconShape shape, Color color, Color accent, int size)
    {
        var key = (shape, color, accent, size);
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var painter = new Painter();
        // Полупрозрачный цвет означает силуэт: важна только форма, детали не нужны
        bool silhouette = color.A < 1f;
        Draw(painter, shape, silhouette ? Colors.White : color, accent);
        if (silhouette)
            painter.Flatten(color);
        else
            painter.Outline(new Color(0.05f, 0.04f, 0.06f));

        var image = painter.ToImage();
        image.Resize(size, size, Image.Interpolation.Lanczos);
        var texture = ImageTexture.CreateFromImage(image);
        Cache[key] = texture;
        return texture;
    }

    private static void Draw(Painter p, IconShape shape, Color color, Color accent)
    {
        var dark = color.Darkened(0.4f);
        switch (shape)
        {
            case IconShape.Sword:
                DrawBlade(p, color, 14f, 34f, 43f, 5f, 3.2f);
                p.Stroke(4f, 2f, 7f, 27f, 21f, 41f, Brass);
                p.Stroke(3.6f, 1.8f, 13f, 35f, 6.5f, 41.5f, Strap);
                p.Disk(5.5f, 42.5f, 3.2f, Brass);
                break;

            case IconShape.Dagger:
                DrawBlade(p, color, 17f, 31f, 37f, 11f, 3.8f);
                p.Stroke(3.6f, 1.8f, 11f, 26f, 22f, 37f, Brass);
                p.Stroke(4f, 2f, 16f, 32f, 9f, 39f, Strap);
                p.Disk(8f, 40f, 3f, Brass);
                break;

            case IconShape.Rapier:
                DrawBlade(p, color, 13f, 35f, 45f, 3f, 1.5f);
                p.Disk(14f, 34f, 7.5f, Brass);
                p.Disk(14f, 34f, 4.8f, Brass.Darkened(0.35f));
                p.Stroke(3f, 1.5f, 12f, 36f, 6f, 42f, Strap);
                p.Disk(5f, 43f, 2.6f, Brass);
                break;

            case IconShape.Axe:
                p.Stroke(4f, 2f, 9f, 44f, 34f, 8f, Wood);
                p.Poly(color, (24f, 10f), (31f, 15f), (42f, 27f), (46f, 15f), (42f, 6f), (33f, 3f));
                p.Poly(color.Lightened(0.45f), (42f, 27f), (46f, 15f), (42f, 6f), (39.5f, 8f), (42.5f, 15f), (40f, 23f));
                p.Stroke(3.4f, 1.7f, 26f, 19f, 32f, 9f, Strap);
                break;

            case IconShape.Scythe:
                p.Stroke(3.6f, 1.8f, 12f, 45f, 31f, 7f, Wood);
                p.Poly(color, (32f, 8f), (25f, 3f), (14f, 3f), (6f, 8f), (2f, 19f), (9f, 13f), (17f, 10f), (28f, 11f));
                p.Poly(color.Lightened(0.45f), (6f, 8f), (2f, 19f), (9f, 13f), (17f, 10f), (15f, 7.5f));
                p.Stroke(3.2f, 1.6f, 23f, 25f, 19f, 33f, Strap);
                p.Disk(30f, 8f, 3.4f, Brass);
                break;

            case IconShape.Vest:
                DrawTorso(p, color);
                p.Stroke(2f, 1f, 24f, 11f, 24f, 42f, dark);
                foreach (float y in new[] { 17f, 24f, 31f })
                    p.Stroke(2f, 1f, 21f, y, 27f, y, Strap);
                p.Box(14f, 34f, 20f, 3f, Strap);
                p.Box(22f, 33.5f, 4f, 4f, Brass);
                break;

            case IconShape.Chain:
                DrawTorso(p, color.Darkened(0.2f));
                for (int row = 0; row < 8; row++)
                    for (int column = 0; column < 7; column++)
                    {
                        float x = 15.5f + column * 3f + (row % 2) * 1.5f;
                        float y = 12f + row * 4f;
                        if (x < 33.5f)
                            p.Annulus(x, y, 1.9f, 1f, color.Lightened(0.3f));
                    }
                p.Box(14f, 37f, 20f, 3f, Strap);
                break;

            case IconShape.Plate:
                DrawTorso(p, color);
                p.Poly(color.Lightened(0.4f), (23f, 11f), (25f, 11f), (25f, 37f), (23f, 37f));
                p.Stroke(1.8f, 0.9f, 15f, 25f, 33f, 25f, dark);
                p.Disk(11f, 13f, 6.5f, color.Lightened(0.15f));
                p.Disk(37f, 13f, 6.5f, color.Lightened(0.15f));
                foreach (float x in new[] { 17f, 31f })
                    foreach (float y in new[] { 16f, 33f })
                        p.Disk(x, y, 1.3f, Brass);
                p.Box(14f, 38f, 20f, 3f, Brass.Darkened(0.2f));
                break;

            case IconShape.Cloak:
                p.Poly(color, (24f, 5f), (32f, 10f), (35f, 20f), (42f, 44f), (32f, 41f), (24f, 44f), (16f, 41f), (6f, 44f), (13f, 20f), (16f, 10f));
                p.Poly(color.Darkened(0.55f), (24f, 9f), (29f, 13f), (30f, 21f), (24f, 25f), (18f, 21f), (19f, 13f));
                p.Stroke(2.4f, 1.2f, 22f, 25f, 16f, 42f, color.Lightened(0.25f));
                p.Stroke(2.4f, 1.2f, 26f, 25f, 32f, 42f, color.Lightened(0.25f));
                p.Disk(24f, 26f, 2.6f, accent);
                break;

            case IconShape.Robe:
                p.Poly(color, (18f, 7f), (30f, 7f), (42f, 14f), (40f, 21f), (34f, 19f), (39f, 44f), (9f, 44f), (14f, 19f), (8f, 21f), (6f, 14f));
                p.Poly(color.Lightened(0.45f), (22f, 8f), (26f, 8f), (28f, 44f), (20f, 44f));
                p.Box(13f, 25f, 22f, 3f, Strap);
                p.Disk(24f, 26.5f, 2.5f, Brass);
                break;

            case IconShape.Cap:
                p.Clip((_, y) => y < 28f, () => p.Disk(24f, 28f, 16f, color));
                p.Stroke(1.8f, 0.9f, 24f, 12f, 24f, 28f, dark);
                p.Stroke(1.8f, 0.9f, 15f, 15f, 17f, 28f, dark);
                p.Stroke(1.8f, 0.9f, 33f, 15f, 31f, 28f, dark);
                p.Box(6f, 27f, 36f, 6f, color.Darkened(0.2f));
                p.Box(21f, 28f, 6f, 4f, Brass);
                break;

            case IconShape.Helm:
                p.Clip((_, y) => y < 30f, () => p.Disk(24f, 26f, 16f, color));
                p.Poly(color, (8f, 26f), (18f, 26f), (18f, 41f), (12f, 38f));
                p.Poly(color.Darkened(0.15f), (40f, 26f), (30f, 26f), (30f, 41f), (36f, 38f));
                p.Stroke(4f, 2f, 24f, 26f, 24f, 38f, color.Darkened(0.2f));
                p.Box(8f, 25f, 32f, 3.5f, Brass);
                p.Stroke(4f, 2f, 24f, 4f, 24f, 11f, accent == Colors.White ? new Color(0.8f, 0.2f, 0.15f) : accent);
                p.Arc(24f, 26f, 11f, 200f, 270f, 1.2f, Shine);
                break;

            case IconShape.Hood:
                p.Poly(color, (24f, 3f), (34f, 11f), (40f, 26f), (40f, 42f), (31f, 37f), (24f, 44f), (17f, 37f), (8f, 42f), (8f, 26f), (14f, 11f));
                p.Poly(color.Darkened(0.65f), (24f, 13f), (31f, 19f), (31f, 30f), (24f, 34f), (17f, 30f), (17f, 19f));
                p.Disk(20.5f, 24f, 1.5f, accent);
                p.Disk(27.5f, 24f, 1.5f, accent);
                break;

            case IconShape.Ring:
                p.Annulus(24f, 30f, 13f, 8.5f, color);
                p.Arc(24f, 30f, 11f, 150f, 250f, 1f, Shine);
                p.Poly(Brass.Darkened(0.25f), (17f, 18f), (31f, 18f), (28f, 12f), (20f, 12f));
                DrawGem(p, 24f, 10f, 7.5f, accent);
                break;

            case IconShape.Amulet:
                p.Arc(24f, 11f, 15f, 0f, 180f, 1.2f, Brass);
                p.Stroke(2.4f, 1.2f, 9f, 11f, 24f, 24f, Brass);
                p.Stroke(2.4f, 1.2f, 39f, 11f, 24f, 24f, Brass);
                p.Disk(24f, 32f, 11f, Brass);
                p.Disk(24f, 32f, 8f, Brass.Darkened(0.4f));
                DrawGem(p, 24f, 32f, 7f, color);
                break;

            case IconShape.RoundFlask:
                DrawLiquidFlask(p, color, 29f, 14f);
                break;

            case IconShape.BigFlask:
                DrawLiquidFlask(p, color, 29f, 16.5f);
                p.Stroke(3f, 1.5f, 24f, 24f, 24f, 34f, Colors.White);
                p.Stroke(3f, 1.5f, 19f, 29f, 29f, 29f, Colors.White);
                break;

            case IconShape.ConeFlask:
                p.Poly(color, (15f, 28f), (33f, 28f), (40f, 42f), (37f, 45f), (11f, 45f), (8f, 42f));
                p.Poly(GlassTint, (20f, 9f), (28f, 9f), (28f, 18f), (41f, 42f), (37f, 45f), (11f, 45f), (7f, 42f), (20f, 18f));
                p.Box(18f, 7f, 12f, 3f, Brass);
                p.Box(20f, 2f, 8f, 6f, Cork);
                p.Disk(21f, 37f, 2f, color.Lightened(0.6f));
                p.Disk(29f, 33f, 1.4f, color.Lightened(0.6f));
                p.Stroke(2f, 1f, 22f, 20f, 15f, 33f, Shine);
                break;

            case IconShape.SlimVial:
                p.Box(16f, 18f, 16f, 24f, color);
                p.Disk(24f, 42f, 8f, color);
                p.Box(15f, 11f, 18f, 31f, GlassTint);
                p.Disk(24f, 42f, 9f, GlassTint);
                p.Box(18f, 7f, 12f, 4f, Brass);
                p.Box(20f, 2f, 8f, 6f, Cork);
                p.Stroke(2f, 1f, 18.5f, 14f, 18.5f, 37f, Shine);
                DrawSparkle(p, 39f, 13f, 5f);
                DrawSparkle(p, 8f, 27f, 3.5f);
                break;

            case IconShape.TeleportScroll:
                DrawScroll(p);
                p.Annulus(24f, 24f, 9f, 7f, color);
                p.Annulus(24f, 24f, 5.2f, 3.6f, color.Lightened(0.3f));
                p.Disk(24f, 24f, 1.8f, Colors.White);
                break;

            case IconShape.FireScroll:
                DrawScroll(p);
                p.Poly(color, (24f, 12f), (31f, 22f), (33f, 29f), (28f, 35f), (20f, 35f), (15f, 29f), (17f, 22f), (20f, 25f));
                p.Poly(new Color(1f, 0.85f, 0.3f), (24f, 21f), (28f, 28f), (26f, 33f), (22f, 33f), (20f, 28f));
                break;

            case IconShape.FreezeScroll:
                DrawScroll(p);
                for (int i = 0; i < 3; i++)
                {
                    float angle = i * Mathf.Pi / 3f;
                    float dx = Mathf.Sin(angle) * 11f, dy = Mathf.Cos(angle) * 11f;
                    p.Stroke(2f, 1f, 24f - dx, 24f - dy, 24f + dx, 24f + dy, color.Darkened(0.2f));
                    foreach (float side in new[] { -1f, 1f })
                    {
                        float tipX = 24f + side * dx * 0.65f, tipY = 24f + side * dy * 0.65f;
                        p.Stroke(1.6f, 0.8f, tipX, tipY, tipX + (dy - dx) * 0.28f * side, tipY - (dx + dy) * 0.28f * side, color.Darkened(0.2f));
                    }
                }
                p.Disk(24f, 24f, 2.2f, color);
                break;

            case IconShape.MapScroll:
                DrawScroll(p);
                p.Poly(color.Darkened(0.35f), (14f, 16f), (22f, 13f), (32f, 16f), (34f, 26f), (29f, 33f), (17f, 34f), (13f, 26f));
                p.Poly(Parchment.Darkened(0.05f), (17f, 18f), (22f, 16f), (29f, 18f), (31f, 25f), (27f, 30f), (19f, 31f), (16f, 25f));
                p.Stroke(2f, 1f, 23f, 21f, 27f, 25f, new Color(0.8f, 0.15f, 0.1f));
                p.Stroke(2f, 1f, 27f, 21f, 23f, 25f, new Color(0.8f, 0.15f, 0.1f));
                break;

            case IconShape.Bomb:
                p.Disk(21f, 29f, 15f, new Color(0.3f, 0.3f, 0.36f));
                p.Disk(16f, 23f, 3.8f, new Color(0.62f, 0.64f, 0.7f));
                p.Box(26f, 11f, 9f, 6f, new Color(0.45f, 0.45f, 0.5f));
                p.Stroke(2.2f, 1.1f, 32f, 11f, 39f, 6f, Cork);
                DrawSparkle(p, 41f, 5f, 5.5f, new Color(1f, 0.65f, 0.1f));
                p.Disk(41f, 5f, 1.6f, Colors.White);
                break;
        }
    }

    /// <summary>Клинок по диагонали: светлая и тёмная половины читаются как заточенная грань.</summary>
    private static void DrawBlade(Painter p, Color color, float x0, float y0, float x1, float y1, float halfWidth)
    {
        float length = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        float nx = -(y1 - y0) / length * halfWidth, ny = (x1 - x0) / length * halfWidth;
        float baseX = x1 - (x1 - x0) / length * halfWidth * 2.2f, baseY = y1 - (y1 - y0) / length * halfWidth * 2.2f;
        p.Poly(color.Lightened(0.3f), (x0 - nx, y0 - ny), (baseX - nx, baseY - ny), (x1, y1), (x0, y0));
        p.Poly(color.Darkened(0.2f), (x0 + nx, y0 + ny), (baseX + nx, baseY + ny), (x1, y1), (x0, y0));
    }

    private static void DrawTorso(Painter p, Color color) =>
        p.Poly(color, (15f, 8f), (19f, 6f), (24f, 11f), (29f, 6f), (33f, 8f), (42f, 14f), (38f, 23f), (34f, 21f), (34f, 42f), (14f, 42f), (14f, 21f), (10f, 23f), (6f, 14f));

    /// <summary>Огранённый камень: четыре грани разной яркости и блик.</summary>
    private static void DrawGem(Painter p, float x, float y, float radius, Color color)
    {
        p.Poly(color.Lightened(0.4f), (x, y - radius), (x - radius, y), (x, y));
        p.Poly(color, (x, y - radius), (x + radius, y), (x, y));
        p.Poly(color, (x, y + radius), (x - radius, y), (x, y));
        p.Poly(color.Darkened(0.4f), (x, y + radius), (x + radius, y), (x, y));
        p.Disk(x - radius * 0.3f, y - radius * 0.3f, radius * 0.16f, Colors.White);
    }

    /// <summary>Круглая колба: жидкость заполняет её до уровня, поверх стекло, пробка и пузырьки.</summary>
    private static void DrawLiquidFlask(Painter p, Color color, float centerY, float radius)
    {
        float top = centerY - radius;
        p.Clip((_, y) => y > centerY - radius * 0.35f, () => p.Disk(24f, centerY, radius - 1f, color));
        p.Disk(24f, centerY, radius, GlassTint);
        p.Box(19f, top - 8f, 10f, 10f, GlassTint);
        p.Box(17f, top - 10f, 14f, 3f, Brass);
        p.Box(19f, top - 15f, 10f, 6f, Cork);
        p.Arc(24f, centerY, radius - 3f, 170f, 250f, 1.2f, Shine);
        p.Disk(29f, centerY + radius * 0.3f, 1.6f, color.Lightened(0.6f));
        p.Disk(20f, centerY + radius * 0.5f, 1.2f, color.Lightened(0.6f));
    }

    private static void DrawScroll(Painter p)
    {
        p.Box(10f, 8f, 28f, 32f, Parchment);
        p.Stroke(6f, 3f, 8f, 8f, 40f, 8f, Parchment.Darkened(0.2f));
        p.Stroke(6f, 3f, 8f, 40f, 40f, 40f, Parchment.Darkened(0.2f));
        foreach (float x in new[] { 6f, 42f })
            foreach (float y in new[] { 8f, 40f })
                p.Disk(x, y, 3f, Wood);
    }

    private static void DrawSparkle(Painter p, float x, float y, float size, Color? color = null)
    {
        var tint = color ?? Colors.White;
        p.Poly(tint, (x, y - size), (x + size * 0.25f, y - size * 0.25f), (x + size, y), (x + size * 0.25f, y + size * 0.25f),
            (x, y + size), (x - size * 0.25f, y + size * 0.25f), (x - size, y), (x - size * 0.25f, y - size * 0.25f));
    }

    /// <summary>Холст с простыми фигурами. Координаты логические, от 0 до 48.</summary>
    private sealed class Painter
    {
        private readonly Color[] _pixels = new Color[Canvas * Canvas];
        private Func<float, float, bool> _clip;

        private void Fill(Func<float, float, bool> inside, Color color)
        {
            // Цвет меняется по диагонали: сверху слева светлее, снизу справа темнее
            var light = color.Lightened(0.22f);
            var shade = color.Darkened(0.25f);
            for (int py = 0; py < Canvas; py++)
                for (int px = 0; px < Canvas; px++)
                {
                    float x = (px + 0.5f) / Scale, y = (py + 0.5f) / Scale;
                    if (!inside(x, y) || (_clip != null && !_clip(x, y)))
                        continue;
                    var tinted = light.Lerp(shade, (x + y) / 96f);
                    tinted.A = color.A;
                    ref var pixel = ref _pixels[py * Canvas + px];
                    pixel = pixel.Blend(tinted);
                }
        }

        public void Clip(Func<float, float, bool> clip, Action draw)
        {
            _clip = clip;
            draw();
            _clip = null;
        }

        public void Disk(float cx, float cy, float radius, Color color) =>
            Fill((x, y) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius, color);

        public void Annulus(float cx, float cy, float outer, float inner, Color color) => Fill((x, y) =>
        {
            float distance = (x - cx) * (x - cx) + (y - cy) * (y - cy);
            return distance <= outer * outer && distance >= inner * inner;
        }, color);

        public void Box(float left, float top, float width, float height, Color color) =>
            Fill((x, y) => x >= left && x <= left + width && y >= top && y <= top + height, color);

        /// <summary>Часть окружности. Углы в градусах, отсчёт по часовой стрелке от направления вправо.</summary>
        public void Arc(float cx, float cy, float radius, float from, float to, float halfWidth, Color color) => Fill((x, y) =>
        {
            float distance = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            if (Mathf.Abs(distance - radius) > halfWidth)
                return false;
            float angle = Mathf.RadToDeg(Mathf.Atan2(y - cy, x - cx));
            if (angle < 0f)
                angle += 360f;
            return angle >= from && angle <= to;
        }, color);

        /// <summary>Толстая линия со скруглёнными концами. width оставлен для наглядности вызова.</summary>
        public void Stroke(float width, float halfWidth, float x0, float y0, float x1, float y1, Color color)
        {
            float dx = x1 - x0, dy = y1 - y0;
            float lengthSquared = Math.Max(dx * dx + dy * dy, 0.001f);
            Fill((x, y) =>
            {
                float t = Mathf.Clamp(((x - x0) * dx + (y - y0) * dy) / lengthSquared, 0f, 1f);
                float nearX = x0 + dx * t, nearY = y0 + dy * t;
                return (x - nearX) * (x - nearX) + (y - nearY) * (y - nearY) <= halfWidth * halfWidth;
            }, color);
        }

        /// <summary>Многоугольник: точка внутри, если луч из неё пересекает стороны нечётное число раз.</summary>
        public void Poly(Color color, params (float X, float Y)[] points) => Fill((x, y) =>
        {
            bool inside = false;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
            {
                var (ax, ay) = points[i];
                var (bx, by) = points[j];
                if ((ay > y) != (by > y) && x < (bx - ax) * (y - ay) / (by - ay) + ax)
                    inside = !inside;
            }
            return inside;
        }, color);

        /// <summary>Тёмная кайма вокруг рисунка: иконка не сливается с фоном ячейки.</summary>
        public void Outline(Color color)
        {
            var source = (Color[])_pixels.Clone();
            const int radius = 3;
            for (int py = 0; py < Canvas; py++)
                for (int px = 0; px < Canvas; px++)
                {
                    if (source[py * Canvas + px].A > 0.1f)
                        continue;
                    bool near = false;
                    for (int dy = -radius; dy <= radius && !near; dy++)
                        for (int dx = -radius; dx <= radius && !near; dx++)
                        {
                            int nx = px + dx, ny = py + dy;
                            near = nx >= 0 && ny >= 0 && nx < Canvas && ny < Canvas && dx * dx + dy * dy <= radius * radius
                                && source[ny * Canvas + nx].A > 0.1f;
                        }
                    if (near)
                        _pixels[py * Canvas + px] = _pixels[py * Canvas + px].Blend(color);
                }
        }

        /// <summary>Оставить только форму: все точки одного цвета, прозрачность задаёт цвет.</summary>
        public void Flatten(Color color)
        {
            for (int i = 0; i < _pixels.Length; i++)
                _pixels[i] = _pixels[i].A > 0f ? color : new Color(0f, 0f, 0f, 0f);
        }

        public Image ToImage()
        {
            var image = Image.CreateEmpty(Canvas, Canvas, false, Image.Format.Rgba8);
            for (int py = 0; py < Canvas; py++)
                for (int px = 0; px < Canvas; px++)
                    image.SetPixel(px, py, _pixels[py * Canvas + px]);
            return image;
        }
    }
}
