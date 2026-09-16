using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace FactoryGame.World
{
    [Serializable]
    public class OreLayer
    {
        public OreType type = OreType.Iron;
        [Tooltip("자원 군락의 크기/간격. 작을수록 군락이 촘촘해짐")] public float scale = 20f;
        [Tooltip("이 값 이상일 때만 자원 배치. 높을수록 군락이 작아짐"), Range(0f, 1f)] public float threshold = 0.6f;
        [Tooltip("군락 중심의 최대 매장량")] public int maxAmount = 5000;

        public OreLayer() { }
        public OreLayer(OreType type, float scale, float threshold, int maxAmount)
        {
            this.type = type; this.scale = scale; this.threshold = threshold; this.maxAmount = maxAmount;
        }
    }

    [Serializable]
    public class WorldGenSettings
    {
        [Header("Seed")]
        public int seed = 12345;

        [Header("Terrain (Simplex + FBM)")]
        public float noiseScale = 50f;
        [Range(1, 8)] public int octaves = 4;
        [Range(0f, 1f)] public float persistence = 0.5f;
        public float lacunarity = 2f;

        [Header("Terrain thresholds (0~1)")]
        public float deepWaterLevel = 0.2f;
        public float shallowWaterLevel = 0.3f;
        public float sandLevel = 0.35f;
        public float grassLevel = 0.6f;

        [Header("Ore (Cellular × Simplex mask, winner-take-all)")]
        [Tooltip("이 높이 이상(평지/숲)에서만 자원 생성")] public float oreMinHeight = 0.45f;
        [Tooltip("테두리를 깎는 심플렉스 노이즈의 스케일")] public float oreEdgeNoiseScale = 10f;
        [Range(0f, 1f), Tooltip("0 = 깎지 않음(원형), 1 = 완전히 노이즈에 따름")] public float oreNoiseBlend = 0.5f;
        public List<OreLayer> ores = new()
        {
            new OreLayer(OreType.Iron,   20f, 0.6f, 5000),
            new OreLayer(OreType.Copper, 20f, 0.6f, 4000),
            new OreLayer(OreType.Coal,   20f, 0.6f, 6000),
        };
    }

    public static class NoiseUtil
    {
        /// <summary>심플렉스 노이즈를 옥타브별로 겹친 FBM. 결과는 0~1로 정규화.</summary>
        public static float Fbm(float x, float y, float scale, int octaves, float persistence, float lacunarity, float2 seedOffset)
        {
            if (scale <= 0f) scale = 0.0001f;

            float total = 0f, amplitude = 1f, frequency = 1f, maxValue = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float2 p = new float2(x / scale * frequency, y / scale * frequency) + seedOffset;
                total += noise.snoise(p) * amplitude; // -1 ~ 1
                maxValue += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }
            return Mathf.Clamp01((total / maxValue + 1f) * 0.5f);
        }

        /// <summary>셀룰러(Worley) 노이즈의 F1 거리를 반전한 밀집도. 시드 포인트 중심 = 1, 가장자리 = 0.</summary>
        public static float CellularDensity(float x, float y, float scale, float2 seedOffset)
        {
            if (scale <= 0f) scale = 0.0001f;
            float2 p = new float2(x / scale, y / scale) + seedOffset;
            return math.saturate(1f - noise.cellular(p).x);
        }
    }

    /// <summary>
    /// 순수 함수형 청크 생성기. 같은 (설정, 시드, 좌표)에 대해 항상 같은 타일을 돌려준다.
    /// Unity 오브젝트를 만들지 않으므로 나중에 Job/Burst로 옮기기 쉽다.
    /// </summary>
    public sealed class WorldGenerator
    {
        private readonly WorldGenSettings s;
        private readonly float2 terrainOffset;
        private readonly float2 edgeOffset;
        private readonly float2[] oreOffsets;

        public WorldGenSettings Settings => s;

        public WorldGenerator(WorldGenSettings settings)
        {
            s = settings;
            terrainOffset = SeedOffset(settings.seed, 0);
            edgeOffset = SeedOffset(settings.seed, 1);
            oreOffsets = new float2[settings.ores.Count];
            for (int i = 0; i < oreOffsets.Length; i++) oreOffsets[i] = SeedOffset(settings.seed, i + 2);
        }

        /// <summary>
        /// (시드, 용도)별로 x/y가 독립인 샘플링 오프셋. x와 y에 같은 값을 더하면 시드가 대각선 평행이동에 불과해지므로 해시로 분리한다.
        /// 오프셋이 너무 크면 float 정밀도가 떨어져 노이즈가 뭉개지므로 0~1000 범위로 제한.
        /// </summary>
        private static float2 SeedOffset(int seed, int salt)
        {
            var rng = new Unity.Mathematics.Random(math.hash(new int2(seed, salt)) | 1u); // 상태 0 금지
            return rng.NextFloat2(new float2(0f), new float2(1000f));
        }

        public void Generate(ChunkData chunk)
        {
            var origin = chunk.WorldOrigin;
            var tiles = chunk.Tiles;
            for (int ly = 0; ly < ChunkData.Size; ly++)
            {
                int wy = origin.y + ly;
                for (int lx = 0; lx < ChunkData.Size; lx++)
                    tiles[ChunkData.Index(lx, ly)] = SampleTile(origin.x + lx, wy);
            }
            chunk.IsGenerated = true;
            chunk.IsDirty = true;
        }

        public TileCell SampleTile(int wx, int wy)
        {
            float h = NoiseUtil.Fbm(wx, wy, s.noiseScale, s.octaves, s.persistence, s.lacunarity, terrainOffset);

            var cell = new TileCell
            {
                terrain = ClassifyTerrain(h),
                height = (byte)Mathf.RoundToInt(h * 255f),
                ore = OreType.None,
                oreAmount = 0,
                entityId = 0,
            };

            if (h > s.oreMinHeight && s.ores.Count > 0)
            {
                // 테두리 마스크: 원형에 가까운 셀룰러 덩어리에 심플렉스를 곱해 가장자리를 불규칙하게 깎는다.
                float edgeScale = Mathf.Max(0.0001f, s.oreEdgeNoiseScale);
                float edge = noise.snoise(new float2(wx / edgeScale, wy / edgeScale) + edgeOffset) * 0.5f + 0.5f;
                float edgeMask = Mathf.Lerp(1f, edge, s.oreNoiseBlend);

                // 다중 자원 승자 독식: 모든 층의 값을 계산하고, 임계값을 넘는 것 중 가장 높은 하나만 채택.
                // → 군락 사이 경계가 보로노이 경계처럼 깔끔하게 분리된다.
                int best = -1;
                float bestValue = 0f, bestT = 0f;
                for (int i = 0; i < s.ores.Count; i++)
                {
                    var layer = s.ores[i];
                    // 인스펙터에서 층이 추가된 경우를 대비해 캐시 범위 밖이면 즉석 계산
                    float2 offset = i < oreOffsets.Length ? oreOffsets[i] : SeedOffset(s.seed, i + 2);
                    float v = NoiseUtil.CellularDensity(wx, wy, layer.scale, offset) * edgeMask;
                    if (v > layer.threshold && v > bestValue)
                    {
                        best = i;
                        bestValue = v;
                        bestT = (v - layer.threshold) / Mathf.Max(1e-4f, 1f - layer.threshold);
                    }
                }

                if (best >= 0)
                {
                    var layer = s.ores[best];
                    cell.ore = layer.type;
                    // 중심(bestT→1)일수록 매장량이 높고, 가장자리도 최소 20%는 보장
                    cell.oreAmount = (ushort)Mathf.Clamp(Mathf.RoundToInt(layer.maxAmount * (0.2f + 0.8f * bestT)), 1, ushort.MaxValue);
                }
            }

            return cell;
        }

        private TerrainKind ClassifyTerrain(float h)
        {
            if (h < s.deepWaterLevel) return TerrainKind.DeepWater;
            if (h < s.shallowWaterLevel) return TerrainKind.ShallowWater;
            if (h < s.sandLevel) return TerrainKind.Sand;
            if (h < s.grassLevel) return TerrainKind.Grass;
            return TerrainKind.Forest;
        }
    }
}
