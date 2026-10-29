using System.Collections.Generic;

namespace Roguelike;

/// <summary>
/// Сумка игрока и надетая экипировка. Надетые предметы в сумке не лежат.
/// </summary>
public class Inventory
{
    public const int Capacity = 10;

    public List<Item> Items { get; } = new();
    public Item Weapon { get; set; }
    public Item Armor { get; set; }

    public bool IsFull => Items.Count >= Capacity;

    public bool TryAdd(Item item)
    {
        if (IsFull)
            return false;
        Items.Add(item);
        return true;
    }

    public void Clear()
    {
        Items.Clear();
        Weapon = null;
        Armor = null;
    }
}
