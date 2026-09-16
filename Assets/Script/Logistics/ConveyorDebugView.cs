using System.Collections.Generic;
using FactoryGame.Core;
using FactoryGame.World;
using UnityEngine;

namespace FactoryGame.Logistics
{
    /// <summary>
    /// 컨베이어 데이터의 임시 시각화. 벨트 타일은 사각형 + 방향 표시, 아이템은 작은 사각형.
    /// 프리팹 없이 런타임에 흰 스프라이트를 만들어 쓴다. 실제 아트가 들어오면 교체.
    /// 아이템 위치는 TickSystem.Alpha로 틱 사이를 보간한다 (데이터는 틱마다만 바뀜).
    /// </summary>
    public sealed class ConveyorDebugView : MonoBehaviour
    {
        [SerializeField] private Color beltColor = new(0.25f, 0.25f, 0.28f, 1f);
        [SerializeField] private Color arrowColor = new(0.85f, 0.85f, 0.2f, 1f);
        [SerializeField] private float itemSize = 0.35f;
        [SerializeField] private int sortingOrder = 10;

        private Sprite square;
        private Transform beltRoot, itemRoot;
        private readonly Dictionary<Vector2Int, Transform> beltTiles = new();
        private readonly List<SpriteRenderer> itemPool = new();
        private int lastStructureVersion = -1;

        private void Awake()
        {
            var tex = Texture2D.whiteTexture;
            square = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);

            beltRoot = new GameObject("Belts").transform;
            beltRoot.SetParent(transform, false);
            itemRoot = new GameObject("Items").transform;
            itemRoot.SetParent(transform, false);
        }

        private void LateUpdate()
        {
            var world = WorldManager.Instance;
            if (world == null) return;
            var net = world.Conveyors;

            if (net.StructureVersion != lastStructureVersion)
            {
                RebuildBelts(net);
                lastStructureVersion = net.StructureVersion;
            }
            DrawItems(net);
        }

        private void RebuildBelts(ConveyorNetwork net)
        {
            // 사라진 타일 제거
            var stale = new List<Vector2Int>();
            foreach (var kv in beltTiles)
                if (!net.HasBelt(kv.Key)) stale.Add(kv.Key);
            foreach (var key in stale) { Destroy(beltTiles[key].gameObject); beltTiles.Remove(key); }

            // 새 타일 생성 / 방향 갱신
            foreach (var kv in net.BeltTiles)
            {
                if (!beltTiles.TryGetValue(kv.Key, out var t))
                {
                    t = CreateBeltTile(kv.Key);
                    beltTiles[kv.Key] = t;
                }
                t.rotation = Quaternion.Euler(0f, 0f, kv.Value.Dir.ToAngleDeg());
            }
        }

        private Transform CreateBeltTile(Vector2Int pos)
        {
            var go = new GameObject($"Belt {pos.x},{pos.y}");
            go.transform.SetParent(beltRoot, false);
            go.transform.position = new Vector3(pos.x + 0.5f, pos.y + 0.5f, 0f);

            var body = MakeSprite("body", go.transform, beltColor, sortingOrder);
            body.transform.localScale = new Vector3(0.92f, 0.92f, 1f);

            // 진행 방향(로컬 +X)을 가리키는 표시
            var arrow = MakeSprite("arrow", go.transform, arrowColor, sortingOrder + 1);
            arrow.transform.localPosition = new Vector3(0.22f, 0f, 0f);
            arrow.transform.localScale = new Vector3(0.3f, 0.5f, 1f);

            return go.transform;
        }

        private SpriteRenderer MakeSprite(string name, Transform parent, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = square;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        private void DrawItems(ConveyorNetwork net)
        {
            float alpha = TickSystem.Instance != null ? TickSystem.Instance.Alpha : 0f;
            int used = 0;

            var segs = net.Segments;
            for (int s = 0; s < segs.Count; s++)
            {
                var seg = segs[s];
                for (int i = 0; i < seg.Items.Count; i++)
                {
                    var it = seg.Items[i];
                    float pos = it.pos;
                    if (it.moving) pos = Mathf.Min(pos + net.Speed * alpha, seg.Length);

                    var sr = GetItemSprite(used++);
                    Vector2 wp = seg.ItemWorldPosition(pos);
                    sr.transform.position = new Vector3(wp.x, wp.y, 0f);
                    sr.transform.localScale = new Vector3(itemSize, itemSize, 1f);

                    var def = Registries.Items.Get(it.itemId);
                    sr.color = def != null ? (Color)def.DebugColor : Color.magenta;
                }
            }

            for (int i = used; i < itemPool.Count; i++)
                if (itemPool[i].gameObject.activeSelf) itemPool[i].gameObject.SetActive(false);
        }

        private SpriteRenderer GetItemSprite(int index)
        {
            while (itemPool.Count <= index)
            {
                var sr = MakeSprite($"Item {itemPool.Count}", itemRoot, Color.white, sortingOrder + 2);
                itemPool.Add(sr);
            }
            var r = itemPool[index];
            if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
            return r;
        }
    }
}
