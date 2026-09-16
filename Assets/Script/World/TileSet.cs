using System;
using UnityEngine;

namespace FactoryGame.World
{
    /// <summary>타일 종류 하나의 아트. sprites가 비어 있으면 color로 임시 타일을 굽는다.</summary>
    [Serializable]
    public class TileArt
    {
        public Color32 color;
        [Tooltip("같은 종류의 변형 스프라이트(최대 4장). 전부 tileSize×tileSize 픽셀이어야 하고, 텍스처 임포트 설정에서 Read/Write가 켜져 있어야 한다.")]
        public Sprite[] sprites = Array.Empty<Sprite>();

        public TileArt() { }
        public TileArt(Color32 color) { this.color = color; }
    }

    /// <summary>
    /// 지형/자원 아트 묶음. WorldView 인스펙터에서 스프라이트를 넣으면 그 자리의 임시 타일을 대체한다.
    /// 자원은 지형 위에 겹쳐 그리므로 투명 배경 스프라이트를 권장.
    /// </summary>
    [Serializable]
    public class TileSet
    {
        [Tooltip("타일 1칸의 픽셀 크기. 스프라이트를 넣을 땐 그 크기와 맞출 것")] public int tileSize = 16;
        [Tooltip("비우면 URP 2D Sprite-Unlit 셰이더로 런타임 생성")] public Material material;

        [Header("Terrain")]
        public TileArt deepWater = new(new Color32(0, 110, 255, 255));
        public TileArt shallowWater = new(new Color32(0, 160, 255, 255));
        public TileArt sand = new(new Color32(255, 235, 4, 255));
        public TileArt grass = new(new Color32(234, 197, 75, 255));
        public TileArt forest = new(new Color32(0, 198, 35, 255));

        [Header("Ore (지형 위 덮개)")]
        public TileArt iron = new(new Color32(225, 119, 52, 255));
        public TileArt copper = new(new Color32(207, 207, 207, 255));
        public TileArt coal = new(new Color32(65, 65, 65, 255));

        public TileArt Terrain(TerrainKind t) => t switch
        {
            TerrainKind.DeepWater => deepWater,
            TerrainKind.ShallowWater => shallowWater,
            TerrainKind.Sand => sand,
            TerrainKind.Grass => grass,
            TerrainKind.Forest => forest,
            _ => grass,
        };

        public TileArt Ore(OreType o) => o switch
        {
            OreType.Iron => iron,
            OreType.Copper => copper,
            OreType.Coal => coal,
            _ => iron,
        };
    }

    /// <summary>
    /// TileSet을 한 장의 아틀라스 텍스처로 굽고 타일별 UV를 제공한다. 행 = 종류, 열 = 변형.
    /// 모든 청크 메시가 이 텍스처 하나를 공유하므로 청크 수와 무관하게 머티리얼은 1개다.
    /// </summary>
    public sealed class TileAtlas
    {
        public const int MaxVariants = 4;
        public const int TerrainRows = 5;   // TerrainKind 개수
        public const int OreRows = 3;       // OreType 개수 (None 제외)
        public const int Rows = TerrainRows + OreRows;

        public Texture2D Texture { get; private set; }
        public int TileSize { get; }

        private readonly int[] variants = new int[Rows];
        private readonly Rect[] uvs = new Rect[Rows * MaxVariants];

        public TileAtlas(TileSet set)
        {
            TileSize = Mathf.Max(2, set.tileSize);
            int w = MaxVariants * TileSize, h = Rows * TileSize;
            Texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "TileAtlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[w * h];

            for (int t = 0; t < TerrainRows; t++) BakeRow(px, t, set.Terrain((TerrainKind)t), overlay: false);
            for (int o = 0; o < OreRows; o++) BakeRow(px, TerrainRows + o, set.Ore((OreType)(o + 1)), overlay: true);

            Texture.SetPixels32(px);
            Texture.Apply(false, true);

            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < MaxVariants; c++)
                    // 반 텍셀 안쪽으로 물려 이웃 칸 번짐을 막는다 (Point 필터라 보이는 결과는 같다)
                    uvs[r * MaxVariants + c] = new Rect(
                        (c * TileSize + 0.5f) / w, (r * TileSize + 0.5f) / h,
                        (TileSize - 1f) / w, (TileSize - 1f) / h);
        }

