using System.Collections.Generic;
using UnityEngine;

namespace FactoryGame.World
{
    /// <summary>
    /// 청크 기반 희소 그리드. 데이터가 존재하는 청크만 Dictionary에 올라가므로 맵 크기에 상한이 없다.
    /// 월드 좌표 → 청크 좌표 변환은 산술 시프트(>> 4)와 마스크(&amp; 15)로 처리해 음수 좌표에서도 올바르게 floor 된다.
    /// 순수 C# 데이터 — Unity 오브젝트를 전혀 참조하지 않는다.
    /// </summary>
    public sealed class WorldGrid
    {
        private readonly Dictionary<Vector2Int, ChunkData> chunks = new();

        public int ChunkCount => chunks.Count;
        public IEnumerable<ChunkData> Chunks => chunks.Values;

        // ---------- 좌표 변환 ----------

        public static Vector2Int WorldToChunk(int wx, int wy) => new(wx >> ChunkData.Shift, wy >> ChunkData.Shift);
        public static Vector2Int WorldToChunk(Vector2Int world) => WorldToChunk(world.x, world.y);

        public static void Split(Vector2Int world, out Vector2Int chunk, out int lx, out int ly)
        {
            chunk = WorldToChunk(world);
            lx = world.x & ChunkData.Mask;
            ly = world.y & ChunkData.Mask;
        }

        // ---------- 청크 ----------

        public bool TryGetChunk(Vector2Int coord, out ChunkData chunk) => chunks.TryGetValue(coord, out chunk);

        public bool HasChunk(Vector2Int coord) => chunks.ContainsKey(coord);

        /// <summary>없으면 빈(미생성) 청크를 만들어 반환. 생성은 WorldGenerator가 담당.</summary>
        public ChunkData GetOrCreateChunk(Vector2Int coord)
        {
            if (!chunks.TryGetValue(coord, out var chunk))
            {
                chunk = new ChunkData(coord);
                chunks.Add(coord, chunk);
            }
            return chunk;
        }

        public bool RemoveChunk(Vector2Int coord) => chunks.Remove(coord);

        public void Clear() => chunks.Clear();

        // ---------- 타일 ----------

        public bool TryGetTile(Vector2Int world, out TileCell tile)
        {
            Split(world, out var c, out int lx, out int ly);
            if (chunks.TryGetValue(c, out var chunk) && chunk.IsGenerated)
            {
                tile = chunk[lx, ly];
                return true;
            }
            tile = default;
            return false;
        }

        /// <summary>청크가 로드되어 있을 때만 ref 반환. 호출자가 수정 후 chunk.MarkDirty()를 호출해야 한다.</summary>
        public bool TryGetTileRef(Vector2Int world, out ChunkData chunk, out int index)
        {
            Split(world, out var c, out int lx, out int ly);
            if (chunks.TryGetValue(c, out chunk) && chunk.IsGenerated)
            {
                index = ChunkData.Index(lx, ly);
                return true;
            }
            index = -1;
            return false;
        }

        public bool SetTile(Vector2Int world, in TileCell tile)
        {
            if (!TryGetTileRef(world, out var chunk, out int index)) return false;
            chunk.Tiles[index] = tile;
            chunk.IsDirty = true;
            return true;
        }

        public bool SetEntityId(Vector2Int world, int entityId)
        {
            if (!TryGetTileRef(world, out var chunk, out int index)) return false;
            chunk.Tiles[index].entityId = entityId;
            return true; // 시각 변화가 없으므로 dirty 표시 안 함
        }
    }
}
