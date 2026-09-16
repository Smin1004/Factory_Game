using System.Collections.Generic;
using UnityEngine;

namespace FactoryGame.Logistics
{
    /// <summary>벨트 위의 아이템. 위치는 세그먼트 시작(0)부터 끝(Length)까지의 진행도.</summary>
    public struct BeltItem
    {
        public int itemId;      // Registries.Items ID
        public float pos;       // 0 ~ Length
        public bool moving;     // 이번 틱에 온전히 한 스텝 이동했는지 (시각 보간용)
        public bool transferred;// 이번 틱에 다른 세그먼트에서 넘어왔는지 (이중 이동 방지)
    }

    /// <summary>
    /// 같은 방향으로 연결된 직선 벨트 묶음. 아이템은 타일이 아니라 세그먼트에 속하고
    /// 0~Length 사이의 float 진행도 하나만 가진다. 타일 수가 늘어나도 아이템당 갱신 비용은 동일하다.
    /// 아이템 목록은 항상 pos 내림차순(맨 앞 아이템이 [0])으로 정렬돼 있어, 앞 아이템만 보면 막힘을 판단할 수 있다.
    /// </summary>
    public sealed class ConveyorSegment
    {
        /// <summary>아이템 간 최소 간격(타일). 0.25 = 타일당 4개.</summary>
        public const float ItemSpacing = 0.25f;

        public int Id { get; internal set; }
        public Direction Dir { get; internal set; }

        /// <summary>Tiles[0] = 입구(tail), Tiles[^1] = 출구(head).</summary>
        public readonly List<Vector2Int> Tiles = new();
        public readonly List<BeltItem> Items = new();

        /// <summary>출구 앞 타일을 가진 세그먼트. 없으면 null (아이템이 끝에서 대기).</summary>
        public ConveyorSegment Next { get; internal set; }
        /// <summary>Next 세그먼트에서 아이템이 들어가는 진행도. 정면/코너 진입 0, 측면 투입은 타일 중앙(j + 0.5).</summary>
        public float NextEntryPos { get; internal set; }

        public float Length => Tiles.Count;
        public Vector2Int Tail => Tiles[0];
        public Vector2Int Head => Tiles[Tiles.Count - 1];

        public bool CanInsertAt(float pos)
        {
            if (pos < 0f || pos > Length) return false;
            for (int i = 0; i < Items.Count; i++)
                if (Mathf.Abs(Items[i].pos - pos) < ItemSpacing) return false;
            return true;
        }

        public bool TryInsert(int itemId, float pos, bool transferred = false)
        {
            if (!CanInsertAt(pos)) return false;
            int i = 0;
            while (i < Items.Count && Items[i].pos > pos) i++;
            Items.Insert(i, new BeltItem { itemId = itemId, pos = pos, moving = false, transferred = transferred });
            return true;
        }

        /// <summary>
        /// 앞 아이템부터 순서대로 전진. 각 아이템은 (앞 아이템 위치 - 간격)까지만 갈 수 있다.
        /// 맨 앞 아이템이 끝을 넘으면 Next로 넘기고, 못 넘기면 끝에서 대기한다.
        /// </summary>
        internal void Tick(float speed)
        {
            float ahead = Length; // 현재 아이템이 도달할 수 있는 최대 위치

            for (int i = 0; i < Items.Count; i++)
            {
                var it = Items[i];

                if (it.transferred)
                {
                    // 이번 틱에 이미 이전 세그먼트에서 이동해 들어온 아이템 — 한 번 더 움직이지 않는다.
                    // 플래그는 ConveyorNetwork.Tick 시작 시 일괄 해제된다 (세그먼트 순서와 무관하게 정확히 한 틱만 유효).
                    it.moving = false;
                    Items[i] = it;
                    ahead = it.pos - ItemSpacing;
                    continue;
                }

                float desired = it.pos + speed;

                if (i == 0 && desired > Length && Next != null)
                {
                    float over = desired - Length;
                    if (Next.TryInsert(it.itemId, NextEntryPos + over, transferred: true))
                    {
                        Items.RemoveAt(0);
                        i--;            // 다음 아이템이 새 [0]이 된다 (ahead는 Length 유지)
                        continue;
                    }
                }

                float target = Mathf.Min(desired, ahead);
                if (target < it.pos) target = it.pos; // 뒤로 밀리지는 않는다
                it.moving = (target - it.pos) >= speed - 1e-5f;
                it.pos = target;
                Items[i] = it;
                ahead = target - ItemSpacing;
            }
        }

        /// <summary>pos에서 maxDist 이내의 가장 가까운 아이템 인덱스. 없으면 -1. (플레이어 줍기용)</summary>
        public int IndexOfNearest(float pos, float maxDist)
        {
            int best = -1;
            float bestD = maxDist;
            for (int i = 0; i < Items.Count; i++)
            {
                float d = Mathf.Abs(Items[i].pos - pos);
                if (d <= bestD) { bestD = d; best = i; }
            }
            return best;
        }

        internal void ClearTransferFlags()
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (!Items[i].transferred) continue;
                var it = Items[i]; it.transferred = false; Items[i] = it;
            }
        }

        /// <summary>진행도 → 월드 좌표(타일 중심선 위). 시각 층용.</summary>
        public Vector2 ItemWorldPosition(float pos)
        {
            int count = Tiles.Count;
            if (count == 0) return Vector2.zero;
            int i = Mathf.Clamp(Mathf.FloorToInt(pos), 0, count - 1);
            float frac = pos - i;
            Vector2 d = Dir.ToVector();
            Vector2 center = new Vector2(Tiles[i].x + 0.5f, Tiles[i].y + 0.5f);
            return center + d * (frac - 0.5f);
        }
    }
}
