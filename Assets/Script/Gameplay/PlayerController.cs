using FactoryGame.Core;
using FactoryGame.World;
using UnityEngine;

namespace FactoryGame.Gameplay
{
    /// <summary>
    /// 플레이어 이동. 타일 단위가 아닌 연속 이동이며, 충돌 상자(AABB)를 축별로 밀어 깊은 물·건물 타일에 막힌다.
    /// 시뮬레이션 틱과 무관하게 프레임마다 움직인다 (결정론이 필요해지면 틱으로 옮길 것).
    /// 시작 시 위치가 육지가 아니면 근처의 풀/숲 타일로 옮긴다.
    /// </summary>
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField, Tooltip("타일/초")] private float moveSpeed = 6f;
        [SerializeField, Tooltip("충돌 상자 반폭(타일)")] private float halfSize = 0.3f;
        [SerializeField] private Color bodyColor = new(0.95f, 0.85f, 0.3f, 1f);
        [SerializeField] private int sortingOrder = 20;
        [SerializeField, Min(1)] private int inventorySlots = 18;

        public Vector2 Position => transform.position;
        public Vector2 Facing { get; private set; } = Vector2.right;
        /// <summary>같은 오브젝트의 다른 컴포넌트가 Awake에서 먼저 접근해도 되도록 지연 생성.</summary>
        public Inventory Inventory => inventory ??= new Inventory(inventorySlots);

        private Inventory inventory;
        private WorldManager world;
        private Transform body;

        private void Awake()
        {
            var go = new GameObject("Body");
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprites.Player;
            sr.color = bodyColor;
            sr.sortingOrder = sortingOrder;
            body = go.transform;
        }

        private void Start()
        {
            world = WorldManager.Instance;
            if (world != null) SnapToSpawn();
        }

        private void Update()
        {
            if (world == null) return;

            Vector2 input = GameInput.Move;
            if (input.sqrMagnitude > 0f)
            {
                Facing = input.normalized;
                body.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(Facing.y, Facing.x) * Mathf.Rad2Deg);
            }

            Vector2 delta = input * (moveSpeed * Time.deltaTime);
            if (delta == Vector2.zero) return;

            // 한 번에 타일 절반 가까이 움직이면 얇은 벽을 뚫을 수 있으므로 나눠서 민다
            const float maxStep = 0.4f;
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / maxStep));
            Vector2 step = delta / steps;
            Vector2 pos = transform.position;
            for (int i = 0; i < steps; i++)
            {
                pos.x = MoveAxis(pos, step.x, xAxis: true);
                pos.y = MoveAxis(pos, step.y, xAxis: false);
            }
            transform.position = new Vector3(pos.x, pos.y, 0f);
        }

        /// <summary>한 축으로 이동하고, 앞쪽 가장자리가 막힌 타일에 들어가면 그 타일 경계에 붙인다.</summary>
        private float MoveAxis(Vector2 pos, float d, bool xAxis)
        {
            float cur = xAxis ? pos.x : pos.y;
            if (d == 0f) return cur;

            const float eps = 0.001f;
            float next = cur + d;
            float lead = d > 0f ? next + halfSize : next - halfSize;   // 이동 방향 앞쪽 가장자리
            int leadTile = Mathf.FloorToInt(lead);

            float other = xAxis ? pos.y : pos.x;
            int o0 = Mathf.FloorToInt(other - halfSize + eps);
            int o1 = Mathf.FloorToInt(other + halfSize - eps);
            for (int o = o0; o <= o1; o++)
            {
                var tile = xAxis ? new Vector2Int(leadTile, o) : new Vector2Int(o, leadTile);
                if (world.IsWalkable(tile)) continue;
                return d > 0f ? leadTile - halfSize - eps : leadTile + 1f + halfSize + eps;
            }
            return next;
        }

        private void SnapToSpawn()
        {
            var start = new Vector2Int(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(transform.position.y));
            if (!FindSpawnTile(start, 96, out var found))
                Debug.LogWarning($"[PlayerController] {start} 주변 96타일 안에 육지가 없어 그 자리에 둡니다.");
            transform.position = new Vector3(found.x + 0.5f, found.y + 0.5f, 0f);
        }

        /// <summary>start에서 나선형으로 넓혀 가며 첫 풀/숲·통행 가능 타일을 찾는다. 필요한 청크는 즉시 생성한다.</summary>
        private bool FindSpawnTile(Vector2Int start, int maxRadius, out Vector2Int result)
        {
            for (int r = 0; r <= maxRadius; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue; // 테두리만
                        var p = start + new Vector2Int(dx, dy);
                        world.GetChunkNow(WorldGrid.WorldToChunk(p));
                        if (world.TryGetTile(p, out var t) && t.terrain >= TerrainKind.Grass && world.IsWalkable(p))
                        {
                            result = p;
                            return true;
                        }
                    }
                }
            }
            result = start;
            return false;
        }
    }
}
