using System.Runtime.InteropServices;

namespace FactoryGame.World
{
    public enum TerrainKind : byte
    {
        DeepWater,
        ShallowWater,
        Sand,
        Grass,
        Forest
    }

    public enum OreType : byte
    {
        None = 0,
        Iron,
        Copper,
        Coal
    }

    /// <summary>
    /// 타일 1칸의 고유 상태값만 담는 플라이웨이트 구조체 (12바이트).
    /// GameObject/SpriteRenderer 같은 시각 참조는 절대 넣지 않는다 — 시각은 ChunkView가 담당.
    /// 좌표 역시 담지 않는다 — 좌표는 청크 배열의 인덱스로부터 유도된다.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TileCell
    {
        public TerrainKind terrain;     // 1
        public OreType ore;         // 1
        public byte height;         // 1  노이즈 높이 0~255 (0.0~1.0)
        public byte flags;          // 1  예약 (예: 오염, 탐사 여부)
        public ushort oreAmount;    // 2  남은 매장량
        public ushort reserved;     // 2  예약
        public int entityId;        // 4  이 타일을 점유한 건물의 EntityRegistry ID (0 = 없음)

        public float Height01 => height / 255f;
        public bool IsLand => terrain >= TerrainKind.Sand;
        public bool HasOre => ore != OreType.None && oreAmount > 0;
        public bool IsOccupied => entityId != 0;
        public bool IsBuildable => IsLand && entityId == 0;
    }
}
