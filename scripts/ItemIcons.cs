using System;
using System.Collections.Generic;
using Godot;

namespace Roguelike;

/// <summary>
/// Пиксельные иконки предметов, нарисованные кодом по точкам (24×24).
/// Готовые иконки кэшируются в словаре, чтобы не рисовать одно и то же заново.
/// </summary>
public static class ItemIcons
{
    private const int Size = 24;

    private static readonly Dictionary<(ItemKind, Color, int), Texture2D> Cache = new();
    private static readonly Color Parchment = new(0.92f, 0.86f, 0.7f);
    private static readonly Color Clear = new(0f, 0f, 0f, 0f);

    public static Texture2D Get(Item item, int size = Size) => Get(item.Kind, item.Color, size);

    /// <summary>
    /// Иконка нужного размера. Рисуем всегда 24×24, а затем увеличиваем методом «ближайшего соседа»
    /// (Nearest): каждый пиксель становится квадратом 2×2, 3×3 и т. д. — края остаются чёткими, без размытия.
    /// </summary>
    public static Texture2D Get(ItemKind kind, Color color, int size = Size)
    {
        if (Cache.TryGetValue((kind, color, size), out var cached))
            return cached;

        var image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        Draw(image, kind, color);
        if (size != Size)
            image.Resize(size, size, Image.Interpolation.Nearest);
        var texture = ImageTexture.CreateFromImage(image);
        Cache[(kind, color, size)] = texture;
        return texture;
    }

    private static void Draw(Image image, ItemKind kind, Color color)
    {
        var dark = color.Darkened(0.5f);
        switch (kind)
        {
            case ItemKind.Weapon:
                Line(image, 6, 18, 19, 5, color, 2);   // клинок
                Line(image, 4, 14, 10, 20, dark, 2);   // гарда
                Line(image, 3, 21, 6, 18, dark, 2);    // рукоять
                break;
            case ItemKind.Armor:
                Rect(image, 6, 6, 12, 14, color);      // туловище
                Rect(image, 2, 6, 4, 6, color);        // плечи
                Rect(image, 18, 6, 4, 6, color);
                Rect(image, 10, 6, 4, 3, Clear);       // вырез для шеи
                Rect(image, 6, 16, 12, 1, dark);       // пояс
                break;
            case ItemKind.Helmet:
                Circle(image, 12, 14, 9, color);
                Rect(image, 0, 15, Size, 9, Clear);    // срезаем нижнюю половину круга
                Rect(image, 3, 14, 18, 3, dark);       // ободок
                Rect(image, 11, 6, 2, 8, dark);        // гребень
                break;
            case ItemKind.Ring:
                Circle(image, 12, 14, 7, color);
                Circle(image, 12, 14, 4, Clear);       // дырка кольца
                Circle(image, 12, 6, 3, new Color(1f, 1f, 1f, color.A)); // камень (прозрачность — как у иконки)
                break;
            case ItemKind.Amulet:
                Line(image, 4, 2, 12, 11, dark, 1);    // цепочка
                Line(image, 20, 2, 12, 11, dark, 1);
                Circle(image, 12, 15, 6, color);
                Circle(image, 12, 15, 2, new Color(1f, 1f, 1f, color.A));
                break;
            case ItemKind.HealthPotion:
            case ItemKind.StrengthPotion:
            case ItemKind.InvisibilityPotion:
                Circle(image, 12, 15, 7, color);       // колба
                Rect(image, 10, 4, 4, 6, dark);        // горлышко
                Rect(image, 9, 2, 6, 3, new Color(0.55f, 0.35f, 0.2f)); // пробка
                Circle(image, 9, 13, 2, color.Lightened(0.5f)); // блик
                break;
            case ItemKind.Bomb:
                Circle(image, 11, 14, 8, new Color(0.22f, 0.22f, 0.27f));
                Circle(image, 8, 11, 2, new Color(0.5f, 0.5f, 0.55f)); // блик
                Line(image, 15, 7, 19, 3, new Color(0.6f, 0.45f, 0.25f), 1); // фитиль
                Circle(image, 20, 3, 2, new Color(1f, 0.6f, 0.1f));    // искра
                break;
            default: // свитки
                Rect(image, 5, 5, 14, 14, Parchment);
                Rect(image, 3, 4, 3, 16, Parchment.Darkened(0.3f)); // свёрнутые края
                Rect(image, 18, 4, 3, 16, Parchment.Darkened(0.3f));
                Rect(image, 8, 8, 8, 2, color);       // «письмена» цвета свитка
                Rect(image, 8, 12, 8, 2, color);
                Rect(image, 8, 16, 5, 1, color);
                break;
        }
    }

    // ---------- Примитивы рисования по пикселям ----------

    private static void Put(Image image, int x, int y, Color color)
    {
        if (x >= 0 && y >= 0 && x < Size && y < Size)
            image.SetPixel(x, y, color);
    }

    private static void Rect(Image image, int x, int y, int width, int height, Color color)
    {
        for (int dx = 0; dx < width; dx++)
            for (int dy = 0; dy < height; dy++)
                Put(image, x + dx, y + dy, color);
    }

    private static void Circle(Image image, int cx, int cy, int radius, Color color)
    {
        for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
                if (dx * dx + dy * dy <= radius * radius)
                    Put(image, cx + dx, cy + dy, color);
    }

    /// <summary>Толстая линия: идём по отрезку маленькими шагами и ставим квадратики.</summary>
    private static void Line(Image image, int x0, int y0, int x1, int y1, Color color, int thickness)
    {
        int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        for (int i = 0; i <= steps; i++)
        {
            float t = steps == 0 ? 0f : (float)i / steps;
            int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
            int y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
            Rect(image, x, y, thickness, thickness, color);
        }
    }
}
