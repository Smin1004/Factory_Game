using System.Collections.Generic;
using FactoryGame.Core;
using UnityEngine;

namespace FactoryGame.Logistics
{
    /// <summary>벨트 타일 1칸의 엔티티. TileCell.entityId로 점유를 표시하기 위해 EntityRegistry에 등록된다.</summary>
    public sealed class BeltEntity
    {
        public readonly Vector2Int Position;
        public BeltEntity(Vector2Int position) { Position = position; }
    }

    /// <summary>
    /// 벨트 타일의 설치/철거를 세그먼트 병합/분할로 변환하고, 틱마다 모든 세그먼트를 전진시킨다.
    /// 규칙:
    ///  - 같은 방향으로 앞뒤가 이어지면 하나의 세그먼트로 병합 (직선)
    ///  - 방향이 바뀌면 새 세그먼트가 되고 Next 링크로 연결 (코너)
    ///  - 다른 세그먼트의 옆구리로 들어가면 측면 투입(side-load)
    ///  - 중간 타일 철거 시 세그먼트를 둘로 분할
    /// 순수 C# 데이터 — 시각은 ConveyorDebugView가 담당.
    /// </summary>
    public sealed class ConveyorNetwork : ITickable
    {
        /// <summary>타일/틱. 60tps 기준 0.125 = 초당 7.5타일.</summary>
        public float Speed = 0.125f;

        private readonly Dictionary<Vector2Int, ConveyorSegment> tileToSegment = new();
        private readonly List<ConveyorSegment> segments = new();
        private int nextSegmentId = 1;

        /// <summary>구조(타일 집합)가 바뀔 때마다 증가. 시각 층이 재구축 여부를 판단하는 데 사용.</summary>
        public int StructureVersion { get; private set; }
        public IReadOnlyList<ConveyorSegment> Segments => segments;
        public int BeltTileCount => tileToSegment.Count;

        public int ItemCount
        {
            get { int n = 0; for (int i = 0; i < segments.Count; i++) n += segments[i].Items.Count; return n; }
        }

        public bool HasBelt(Vector2Int pos) => tileToSegment.ContainsKey(pos);
        public bool TryGetSegment(Vector2Int pos, out ConveyorSegment seg) => tileToSegment.TryGetValue(pos, out seg);

        public IEnumerable<KeyValuePair<Vector2Int, ConveyorSegment>> BeltTiles => tileToSegment;

        // ---------------- 설치 / 철거 ----------------

        public bool PlaceBelt(Vector2Int pos, Direction dir)
        {
            if (tileToSegment.ContainsKey(pos)) return false;

            Vector2Int d = dir.ToVector();
            tileToSegment.TryGetValue(pos - d, out var behind);
            tileToSegment.TryGetValue(pos + d, out var front);

            // 같은 방향 세그먼트의 머리 바로 앞 / 꼬리 바로 뒤에 놓이면 직선 연결
            bool joinBehind = behind != null && behind.Dir == dir && behind.Head == pos - d;
            bool joinFront = front != null && front.Dir == dir && front.Tail == pos + d;

            ConveyorSegment seg;
            if (joinBehind)
            {
                seg = behind;
                seg.Tiles.Add(pos);
                // front == behind 이면 고리(ring)가 닫히는 경우 — 자기 자신과 병합하지 않는다. TODO: 순환 세그먼트 지원
                if (joinFront && front != seg) Merge(seg, front);
            }
            else if (joinFront)
            {
                seg = front;
                seg.Tiles.Insert(0, pos);
                ShiftPositions(seg, 1f);
            }
            else
            {
                seg = new ConveyorSegment { Id = nextSegmentId++, Dir = dir };
                seg.Tiles.Add(pos);
                segments.Add(seg);
            }

            tileToSegment[pos] = seg;
            RelinkAround(pos);
            RelinkFeeders(seg); // 꼬리가 바뀌거나 병합되면 기존 피더의 진입점(코너↔측면)이 달라질 수 있다
            StructureVersion++;
            return true;
        }

        public bool RemoveBelt(Vector2Int pos)
        {
            if (!tileToSegment.TryGetValue(pos, out var seg)) return false;

            int k = seg.Tiles.IndexOf(pos);
            tileToSegment.Remove(pos);

            // 뒤쪽 조각 [0..k) 은 기존 세그먼트가 유지, 앞쪽 조각 (k..end] 은 새 세그먼트로 분리
            int frontCount = seg.Tiles.Count - k - 1;
            ConveyorSegment frontSeg = null;
            if (frontCount > 0)
            {
                frontSeg = new ConveyorSegment { Id = nextSegmentId++, Dir = seg.Dir };
                frontSeg.Tiles.AddRange(seg.Tiles.GetRange(k + 1, frontCount));
                foreach (var t in frontSeg.Tiles) tileToSegment[t] = frontSeg;
                frontSeg.Next = seg.Next;
                frontSeg.NextEntryPos = seg.NextEntryPos;
                segments.Add(frontSeg);
            }
            seg.Tiles.RemoveRange(k, seg.Tiles.Count - k);

            // 아이템 재분배. 철거된 타일 위(k ~ k+1)의 아이템은 소실된다. TODO: 바닥에 드롭
            float cut = k + 1;
            var keep = new List<BeltItem>(seg.Items.Count);
            foreach (var it in seg.Items)
            {
                if (it.pos <= k) keep.Add(it);
                else if (it.pos >= cut && frontSeg != null)
                {
                    var moved = it; moved.pos -= cut;
                    frontSeg.Items.Add(moved); // 원래 정렬(내림차순)이 유지된다
                }
            }
            seg.Items.Clear();
            seg.Items.AddRange(keep);

            // 이 세그먼트로 들어오던 피더들의 진입점 재배정
            foreach (var s in segments)
            {
                if (s.Next != seg) continue;
                if (s.NextEntryPos >= cut && frontSeg != null) { s.Next = frontSeg; s.NextEntryPos -= cut; }
                else if (s.NextEntryPos > k) { s.Next = null; s.NextEntryPos = 0f; }
            }

            if (seg.Tiles.Count == 0)
            {
                segments.Remove(seg);
                foreach (var s in segments) if (s.Next == seg) { s.Next = null; s.NextEntryPos = 0f; }
            }

            RelinkAround(pos);
            StructureVersion++;
            return true;
        }

