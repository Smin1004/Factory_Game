using System.Collections.Generic;
using UnityEngine;

namespace FactoryGame.Core
{
    /// <summary>고정 틱마다 호출되는 시뮬레이션 객체.</summary>
    public interface ITickable
    {
        void Tick(long tick);
    }

    /// <summary>
    /// Update()와 분리된 고정 시간 간격의 결정론적 틱 루프.
    /// 프레임 시간이 흔들려도 틱 간격은 항상 동일하므로, 같은 입력 순서 → 같은 시뮬레이션 결과.
    /// 렌더링 프레임이 느려지면 한 프레임에 여러 틱을 몰아서 돌리고(maxTicksPerFrame까지),
    /// 그래도 못 따라잡으면 남은 시간을 버려 스파이럴 오브 데스를 막는다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class TickSystem : MonoBehaviour
    {
        public static TickSystem Instance { get; private set; }

        [SerializeField, Min(1)] private int ticksPerSecond = 60;
        [SerializeField, Min(1), Tooltip("한 프레임에서 따라잡기 위해 실행할 최대 틱 수")] private int maxTicksPerFrame = 8;
        [SerializeField] private bool paused;

        public long CurrentTick { get; private set; }
        public int TicksPerSecond => ticksPerSecond;
        public float TickInterval => 1f / ticksPerSecond;
        /// <summary>마지막 틱 이후 경과 비율(0~1). 시각 층에서 위치 보간에 사용.</summary>
        public float Alpha { get; private set; }
        public bool Paused { get => paused; set => paused = value; }
        public int TickableCount => tickables.Count;

        private double accumulator;
        private readonly List<ITickable> tickables = new();
        private readonly List<ITickable> pendingAdd = new();
        private readonly List<ITickable> pendingRemove = new();
        private bool ticking;

        /// <summary>씬에 TickSystem이 없으면 자동 생성.</summary>
        public static TickSystem GetOrCreate()
        {
            if (Instance != null) return Instance;
            var found = FindFirstObjectByType<TickSystem>();
            if (found != null) { Instance = found; return found; }
            var go = new GameObject("TickSystem");
            return go.AddComponent<TickSystem>(); // Awake에서 Instance 설정
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>틱 도중 호출되어도 안전 (다음 틱부터 반영).</summary>
        public void Register(ITickable t)
        {
            if (t == null) return;
            if (ticking) pendingAdd.Add(t);
            else if (!tickables.Contains(t)) tickables.Add(t);
        }

        public void Unregister(ITickable t)
        {
            if (t == null) return;
            if (ticking) pendingRemove.Add(t);
            else tickables.Remove(t);
        }

        private void Update()
        {
            if (paused) { Alpha = 0f; return; }

            float interval = TickInterval;
            accumulator += Time.deltaTime;

            int steps = 0;
            while (accumulator >= interval && steps < maxTicksPerFrame)
            {
                Step();
                accumulator -= interval;
                steps++;
            }

            // 따라잡지 못한 시간은 버린다. 시뮬레이션이 실시간보다 느려질 뿐, 결과는 결정론적으로 유지된다.
            if (accumulator > interval) accumulator = interval;
            Alpha = (float)(accumulator / interval);
        }

        /// <summary>틱 1회 수행. 일시정지 중 수동 스텝(디버그)에도 사용.</summary>
        public void Step()
        {
            CurrentTick++;
            ticking = true;
            for (int i = 0; i < tickables.Count; i++) tickables[i].Tick(CurrentTick);
            ticking = false;

            if (pendingAdd.Count > 0)
            {
                foreach (var t in pendingAdd) if (!tickables.Contains(t)) tickables.Add(t);
                pendingAdd.Clear();
            }
            if (pendingRemove.Count > 0)
            {
                foreach (var t in pendingRemove) tickables.Remove(t);
                pendingRemove.Clear();
            }
        }
    }
}
