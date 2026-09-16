using FactoryGame.Core;
using FactoryGame.Logistics;
using FactoryGame.World;
using UnityEngine;

namespace FactoryGame.Gameplay
{
    /// <summary>
    /// 커서 타일과의 상호작용. 사거리(reach) 안에서만 동작한다.
    ///  - 좌클릭: 손에 든 것이 건물 아이템이면 설치(홀드 드래그 가능), 일반 아이템이면 커서 벨트 위에 올리기
    ///  - 우클릭(홀드): 채굴 — 벨트는 철거해 인벤토리로, 자원 타일은 1개씩 캐서 인벤토리로
    ///  - R 벨트 방향 회전, F 커서 벨트의 아이템 줍기, 1~9 핫바 슬롯 선택(같은 번호를 다시 누르면 손 비움)
    /// 현재 건물은 벨트뿐이다. 종류가 늘면 BuildingDefinition별 설치 로직으로 분기할 것.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerInteraction : MonoBehaviour
    {
        [SerializeField] private Camera cam;
        [SerializeField, Tooltip("상호작용 사거리(타일, 플레이어 중심 기준)")] private float reach = 6f;
        [SerializeField, Tooltip("자원 1개를 캐는 데 걸리는 시간(초)")] private float oreMiningTime = 1f;
        [SerializeField, Tooltip("벨트 한 칸을 철거하는 시간(초)")] private float beltMiningTime = 0.25f;
        [SerializeField, Tooltip("시작 시 지급할 컨베이어 수 (테스트용)")] private int startingConveyors = 100;
        [SerializeField] private int cursorSortingOrder = 50;
        [SerializeField] private Color inReachColor = new(0.35f, 1f, 0.35f, 0.9f);
        [SerializeField] private Color outOfReachColor = new(1f, 0.35f, 0.35f, 0.6f);

        private PlayerController player;
        private WorldManager world;
        private Inventory inventory;
        private SpriteRenderer cursor, cursorArrow;
        private GUIStyle labelStyle, slotStyle, slotSelectedStyle;
        private int conveyorItemId;

        // 채굴 진행 상태
        private Vector2Int miningTile;
        private float miningProgress, miningTime = 1f;
        private string miningLabel = "";
        private string status = "";
        private float statusUntil;

        public Vector2Int CursorTile { get; private set; }
        public bool CursorInReach { get; private set; }
        public Direction BeltDir { get; private set; } = Direction.East;
        /// <summary>선택된 핫바 슬롯(0~8). 없으면 -1.</summary>
        public int SelectedSlot { get; private set; } = -1;
        public int HandItemId => SelectedSlot >= 0 ? inventory.Slots[SelectedSlot].itemId : 0;

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            inventory = player.Inventory;

            // 커서는 정사각형 테두리라 벨트 방향으로 돌려도 모양이 같다 → 화살표만 자식으로 붙여 같이 돌린다
            cursor = MakeSprite("TileCursor", RuntimeSprites.Outline, transform, cursorSortingOrder);
            cursorArrow = MakeSprite("BeltDirection", RuntimeSprites.Square, cursor.transform, cursorSortingOrder + 1);
            cursorArrow.transform.localPosition = new Vector3(0.22f, 0f, 0f);
            cursorArrow.transform.localScale = new Vector3(0.3f, 0.5f, 1f);
        }

