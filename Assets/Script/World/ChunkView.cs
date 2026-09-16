using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FactoryGame.World
{
    /// <summary>
    /// 청크 1개 = 메시 1개 + MeshRenderer 1개. 타일마다 쿼드 1개, 자원 타일은 덮개 쿼드 1개를 더 그린다.
    /// 타일 종류/변형은 아틀라스 UV로 표현하므로 타일당 오브젝트가 없고, 청크당 드로우콜 1개다.
    /// 데이터(ChunkData)를 참조만 하며, IsDirty가 켜졌을 때만 메시를 다시 만든다.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ChunkView : MonoBehaviour
    {
        public ChunkData Chunk { get; private set; }

        private Mesh mesh;
        private TileAtlas atlas;

        // 메시 빌드는 메인 스레드에서 순차 실행되므로 모든 뷰가 버퍼를 공유한다
        private static readonly List<Vector3> verts = new(ChunkData.Area * 8);
        private static readonly List<Vector2> uvs = new(ChunkData.Area * 8);
        private static readonly List<int> tris = new(ChunkData.Area * 12);
        private static readonly Bounds LocalBounds = new(
            new Vector3(ChunkData.Size * 0.5f, ChunkData.Size * 0.5f, 0f),
            new Vector3(ChunkData.Size, ChunkData.Size, 1f));

        public void Init(TileAtlas atlas, Material material, int sortingOrder)
        {
            this.atlas = atlas;
            mesh = new Mesh { name = "Chunk" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;

            var mr = GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.sortingOrder = sortingOrder;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }

        public void Bind(ChunkData chunk)
        {
            Chunk = chunk;
            var o = chunk.WorldOrigin;
            transform.position = new Vector3(o.x, o.y, 0f);
            Refresh();
        }

        public void Unbind() => Chunk = null;

        public void Refresh()
        {
            if (Chunk == null || atlas == null) return;
            verts.Clear(); uvs.Clear(); tris.Clear();

            var tiles = Chunk.Tiles;
            var origin = Chunk.WorldOrigin;

            // 1층: 지형
            for (int ly = 0; ly < ChunkData.Size; ly++)
                for (int lx = 0; lx < ChunkData.Size; lx++)
                {
                    ref var c = ref tiles[ChunkData.Index(lx, ly)];
                    uint h = TileHash(origin.x + lx, origin.y + ly);
                    int v = (int)(h % (uint)atlas.TerrainVariants(c.terrain));
                    AddQuad(lx, ly, atlas.TerrainUV(c.terrain, v));
                }

            // 2층: 자원 덮개 — 뒤에 추가된 삼각형이 위에 그려진다 (스프라이트 셰이더는 ZWrite Off)
            for (int ly = 0; ly < ChunkData.Size; ly++)
                for (int lx = 0; lx < ChunkData.Size; lx++)
                {
                    ref var c = ref tiles[ChunkData.Index(lx, ly)];
                    if (!c.HasOre) continue;
                    uint h = TileHash(origin.x + lx, origin.y + ly) >> 8;
                    int v = (int)(h % (uint)atlas.OreVariants(c.ore));
                    AddQuad(lx, ly, atlas.OreUV(c.ore, v));
                }

            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0, false);
            mesh.bounds = LocalBounds;
            Chunk.IsDirty = false;
        }

        private static void AddQuad(int x, int y, Rect uv)
        {
            int i = verts.Count;
            verts.Add(new Vector3(x, y, 0f));
            verts.Add(new Vector3(x + 1, y, 0f));
            verts.Add(new Vector3(x + 1, y + 1, 0f));
            verts.Add(new Vector3(x, y + 1, 0f));
            uvs.Add(new Vector2(uv.xMin, uv.yMin));
            uvs.Add(new Vector2(uv.xMax, uv.yMin));
            uvs.Add(new Vector2(uv.xMax, uv.yMax));
            uvs.Add(new Vector2(uv.xMin, uv.yMax));
            // 카메라(-Z)에서 볼 때 시계 방향
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
        }

        /// <summary>월드 좌표 → 결정론적 해시. 타일 변형 선택용.</summary>
        public static uint TileHash(int wx, int wy)
        {
            uint h = (uint)wx * 0x9E3779B1u ^ (uint)wy * 0x85EBCA77u;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12;
            return h;
        }
    }
}
