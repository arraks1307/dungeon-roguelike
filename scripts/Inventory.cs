using System.Collections.Generic;

namespace Roguelike;

/// <summary>
/// Сумка игрока и надетая экипировка (пять слотов). Надетые предметы в сумке не лежат.
/// Расходники одного вида складываются в стопки до Item.MaxStack штук.
/// </summary>
public class Inventory
{
    public const int Capacity = 10;

    public static readonly EquipSlot[] AllSlots =
        { EquipSlot.Weapon, EquipSlot.Armor, EquipSlot.Helmet, EquipSlot.Ring, EquipSlot.Amulet };

    private readonly Dictionary<EquipSlot, Item> _equipped = new();

    public List<Item> Items { get; } = new();
    public IEnumerable<Item> Equipped => _equipped.Values;
    public bool IsFull => Items.Count >= Capacity;

    public Item GetEquipped(EquipSlot slot) => _equipped.GetValueOrDefault(slot);

    /// <summary>Поместится ли предмет: есть свободная ячейка или неполная стопка того же вида.</summary>
    public bool CanAdd(Item item) => !IsFull || Items.Exists(existing => existing.CanStackWith(item));

    public bool TryAdd(Item item)
    {
        var stack = Items.Find(existing => existing.CanStackWith(item));
        if (stack != null)
        {
            stack.Count++;
            return true;
        }
        if (IsFull)
            return false;
        Items.Add(item);
        return true;
    }

    /// <summary>Забирает одну штуку из ячейки и возвращает её отдельным предметом.</summary>
    public Item TakeOne(int index)
    {
        var item = Items[index];
        if (item.Count > 1)
        {
            item.Count--;
            return item.CloneSingle();
        }
        Items.RemoveAt(index);
        return item;
    }

    /// <summary>
    /// Надеть предмет из сумки. Снятый предмет встаёт на его место,
    /// поэтому для обмена не нужна свободная ячейка.
    /// </summary>
    public void Equip(int index)
    {
        var item = Items[index];
        var previous = GetEquipped(item.Slot);
        _equipped[item.Slot] = item;

        if (previous != null)
            Items[index] = previous;
        else
            Items.RemoveAt(index);
    }

    /// <summary>Надеть предмет напрямую, минуя сумку (при загрузке сохранения).</summary>
    public void SetEquipped(Item item) => _equipped[item.Slot] = item;

    public void Clear()
    {
        Items.Clear();
        _equipped.Clear();
    }

    public static string SlotName(EquipSlot slot) => slot switch
    {
        EquipSlot.Weapon => "Оружие",
        EquipSlot.Armor => "Броня",
        EquipSlot.Helmet => "Шлем",
        EquipSlot.Ring => "Кольцо",
        EquipSlot.Amulet => "Амулет",
        _ => "",
    };
}
