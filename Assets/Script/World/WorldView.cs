using System.Collections.Generic;
using UnityEngine;

namespace FactoryGame.World
{
    /// <summary>
    /// 시각 층. 카메라에 보이는 청크 범위만 계산해 ChunkView를 붙이고, 범위 밖 뷰는 풀로 돌려보낸다.
    /// 데이터(WorldGrid)는 화면 밖에서도 그대로 남고 시뮬레이션되며, 시각만 켜고 끈다.
    /// 타일 아트는 TileSet → TileAtlas 한 장으로 구워지고, 모든 청크 메시가 같은 머티리얼을 공유한다.
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private TileSet tileSet = new();
        [SerializeField, Min(0), Tooltip("화면 밖으로 미리 준비해 둘 청크 여유폭")] private int chunkPadding = 1;
        [SerializeField, Tooltip("지형 메시의 정렬 순서. 벨트(10)·플레이어(20)보다 낮게")] private int terrainSortingOrder = 0;

        private readonly Dictionary<Vector2Int, ChunkView> active = new();
        private readonly Stack<ChunkView> pool = new();
        private readonly List<Vector2Int> toRelease = new();
        private RectInt lastRect = new(int.MinValue / 2, int.MinValue / 2, 0, 0);
        private WorldManager world;
        private TileAtlas atlas;
        private Material material;

        public int ActiveViewCount => active.Count;
        public int PooledViewCount => pool.Count;
        public RectInt VisibleChunks => lastRect;
        public TileAtlas Atlas => atlas;

        private void Awake()
        {
            atlas = new TileAtlas(tileSet);
            material = tileSet.material != null ? new Material(tileSet.material) : CreateDefaultMaterial();
            material.mainTexture = atlas.Texture;
        }

        private void Start()
        {
            world = WorldManager.Instance;
            if (viewCamera == null) viewCamera = Camera.main;
            if (world != null) world.ChunkGenerated += OnChunkGenerated;
        }

        private void OnDestroy()
        {
            if (world != null) world.ChunkGenerated -= OnChunkGenerated;
            if (material != null) Destroy(material);
            atlas?.Dispose();
        }

        private void LateUpdate()
        {
            if (world == null || viewCamera == null) return;

            RectInt rect = ComputeVisibleChunkRect();
            if (!rect.Equals(lastRect))
            {
                lastRect = rect;
                SyncActiveSet(rect);
            }

            // 로직 층이 바꾼 청크만 다시 만든다
            foreach (var kv in active)
            {
                var view = kv.Value;
                if (view.Chunk != null && view.Chunk.IsDirty) view.Refresh();
            }
        }

        private RectInt ComputeVisibleChunkRect()
        {
            float halfH = viewCamera.orthographicSize;
            float halfW = halfH * viewCamera.aspect;
            Vector3 c = viewCamera.transform.position;

            int minCx = Mathf.FloorToInt((c.x - halfW) / ChunkData.Size) - chunkPadding;
            int maxCx = Mathf.FloorToInt((c.x + halfW) / ChunkData.Size) + chunkPadding;
            int minCy = Mathf.FloorToInt((c.y - halfH) / ChunkData.Size) - chunkPadding;
            int maxCy = Mathf.FloorToInt((c.y + halfH) / ChunkData.Size) + chunkPadding;

            return new RectInt(minCx, minCy, maxCx - minCx + 1, maxCy - minCy + 1);
        }

        private void SyncActiveSet(RectInt rect)
        {
            // 범위를 벗어난 뷰 회수
            toRelease.Clear();
            foreach (var kv in active)
                if (!rect.Contains(kv.Key)) toRelease.Add(kv.Key);

            foreach (var key in toRelease)
            {
                var view = active[key];
                active.Remove(key);
                view.Unbind();
                view.gameObject.SetActive(false);
                pool.Push(view);
            }

            // 범위 안의 청크: 생성돼 있으면 바로 표시, 아니면 생성 요청(완료 시 이벤트로 표시)
            for (int cy = rect.yMin; cy < rect.yMax; cy++)
            {
                for (int cx = rect.xMin; cx < rect.xMax; cx++)
                {
                    var key = new Vector2Int(cx, cy);
                    if (active.ContainsKey(key)) continue;

                    if (world.Grid.TryGetChunk(key, out var chunk) && chunk.IsGenerated) Show(chunk);
                    else world.RequestChunk(key);
                }
            }
        }

        private void OnChunkGenerated(ChunkData chunk)
        {
            if (active.TryGetValue(chunk.Coord, out var view)) view.Refresh();
            else if (lastRect.Contains(chunk.Coord)) Show(chunk);
        }

        private void Show(ChunkData chunk)
        {
            var view = pool.Count > 0 ? pool.Pop() : CreateView();
            view.gameObject.SetActive(true);
            view.name = $"Chunk ({chunk.Coord.x}, {chunk.Coord.y})";
            view.Bind(chunk);
            active[chunk.Coord] = view;
        }

        private ChunkView CreateView()
        {
            var go = new GameObject("Chunk", typeof(MeshFilter), typeof(MeshRenderer), typeof(ChunkView));
            go.transform.SetParent(transform, false);
            var view = go.GetComponent<ChunkView>();
            view.Init(atlas, material, terrainSortingOrder);
            return view;
        }

        private static Material CreateDefaultMaterial()
        {
            string[] candidates =
            {
                "Universal Render Pipeline/2D/Sprite-Unlit-Default",
                "Sprites/Default",
                "Unlit/Transparent",
            };
            foreach (var name in candidates)
            {
                var shader = Shader.Find(name);
                if (shader != null) return new Material(shader) { name = "TileAtlas (runtime)" };
            }
            Debug.LogError("[WorldView] 타일 셰이더를 찾지 못했습니다. TileSet.material을 직접 지정하세요.");
            return new Material(Shader.Find("Hidden/InternalErrorShader"));
        }
    }
}
