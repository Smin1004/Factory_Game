using UnityEngine;

namespace FactoryGame.World
{
    /// <summary>
    /// 16×16 타일 묶음. 타일은 1차원 배열(행 우선)로 연속 배치되어 캐시 친화적이다.
    /// 청크 자체는 클래스지만 내부 타일은 struct 배열이므로 청크 하나가 GC 객체 1개 + 배열 1개다.
    /// </summary>
    public sealed class ChunkData
    {
        public const int Size = 16;
        public const int Shift = 4;          // Size == 1 << Shift
        public const int Mask = Size - 1;    // 로컬 좌표 추출용
        public const int Area = Size * Size;

        public readonly Vector2Int Coord;
        public readonly TileCell[] Tiles = new TileCell[Area];

        /// <summary>절차 생성이 끝났는지. false면 Tiles는 전부 기본값.</summary>
        public bool IsGenerated;
        /// <summary>데이터가 바뀌어 시각 층 갱신이 필요한지. ChunkView가 소비하며 false로 되돌린다.</summary>
        public bool IsDirty;

        public ChunkData(Vector2Int coord) { Coord = coord; }

        public Vector2Int WorldOrigin => new(Coord.x << Shift, Coord.y << Shift);

        public static int Index(int lx, int ly) => (ly << Shift) + lx;

        public ref TileCell this[int lx, int ly] => ref Tiles[Index(lx, ly)];

        public void MarkDirty() => IsDirty = true;
    }
}