        // ---------------- 아이템 ----------------

        /// <summary>해당 타일 중앙에 아이템을 올린다 (채굴기/인서터 출력용 진입점).</summary>
        public bool TryInsertItem(Vector2Int pos, int itemId)
        {
            if (!tileToSegment.TryGetValue(pos, out var seg)) return false;
            int k = seg.Tiles.IndexOf(pos);
            return seg.TryInsert(itemId, k + 0.5f);
        }

        /// <summary>타일 위(중앙 ±0.5)에 있는 가장 가까운 아이템을 꺼내지 않고 확인만 한다.</summary>
        public bool TryPeekItem(Vector2Int pos, out int itemId)
        {
            itemId = 0;
            if (!tileToSegment.TryGetValue(pos, out var seg)) return false;
            int i = seg.IndexOfNearest(seg.Tiles.IndexOf(pos) + 0.5f, 0.5f);
            if (i < 0) return false;
            itemId = seg.Items[i].itemId;
            return true;
        }

        /// <summary>타일 위(중앙 ±0.5)에 있는 가장 가까운 아이템을 꺼낸다 (플레이어 줍기).</summary>
        public bool TryPickupItem(Vector2Int pos, out int itemId)
        {
            itemId = 0;
            if (!tileToSegment.TryGetValue(pos, out var seg)) return false;
            int i = seg.IndexOfNearest(seg.Tiles.IndexOf(pos) + 0.5f, 0.5f);
            if (i < 0) return false;
            itemId = seg.Items[i].itemId;
            seg.Items.RemoveAt(i);
            return true;
        }

        // ---------------- 틱 ----------------

        public void Tick(long tick)
        {
            // 1) 지난 틱에 넘어온 아이템의 플래그 해제 (세그먼트 처리 순서와 무관하게 정확히 한 틱만 유효)
            for (int i = 0; i < segments.Count; i++) segments[i].ClearTransferFlags();
            // 2) 세그먼트 순서는 리스트 순서(설치 순서)로 고정 → 같은 조작 순서면 결과도 같다
            for (int i = 0; i < segments.Count; i++) segments[i].Tick(Speed);
        }

        // ---------------- 내부 ----------------

        /// <summary>a의 머리 뒤에 b를 이어 붙인다. b는 사라진다.</summary>
        private void Merge(ConveyorSegment a, ConveyorSegment b)
        {
            float offset = a.Length;
            a.Tiles.AddRange(b.Tiles);
            foreach (var t in b.Tiles) tileToSegment[t] = a;

            // b의 아이템은 offset만큼 앞으로 밀리며, a의 아이템보다 앞에 있으므로 리스트 앞쪽에 온다
            var merged = new List<BeltItem>(a.Items.Count + b.Items.Count);
            foreach (var it in b.Items) { var m = it; m.pos += offset; merged.Add(m); }
            merged.AddRange(a.Items);
            a.Items.Clear();
            a.Items.AddRange(merged);

            a.Next = b.Next;
            a.NextEntryPos = b.NextEntryPos;

            foreach (var s in segments)
                if (s.Next == b) { s.Next = a; s.NextEntryPos += offset; }

            segments.Remove(b);
        }

        /// <summary>세그먼트 앞에 타일이 추가됐을 때 아이템·피더 진입점을 밀어준다.</summary>
        private void ShiftPositions(ConveyorSegment seg, float delta)
        {
            for (int i = 0; i < seg.Items.Count; i++)
            {
                var it = seg.Items[i]; it.pos += delta; seg.Items[i] = it;
            }
            foreach (var s in segments)
                if (s.Next == seg) s.NextEntryPos += delta;
        }

        /// <summary>seg로 들어오는 모든 세그먼트의 링크를 다시 계산.</summary>
        private void RelinkFeeders(ConveyorSegment seg)
        {
            for (int i = 0; i < segments.Count; i++)
                if (segments[i].Next == seg) LinkNext(segments[i]);
        }

        /// <summary>pos와 그 4방 이웃을 가진 세그먼트들의 Next 링크를 다시 계산.</summary>
        private void RelinkAround(Vector2Int pos)
        {
            if (tileToSegment.TryGetValue(pos, out var own)) LinkNext(own);
            for (int d = 0; d < 4; d++)
            {
                var nb = pos + ((Direction)d).ToVector();
                if (tileToSegment.TryGetValue(nb, out var s)) LinkNext(s);
            }
        }

        private void LinkNext(ConveyorSegment seg)
        {
            Vector2Int front = seg.Head + seg.Dir.ToVector();
            if (tileToSegment.TryGetValue(front, out var n) && n != seg && n.Dir != seg.Dir.Opposite())
            {
                int j = n.Tiles.IndexOf(front);
                seg.Next = n;
                // 꼬리 타일로 들어가면 코너(정면 진입, 0), 중간 타일이면 측면 투입(타일 중앙)
                seg.NextEntryPos = j == 0 ? 0f : j + 0.5f;
            }
            else
            {
                seg.Next = null;
                seg.NextEntryPos = 0f;
            }
        }
    }
}
