using System.Collections.Generic;
using UnityEngine;

namespace FactoryGame.Core
{
    /// <summary>정수 ID로 참조되는 정의(Definition) 데이터. 0은 항상 "없음".</summary>
    public interface IDefinition
    {
        string Key { get; }
        int Id { get; set; }
    }

    /// <summary>
    /// 통합 ID 레지스트리 — 문자열 키 ↔ 정수 ID.
    /// 아이템/건물/자원 종류를 객체 참조가 아닌 정수로 다루어 결합도를 낮추고,
    /// 타일/세그먼트 등 순수 데이터 구조체 안에 int 하나로 저장할 수 있게 한다.
    /// </summary>
    public sealed class DefinitionRegistry<T> where T : class, IDefinition
    {
        private readonly List<T> byId = new() { null }; // 인덱스 0 = None
        private readonly Dictionary<string, int> byKey = new();

        public int Count => byId.Count - 1;

        public int Register(T def)
        {
            if (byKey.TryGetValue(def.Key, out int existing))
            {
                def.Id = existing;
                byId[existing] = def; // 같은 키 재등록 → 교체
                return existing;
            }
            int id = byId.Count;
            def.Id = id;
            byId.Add(def);
            byKey[def.Key] = id;
            return id;
        }

        public T Get(int id) => (id > 0 && id < byId.Count) ? byId[id] : null;
        public T Get(string key) => byKey.TryGetValue(key, out int id) ? byId[id] : null;
        public bool TryGetId(string key, out int id) => byKey.TryGetValue(key, out id);

        public IEnumerable<T> All
        {
            get { for (int i = 1; i < byId.Count; i++) yield return byId[i]; }
        }
    }

    public sealed class ItemDefinition : IDefinition
    {
        public string Key { get; }
        public int Id { get; set; }
        public string DisplayName;
        public Color32 DebugColor;
        public int StackSize = 100;

        public ItemDefinition(string key, string displayName, Color32 debugColor)
        {
            Key = key; DisplayName = displayName; DebugColor = debugColor;
        }
    }

    public sealed class BuildingDefinition : IDefinition
    {
        public string Key { get; }
        public int Id { get; set; }
        public string DisplayName;
        public Vector2Int Size = Vector2Int.one;
        /// <summary>설치할 때 소모되고 철거하면 돌려받는 아이템의 키. null이면 아이템 없이 설치 가능.</summary>
        public string ItemKey;

        public BuildingDefinition(string key, string displayName, Vector2Int size, string itemKey = null)
        {
            Key = key; DisplayName = displayName; Size = size; ItemKey = itemKey;
        }
    }

    /// <summary>
    /// 런타임 인스턴스(건물, 세그먼트 등) ↔ 정수 ID. 해제된 ID는 프리리스트로 재활용한다.
    /// TODO: 오래된 ID가 재활용된 슬롯을 가리키는 문제를 막으려면 상위 비트에 세대(generation)를 넣을 것.
    /// </summary>
    public sealed class EntityRegistry
    {
        private readonly List<object> entities = new() { null };
        private readonly Stack<int> free = new();

        public int Count { get; private set; }

        public int Add(object entity)
        {
            int id;
            if (free.Count > 0) { id = free.Pop(); entities[id] = entity; }
            else { id = entities.Count; entities.Add(entity); }
            Count++;
            return id;
        }

        public TEntity Get<TEntity>(int id) where TEntity : class
            => (id > 0 && id < entities.Count) ? entities[id] as TEntity : null;

        public bool Remove(int id)
        {
            if (id <= 0 || id >= entities.Count || entities[id] == null) return false;
            entities[id] = null;
            free.Push(id);
            Count--;
            return true;
        }
    }

    /// <summary>전역 레지스트리 모음.</summary>
    public static class Registries
    {
        public static readonly DefinitionRegistry<ItemDefinition> Items = new();
        public static readonly DefinitionRegistry<BuildingDefinition> Buildings = new();
        public static readonly EntityRegistry Entities = new();

        private static bool defaultsRegistered;
        private static readonly Dictionary<int, BuildingDefinition> buildingByItem = new();

        /// <summary>디버그/초기 테스트용 기본 정의. 나중에 ScriptableObject 데이터로 교체 예정.</summary>
        public static void EnsureDefaults()
        {
            if (defaultsRegistered) return;
            defaultsRegistered = true;

            Items.Register(new ItemDefinition("iron-ore", "철광석", new Color32(225, 119, 52, 255)));
            Items.Register(new ItemDefinition("copper-ore", "구리광석", new Color32(207, 207, 207, 255)));
            Items.Register(new ItemDefinition("coal", "석탄", new Color32(65, 65, 65, 255)));
            Items.Register(new ItemDefinition("conveyor", "컨베이어 벨트", new Color32(230, 200, 60, 255)));

            Buildings.Register(new BuildingDefinition("conveyor", "컨베이어 벨트", Vector2Int.one, itemKey: "conveyor"));
            LinkBuildingItems();
        }

        /// <summary>아이템 ID → 그 아이템으로 설치하는 건물. 없으면 null.</summary>
        public static BuildingDefinition BuildingForItem(int itemId)
            => buildingByItem.TryGetValue(itemId, out var b) ? b : null;

        /// <summary>건물/아이템을 추가 등록한 뒤 호출해 아이템↔건물 연결을 다시 만든다.</summary>
        public static void LinkBuildingItems()
        {
            buildingByItem.Clear();
            foreach (var b in Buildings.All)
                if (!string.IsNullOrEmpty(b.ItemKey) && Items.TryGetId(b.ItemKey, out int id)) buildingByItem[id] = b;
        }
    }
}
