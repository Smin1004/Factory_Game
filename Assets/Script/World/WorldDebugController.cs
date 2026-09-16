using FactoryGame.Core;
using FactoryGame.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FactoryGame.World
{
    /// <summary>
    /// 디버그 전용 입력/HUD (Input System 장치 직접 읽기).
    /// F1: 맵 튜닝 모드 토글 — 플레이어 조작이 멈추고 아래 키가 활성화된다.
    ///   지형: Q/W 노이즈 스케일, E/R 옥타브, T/Y persistence, U/I lacunarity, Space 새 시드
    ///   자원: G/H 군락 스케일, J/K 임계값
    ///   카메라: 방향키 자유 이동
    /// F3: 통계 HUD 토글. P: 틱 일시정지, O: 한 틱 스텝 (모드 무관).
    /// </summary>
    public sealed class WorldDebugController : MonoBehaviour
    {
        [SerializeField] private Camera cam;
        [SerializeField] private WorldView worldView;
        [SerializeField] private bool showStats = true;

        private CameraFollow follow;
        private GUIStyle hudStyle;

        /// <summary>맵 튜닝 모드 여부. 켜져 있으면 GameInput.PlayerInputEnabled가 꺼진다.</summary>
        public bool TuningMode { get; private set; }

        private void Awake()
        {
            SetTuningMode(false); // 도메인 리로드가 꺼진 환경에서 정적 상태가 남지 않도록
        }

        private void Start()
        {
            if (cam == null) cam = Camera.main;
            if (worldView == null) worldView = FindFirstObjectByType<WorldView>();
            if (cam != null) follow = cam.GetComponent<CameraFollow>();
        }

        private void Update()
        {
            var kb = Keyboard.current;
            var world = WorldManager.Instance;
            if (kb == null || world == null) return;

            if (kb.f1Key.wasPressedThisFrame) SetTuningMode(!TuningMode);
            if (kb.f3Key.wasPressedThisFrame) showStats = !showStats;
            HandleTick(kb);

            if (!TuningMode) return;
            HandleGenerationKeys(world, kb);
            HandlePan(kb);
        }

        private void SetTuningMode(bool on)
        {
            TuningMode = on;
            GameInput.PlayerInputEnabled = !on;
            if (follow != null) follow.Follow = !on;
        }

        private void HandleGenerationKeys(WorldManager world, Keyboard kb)
        {
            var s = world.GenSettings;
            bool regen = false;

            if (kb.qKey.wasPressedThisFrame) { s.noiseScale += 10f; regen = true; }
            if (kb.wKey.wasPressedThisFrame) { s.noiseScale = Mathf.Max(1f, s.noiseScale - 10f); regen = true; }
            if (kb.eKey.wasPressedThisFrame) { s.octaves = Mathf.Min(8, s.octaves + 1); regen = true; }
            if (kb.rKey.wasPressedThisFrame) { s.octaves = Mathf.Max(1, s.octaves - 1); regen = true; }
            if (kb.tKey.wasPressedThisFrame) { s.persistence = Mathf.Min(1f, s.persistence + 0.1f); regen = true; }
            if (kb.yKey.wasPressedThisFrame) { s.persistence = Mathf.Max(0f, s.persistence - 0.1f); regen = true; }
            if (kb.uKey.wasPressedThisFrame) { s.lacunarity += 0.5f; regen = true; }
            if (kb.iKey.wasPressedThisFrame) { s.lacunarity = Mathf.Max(0.5f, s.lacunarity - 0.5f); regen = true; }

            if (kb.gKey.wasPressedThisFrame) { foreach (var o in s.ores) o.scale += 5f; regen = true; }
            if (kb.hKey.wasPressedThisFrame) { foreach (var o in s.ores) o.scale = Mathf.Max(1f, o.scale - 5f); regen = true; }
            if (kb.jKey.wasPressedThisFrame) { foreach (var o in s.ores) o.threshold = Mathf.Min(1f, o.threshold + 0.05f); regen = true; }
            if (kb.kKey.wasPressedThisFrame) { foreach (var o in s.ores) o.threshold = Mathf.Max(0f, o.threshold - 0.05f); regen = true; }

            if (kb.spaceKey.wasPressedThisFrame) { s.seed = Random.Range(0, 65536); regen = true; }

            if (regen) world.RegenerateAll();
        }

        private void HandlePan(Keyboard kb)
        {
            Vector2 move = Vector2.zero;
            if (kb.leftArrowKey.isPressed) move.x -= 1f;
            if (kb.rightArrowKey.isPressed) move.x += 1f;
            if (kb.downArrowKey.isPressed) move.y -= 1f;
            if (kb.upArrowKey.isPressed) move.y += 1f;
            if (move == Vector2.zero || cam == null) return;

            move.Normalize();
            if (follow != null) follow.Pan(move);
            else cam.transform.position += (Vector3)(move * (1.5f * cam.orthographicSize * Time.deltaTime));
        }

        private void HandleTick(Keyboard kb)
        {
            var tick = TickSystem.Instance;
            if (tick == null) return;
            if (kb.pKey.wasPressedThisFrame) tick.Paused = !tick.Paused;
            if (kb.oKey.wasPressedThisFrame && tick.Paused) tick.Step();
        }

        private void OnGUI()
        {
            var world = WorldManager.Instance;
            if (world == null) return;
            hudStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true, alignment = TextAnchor.UpperRight };

            var tick = TickSystem.Instance;
            var net = world.Conveyors;
            string text = "";

            if (showStats)
            {
                text +=
                    $"<b>Chunks</b> loaded {world.Grid.ChunkCount}  pending {world.PendingChunkCount}" +
                    (worldView != null ? $"  views {worldView.ActiveViewCount} (+{worldView.PooledViewCount} pooled)" : "") + "\n" +
                    $"<b>Tick</b> {(tick != null ? tick.CurrentTick : 0)} {(tick != null && tick.Paused ? "[PAUSED]" : "")}  " +
                    $"<b>Belts</b> tiles {net.BeltTileCount}  segs {net.Segments.Count}  items {net.ItemCount}\n" +
                    $"<b>FPS</b> {1f / Mathf.Max(Time.unscaledDeltaTime, 1e-5f):F0}\n";
            }

            if (TuningMode)
            {
                var s = world.GenSettings;
                float oreScale = s.ores.Count > 0 ? s.ores[0].scale : 0f;
                float oreThr = s.ores.Count > 0 ? s.ores[0].threshold : 0f;
                text +=
                    "<color=#ffd166><b>[F1 맵 튜닝 모드]</b></color> 플레이어 조작 정지, 방향키로 카메라 이동\n" +
                    $"<b>Seed</b> {s.seed}  scale {s.noiseScale:F0}  oct {s.octaves}  pers {s.persistence:F1}  lac {s.lacunarity:F1}\n" +
                    $"<b>Ore</b> scale {oreScale:F0}  thr {oreThr:F2}\n" +
                    "Q/W scale  E/R oct  T/Y pers  U/I lac  Space seed  G/H ore scale  J/K ore thr";
            }
            else
            {
                text += "F1 맵 튜닝  F3 통계  P 일시정지  O 스텝";
            }

            GUI.Label(new Rect(Screen.width - 620, 10, 610, 160), text, hudStyle);
        }
    }
}
