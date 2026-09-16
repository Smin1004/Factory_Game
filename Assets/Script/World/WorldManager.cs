using System;
using System.Collections.Generic;
using FactoryGame.Core;
using FactoryGame.Logistics;
using UnityEngine;

namespace FactoryGame.World
{
    /// <summary>
    /// 로직 층의 진입점. 희소 그리드·생성기·컨베이어 네트워크를 소유하고,
    /// 청크 생성 요청을 큐에 모아 프레임당 예산만큼만 처리해 스파이크를 막는다.
    /// 시각 층(WorldView)은 이 클래스의 데이터를 읽기만 하고, 변경은 이벤트/더티 플래그로 통지받는다.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class WorldManager : MonoBehaviour
    {
        public static WorldManager Instance { get; private set; }

        [SerializeField] private WorldGenSettings genSettings = new();
        [SerializeField, Min(1), Tooltip("프레임당 생성할 최대 청크 수")] private int generationBudgetPerFrame = 8;

        public WorldGenSettings GenSettings => genSettings;
        public WorldGrid Grid { get; private set; }
        public WorldGenerator Generator { get; private set; }
        public ConveyorNetwork Conveyors { get; private set; }

        /// <summary>청크 생성(또는 재생성)이 끝났을 때. 시각 층이 구독한다.</summary>
        public event Action<ChunkData> ChunkGenerated;

        private readonly Queue<Vector2Int> genQueue = new();
        private readonly HashSet<Vector2Int> queued = new();

        public int PendingChunkCount => genQueue.Count;

        private readonly int[] oreItemIds = new int[4]; // OreType 인덱스 → 아이템 ID

        private void Awake()
        {
            Instance = this;
            Registries.EnsureDefaults();
            oreItemIds[(int)OreType.Iron] = ItemId("iron-ore");
            oreItemIds[(int)OreType.Copper] = ItemId("copper-ore");
            oreItemIds[(int)OreType.Coal] = ItemId("coal");
            Grid = new WorldGrid();
            Generator = new WorldGenerator(genSettings);
            Conveyors = new ConveyorNetwork();
        }

        private void Start()
        {
            TickSystem.GetOrCreate().Register(Conveyors);
        }

        private void OnDestroy()
        {
            if (TickSystem.Instance != null) TickSystem.Instance.Unregister(Conveyors);
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            int budget = generationBudgetPerFrame;
            while (budget-- > 0 && genQueue.Count > 0)
            {
                var coord = genQueue.Dequeue();
                queued.Remove(coord);

                var chunk = Grid.GetOrCreateChunk(coord);
                if (chunk.IsGenerated) continue;
                Generator.Generate(chunk);
                ChunkGenerated?.Invoke(chunk);
            }
        }

        /// <summary>비동기(예산 기반) 생성 요청. 이미 생성됐거나 대기 중이면 무시.</summary>
        public void RequestChunk(Vector2Int coord)
        {
            if (Grid.TryGetChunk(coord, out var chunk) && chunk.IsGenerated) return;
            if (queued.Add(coord)) genQueue.Enqueue(coord);
        }

        /// <summary>즉시 생성이 필요한 경우(로직이 지금 당장 타일을 읽어야 할 때).</summary>
        public ChunkData GetChunkNow(Vector2Int coord)
        {
            var chunk = Grid.GetOrCreateChunk(coord);
            if (!chunk.IsGenerated)
            {
                Generator.Generate(chunk);
                ChunkGenerated?.Invoke(chunk);
            }
            return chunk;
        }

        public bool TryGetTile(Vector2Int world, out TileCell tile) => Grid.TryGetTile(world, out tile);

        private static int ItemId(string key) => Registries.Items.TryGetId(key, out int id) ? id : 0;

        /// <summary>자원 종류 → 캐면 나오는 아이템 ID. 없으면 0.</summary>
        public int OreItemId(OreType ore) => oreItemIds[(int)ore];

        /// <summary>플레이어가 지나갈 수 있는 타일인지. 미로드 청크·깊은 물·벨트가 아닌 건물은 막힌다.</summary>
        public bool IsWalkable(Vector2Int pos)
        {
            if (!Grid.TryGetTile(pos, out var tile)) return false;
            if (tile.terrain == TerrainKind.DeepWater) return false;
            if (tile.entityId == 0) return true;
            return Registries.Entities.Get<BeltEntity>(tile.entityId) != null; // 벨트 위는 걸을 수 있다
        }

        /// <summary>자원 타일에서 1개 채굴. 매장량이 0이 되면 자원이 사라지고 청크가 dirty 표시된다.</summary>
        public bool TryMineOre(Vector2Int pos, out int itemId)
        {
            itemId = 0;
            if (!Grid.TryGetTileRef(pos, out var chunk, out int index)) return false;
            ref var tile = ref chunk.Tiles[index];
            if (!tile.HasOre) return false;
            itemId = OreItemId(tile.ore);
            if (itemId == 0) return false;

            tile.oreAmount--;
            if (tile.oreAmount == 0)
            {
                tile.ore = OreType.None;
                chunk.MarkDirty();
            }
            return true;
        }

        // ---------------- 벨트 (타일 점유와 네트워크를 함께 갱신) ----------------

        /// <summary>건설 가능한 타일(육지, 미점유)에만 벨트를 설치하고 TileCell.entityId에 점유를 기록한다.</summary>
        public bool TryPlaceBelt(Vector2Int pos, Direction dir)
        {
            if (!Grid.TryGetTileRef(pos, out var chunk, out int index)) return false;
            if (!chunk.Tiles[index].IsBuildable) return false;
            if (!Conveyors.PlaceBelt(pos, dir)) return false;

            chunk.Tiles[index].entityId = Registries.Entities.Add(new BeltEntity(pos));
            return true;
        }

        public bool RemoveBelt(Vector2Int pos)
        {
            if (!Conveyors.RemoveBelt(pos)) return false;

            if (Grid.TryGetTileRef(pos, out var chunk, out int index))
            {
                int id = chunk.Tiles[index].entityId;
                if (Registries.Entities.Get<BeltEntity>(id) != null)
                {
                    Registries.Entities.Remove(id);
                    chunk.Tiles[index].entityId = 0;
                }
            }
            return true;
        }

        private readonly int[] entityBuffer = new int[ChunkData.Area];
        private readonly List<Vector2Int> beltRemoveBuffer = new();

        /// <summary>
        /// 설정 변경 후 로드된 모든 청크를 다시 생성(디버그 용도). 건물 점유(entityId)는 보존하고,
        /// 새 지형에서 물 위에 놓이게 된 벨트는 철거한다. 매장량은 초기화된다.
        /// </summary>
        public void RegenerateAll()
        {
            Generator = new WorldGenerator(genSettings);
            foreach (var chunk in Grid.Chunks)
            {
                if (!chunk.IsGenerated) continue;
                var tiles = chunk.Tiles;
                for (int i = 0; i < ChunkData.Area; i++) entityBuffer[i] = tiles[i].entityId;
                Generator.Generate(chunk);
                for (int i = 0; i < ChunkData.Area; i++) tiles[i].entityId = entityBuffer[i];
                ChunkGenerated?.Invoke(chunk);
            }

            beltRemoveBuffer.Clear();
            foreach (var kv in Conveyors.BeltTiles)
                if (!Grid.TryGetTile(kv.Key, out var tile) || !tile.IsLand) beltRemoveBuffer.Add(kv.Key);
            foreach (var pos in beltRemoveBuffer) RemoveBelt(pos);
        }
    }
}
