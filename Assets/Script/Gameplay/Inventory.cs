using System;
using FactoryGame.Core;

namespace FactoryGame.Gameplay
{
    public struct ItemStack
    {
        public int itemId;  // Registries.Items ID, 0 = 빈 슬롯
        public int count;

        public bool IsEmpty => itemId == 0 || count <= 0;
    }

    /// <summary>
    /// 고정 슬롯 인벤토리. 슬롯 번호가 바뀌지 않으므로 핫바(1~9 = 슬롯 0~8)로 그대로 쓴다.
    /// 순수 C# — Unity 오브젝트 참조 없음.
    /// </summary>
    public sealed class Inventory
    {
        public readonly ItemStack[] Slots;

        /// <summary>내용이 바뀔 때. UI 갱신용.</summary>
        public event Action Changed;

        public Inventory(int capacity) { Slots = new ItemStack[Math.Max(1, capacity)]; }

        public int Capacity => Slots.Length;

        private static int StackSize(int itemId)
        {
            var def = Registries.Items.Get(itemId);
            return def != null ? Math.Max(1, def.StackSize) : 1;
        }

        public int CountOf(int itemId)
        {
            int n = 0;
            for (int i = 0; i < Slots.Length; i++) if (Slots[i].itemId == itemId) n += Slots[i].count;
            return n;
        }

        /// <summary>해당 아이템을 더 넣을 수 있는 수량 (기존 스택 여유 + 빈 슬롯).</summary>
        public int SpaceFor(int itemId)
        {
            int stack = StackSize(itemId), space = 0;
            for (int i = 0; i < Slots.Length; i++)
            {
                if (Slots[i].IsEmpty) space += stack;
                else if (Slots[i].itemId == itemId) space += stack - Slots[i].count;
            }
            return space;
        }

        public bool CanAdd(int itemId, int count) => SpaceFor(itemId) >= count;

        /// <summary>넣고 남은 수량을 반환 (0이면 전부 들어감). 기존 스택부터 채운 뒤 빈 슬롯을 쓴다.</summary>
        public int Add(int itemId, int count)
        {
            if (itemId == 0 || count <= 0) return count;
            int stack = StackSize(itemId);

            for (int pass = 0; pass < 2 && count > 0; pass++)
            {
                for (int i = 0; i < Slots.Length && count > 0; i++)
                {
                    ref var s = ref Slots[i];
                    bool target = pass == 0 ? (s.itemId == itemId && !s.IsEmpty && s.count < stack) : s.IsEmpty;
                    if (!target) continue;
                    if (s.IsEmpty) { s.itemId = itemId; s.count = 0; }
                    int n = Math.Min(count, stack - s.count);
                    s.count += n;
                    count -= n;
                }
            }
            Changed?.Invoke();
            return count;
        }

        /// <summary>뒤쪽 슬롯부터 꺼낸다. 실제로 꺼낸 수량을 반환.</summary>
        public int Remove(int itemId, int count)
        {
            int removed = 0;
            for (int i = Slots.Length - 1; i >= 0 && removed < count; i--)
            {
                ref var s = ref Slots[i];
                if (s.itemId != itemId || s.count <= 0) continue;
                int n = Math.Min(s.count, count - removed);
                s.count -= n;
                removed += n;
                if (s.count == 0) s.itemId = 0;
            }
            if (removed > 0) Changed?.Invoke();
            return removed;
        }

        /// <summary>특정 슬롯에서 count개 꺼내기. 핫바에 든 아이템 소모용.</summary>
        public bool TryTakeFromSlot(int slot, int count)
        {
            if (slot < 0 || slot >= Slots.Length) return false;
            ref var s = ref Slots[slot];
            if (s.IsEmpty || s.count < count) return false;
            s.count -= count;
            if (s.count == 0) s.itemId = 0;
            Changed?.Invoke();
            return true;
        }
    }
}