        public void Dispose()
        {
            if (Texture != null) UnityEngine.Object.Destroy(Texture);
            Texture = null;
        }

        public int TerrainVariants(TerrainKind t) => variants[(int)t];
        public int OreVariants(OreType o) => variants[TerrainRows + (int)o - 1];
        public Rect TerrainUV(TerrainKind t, int variant) => uvs[(int)t * MaxVariants + variant];
        public Rect OreUV(OreType o, int variant) => uvs[(TerrainRows + (int)o - 1) * MaxVariants + variant];

        private void BakeRow(Color32[] px, int row, TileArt art, bool overlay)
        {
            int count = 0;
            if (art.sprites != null)
                foreach (var sp in art.sprites)
                {
                    if (count >= MaxVariants) break;
                    if (TryCopySprite(px, count, row, sp)) count++;
                }

            if (count == 0)
            {
                count = MaxVariants;
                for (int v = 0; v < count; v++) BakePlaceholder(px, v, row, v, count, art.color, overlay);
            }
            variants[row] = count;
        }

        private bool TryCopySprite(Color32[] px, int col, int row, Sprite sp)
        {
            if (sp == null) return false;
            var r = sp.textureRect;
            if ((int)r.width != TileSize || (int)r.height != TileSize)
            {
                Debug.LogWarning($"[TileAtlas] '{sp.name}' 크기 {r.width}×{r.height}가 tileSize {TileSize}와 다릅니다. 건너뜁니다.");
                return false;
            }
            if (!sp.texture.isReadable)
            {
                Debug.LogWarning($"[TileAtlas] '{sp.texture.name}' 텍스처의 Read/Write가 꺼져 있어 복사할 수 없습니다. 임포트 설정에서 켜 주세요.");
                return false;
            }

            var src = sp.texture.GetPixels((int)r.x, (int)r.y, TileSize, TileSize);
            int w = Texture.width;
            for (int y = 0; y < TileSize; y++)
                for (int x = 0; x < TileSize; x++)
                    px[(row * TileSize + y) * w + col * TileSize + x] = src[y * TileSize + x];
            return true;
        }

        /// <summary>임시 타일. 지형은 변형별 밝기 차 + 미세한 점 무늬, 자원은 투명 배경 위 덩어리 몇 개.</summary>
        private void BakePlaceholder(Color32[] px, int col, int row, int variant, int variantCount, Color32 color, bool overlay)
        {
            int ts = TileSize, w = Texture.width;
            float shade = 1f + (variant - (variantCount - 1) * 0.5f) * 0.05f;
            uint seed = (uint)(row * 7919 + variant * 104729 + 1);

            if (!overlay)
            {
                for (int y = 0; y < ts; y++)
                    for (int x = 0; x < ts; x++)
                    {
                        float n = 1f + ((Hash(seed + (uint)(x + y * ts)) & 0xFF) / 255f - 0.5f) * 0.08f;
                        px[(row * ts + y) * w + col * ts + x] = Scale(color, shade * n, 255);
                    }
                return;
            }

            for (int y = 0; y < ts; y++)
                for (int x = 0; x < ts; x++)
                    px[(row * ts + y) * w + col * ts + x] = new Color32(0, 0, 0, 0);

            int blobs = 3 + (int)(Hash(seed) % 3u);
            for (int b = 0; b < blobs; b++)
            {
                uint hb = Hash(seed * 31u + (uint)b * 7u);
                float cx = 2f + (hb & 0xFF) / 255f * (ts - 4f);
                float cy = 2f + ((hb >> 8) & 0xFF) / 255f * (ts - 4f);
                float rad = ts * (0.14f + ((hb >> 16) & 0xFF) / 255f * 0.12f);
                for (int y = 0; y < ts; y++)
                    for (int x = 0; x < ts; x++)
                    {
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d > rad) continue;
                        bool rim = d > rad - 1f;
                        px[(row * ts + y) * w + col * ts + x] = Scale(color, rim ? 0.6f : shade, 255);
                    }
            }
        }

        private static Color32 Scale(Color32 c, float f, byte alpha) => new(
            (byte)Mathf.Clamp(c.r * f, 0f, 255f),
            (byte)Mathf.Clamp(c.g * f, 0f, 255f),
            (byte)Mathf.Clamp(c.b * f, 0f, 255f),
            alpha);

        private static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7FEB352Du;
            x ^= x >> 15; x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }
    }
}