        private static SpriteRenderer MakeSprite(string name, Sprite sprite, Transform parent, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        private void Start()
        {
            if (cam == null) cam = Camera.main;
            world = WorldManager.Instance;

            var conveyor = Registries.Buildings.Get("conveyor");
            conveyorItemId = conveyor?.ItemKey != null && Registries.Items.TryGetId(conveyor.ItemKey, out int id) ? id : 0;
            if (conveyorItemId != 0 && startingConveyors > 0)
            {
                inventory.Add(conveyorItemId, startingConveyors);
                SelectedSlot = 0;
            }
        }

        private void Update()
        {
            if (world == null || cam == null) return;
            UpdateCursor();

            if (!GameInput.PlayerInputEnabled) { miningProgress = 0f; return; }

            int hotbar = GameInput.HotbarPressed;
            if (hotbar > 0)
            {
                int slot = hotbar - 1;
                SelectedSlot = SelectedSlot == slot ? -1 : slot;
            }
            if (GameInput.RotatePressed) BeltDir = BeltDir.RotateCW();

            HandlePlace();
            HandleMine();
            HandlePickup();
        }

        private void UpdateCursor()
        {
            Vector3 wp = cam.ScreenToWorldPoint(GameInput.PointerScreenPosition);
            CursorTile = new Vector2Int(Mathf.FloorToInt(wp.x), Mathf.FloorToInt(wp.y));
            Vector2 center = new(CursorTile.x + 0.5f, CursorTile.y + 0.5f);
            CursorInReach = (center - player.Position).sqrMagnitude <= reach * reach;

            cursor.transform.position = new Vector3(center.x, center.y, 0f);
            cursor.transform.rotation = Quaternion.Euler(0f, 0f, BeltDir.ToAngleDeg());
            cursor.color = CursorInReach ? inReachColor : outOfReachColor;

            bool showArrow = Registries.BuildingForItem(HandItemId) != null;
            if (cursorArrow.gameObject.activeSelf != showArrow) cursorArrow.gameObject.SetActive(showArrow);
            cursorArrow.color = cursor.color;
        }

        private void HandlePlace()
        {
            int hand = HandItemId;
            if (hand == 0 || !CursorInReach) return;

            if (Registries.BuildingForItem(hand) != null)
            {
                // 홀드 중 커서가 지나가는 칸마다 설치 (드래그 설치). 이미 벨트가 있으면 조용히 건너뛴다.
                if (!GameInput.PlaceHeld || world.Conveyors.HasBelt(CursorTile)) return;
                if (world.TryPlaceBelt(CursorTile, BeltDir)) inventory.TryTakeFromSlot(SelectedSlot, 1);
                else if (GameInput.PlacePressed) Status("여기에는 설치할 수 없습니다");
            }
            else if (GameInput.PlacePressed && world.Conveyors.HasBelt(CursorTile))
            {
                if (world.Conveyors.TryInsertItem(CursorTile, hand)) inventory.TryTakeFromSlot(SelectedSlot, 1);
                else Status("벨트에 빈자리가 없습니다");
            }
        }

        private void HandleMine()
        {
            if (!GameInput.MineHeld || !CursorInReach) { miningProgress = 0f; return; }

            bool isBelt = world.Conveyors.HasBelt(CursorTile);
            bool hasTile = world.TryGetTile(CursorTile, out var tile);
            bool isOre = !isBelt && hasTile && tile.HasOre;
            if (!isBelt && !isOre) { miningProgress = 0f; return; }

            if (CursorTile != miningTile) { miningTile = CursorTile; miningProgress = 0f; }
            miningTime = isBelt ? beltMiningTime : oreMiningTime;
            miningLabel = isBelt ? "철거" : "채굴";

            int itemId = isBelt ? conveyorItemId : world.OreItemId(tile.ore);
            if (itemId != 0 && !inventory.CanAdd(itemId, 1))
            {
                miningProgress = 0f;
                Status("인벤토리가 가득 찼습니다");
                return;
            }

            miningProgress += Time.deltaTime;
            if (miningProgress < miningTime) return;
            miningProgress = 0f;

            if (isBelt)
            {
                if (world.RemoveBelt(CursorTile)) inventory.Add(itemId, 1);
            }
            else if (world.TryMineOre(CursorTile, out int mined))
            {
                inventory.Add(mined, 1);
            }
        }

        private void HandlePickup()
        {
            if (!GameInput.PickupPressed || !CursorInReach) return;
            // 꺼내기 전에 넣을 자리를 확인하지 않으면 아이템이 사라진다
            if (!world.Conveyors.TryPeekItem(CursorTile, out int itemId)) return;
            if (!inventory.CanAdd(itemId, 1)) { Status("인벤토리가 가득 찼습니다"); return; }
            if (world.Conveyors.TryPickupItem(CursorTile, out itemId)) inventory.Add(itemId, 1);
        }

        private void Status(string message)
        {
            status = message;
            statusUntil = Time.time + 2f;
        }

        private void OnGUI()
        {
            if (world == null || cam == null) return;
            labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
            slotStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 12, alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = true };
            slotSelectedStyle ??= new GUIStyle(slotStyle) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.3f) } };

            // 커서 타일 정보
            string tileInfo = "(미로드)";
            if (world.TryGetTile(CursorTile, out var cell))
                tileInfo = $"{cell.terrain} h={cell.Height01:F2}"
                    + (cell.HasOre ? $"  {cell.ore} ×{cell.oreAmount}" : "")
                    + (cell.IsOccupied ? $"  entity#{cell.entityId}" : "");
            if (world.Conveyors.TryGetSegment(CursorTile, out var seg))
                tileInfo += $"  belt seg#{seg.Id} {seg.Dir} items {seg.Items.Count}";

            var handDef = Registries.Items.Get(HandItemId);
            string handText = handDef != null ? $"{handDef.DisplayName} ×{inventory.Slots[SelectedSlot].count}" : "(비어 있음)";

            string text =
                $"<b>커서</b> {CursorTile}  {tileInfo}" + (CursorInReach ? "" : "  <color=#ff6b6b>[사거리 밖]</color>") + "\n" +
                $"<b>손</b> {handText}   <b>벨트 방향</b> {BeltDir} (R)\n" +
                (miningProgress > 0f ? $"<b>{miningLabel}</b> {miningProgress / miningTime * 100f:F0}%\n" : "") +
                (Time.time < statusUntil ? $"<color=#ffd166>{status}</color>" : "");
            GUI.Label(new Rect(10, 10, 720, 100), text, labelStyle);

            // 채굴 진행 바 (커서 타일 아래)
            if (miningProgress > 0f)
            {
                Vector3 sp = cam.WorldToScreenPoint(new Vector3(CursorTile.x + 0.5f, CursorTile.y - 0.15f, 0f));
                const float w = 48f, h = 7f;
                var r = new Rect(sp.x - w * 0.5f, Screen.height - sp.y, w, h);
                GUI.Box(r, GUIContent.none);
                GUI.color = new Color(0.4f, 1f, 0.4f);
                GUI.DrawTexture(new Rect(r.x + 1f, r.y + 1f, (w - 2f) * Mathf.Clamp01(miningProgress / miningTime), h - 2f), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            // 핫바 (1~9 = 슬롯 0~8)
            const float slotW = 76f, slotH = 48f, gap = 4f;
            float x0 = 10f, y0 = Screen.height - slotH - 10f;
            for (int i = 0; i < 9 && i < inventory.Capacity; i++)
            {
                var s = inventory.Slots[i];
                var def = Registries.Items.Get(s.itemId);
                string label = def != null ? $"{i + 1}  {def.DisplayName}\n×{s.count}" : $"{i + 1}\n-";
                GUI.Box(new Rect(x0 + i * (slotW + gap), y0, slotW, slotH), label, i == SelectedSlot ? slotSelectedStyle : slotStyle);
            }
        }
    }
}
