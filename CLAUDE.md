# Factory_Game — 프로젝트 인수인계 문서 (Claude Code용)

작성일: 2026-09-15. 이전 Cowork 세션에서 작업한 내용을 전부 정리한 문서. 새 세션은 이 문서를 먼저 읽을 것.

## 1. 프로젝트 개요

- Unity 2D(6000.0.66f2, URP 17.0.4, Unity.Mathematics 1.3.3, Input System 1.17.0 + 레거시 Input 병용) 기반 대규모 공장 자동화 시뮬레이션 게임. 레퍼런스는 팩토리오.
- 목표: 수만 개 객체와 무한 맵을 프레임 드랍 없이 처리하는 데이터 중심 아키텍처 + 절차적 맵 생성 최적화.
- 경로: `D:\project\Factory_Game` (git). 에디터는 `d:\unity\6000.0.66f2\editor\unity.exe` (같은 PC에 6000.4.0f1도 설치돼 있으나 이 프로젝트는 6000.0.66f2 사용).
- Windows 사용자명: `smin`.

## 2. 사용자 방침 (반드시 지킬 것)

- **기존 프로토타입 파일(`Assets/Script/MapGenerator.cs`, `MapVisualizer.cs`, `TileData.cs`)은 삭제·수정하지 않는다.** 비교용으로 남겨두기로 결정함. 새 코드는 별도 파일로만 추가.
- 새 코드는 전부 `FactoryGame.*` 네임스페이스. 전역 네임스페이스의 기존 타입(`TileData`, `TerrainType`, `NoiseGenerator`, `ChunkGenerator`, `ResourceGenerator`, `MapGenerator`, `MapVisualizer`)과 이름이 겹치지 않게 할 것. `UnityEngine.Terrain`과 충돌하므로 지형 enum은 `TerrainKind`.
- 응답/문서는 한국어.

## 3. 코드 구조 (2026-09-15 추가분, 모두 컴파일 통과 확인)

`Assets/Script/` 아래 네 폴더, 22개 파일. 2026-09-16에 `Gameplay/` 폴더, 메시 기반 타일 렌더링, 플레이어 이동/상호작용이 추가됨 (이 추가분은 컴파일·플레이 미확인).

렌더링/입력 구조 요약:
- 타일은 `TileSet`(WorldView 인스펙터, 종류별 색 + 스프라이트 변형 최대 4장) → `TileAtlas`(런타임 한 장) → 청크당 메시 1개(타일당 쿼드, 자원은 덮개 쿼드 추가). 스프라이트가 없으면 색으로 임시 타일을 굽는다. 청크 수와 무관하게 머티리얼 1개.
- 입력은 `GameInput`(Input System 장치 직접 읽기)으로 통일. 레거시 `Input`은 새 코드에서 쓰지 않는다.
- 플레이어 조작(WASD 이동, 좌클릭 설치/투입, 우클릭 홀드 채굴, R 회전, F 줍기, 1~9 핫바, 휠 줌)과 디버그 조작(F1 맵 튜닝 모드, F3 통계, P/O 틱)이 분리돼 있다. 튜닝 모드에서는 플레이어 입력이 꺼진다.

### Core/
| 파일 | 내용 |
|---|---|
| `TickSystem.cs` | `ITickable` 인터페이스 + 고정 간격(기본 60tps) 결정론적 틱 루프. 어큐뮬레이터 방식, 프레임당 최대 8틱 따라잡기, 초과분은 버림. `Alpha`(틱 사이 보간 비율), `Paused`, `Step()`. `TickSystem.GetOrCreate()`로 씬에 없으면 자동 생성. |
| `Registries.cs` | `IDefinition`, `DefinitionRegistry<T>`(문자열 키 ↔ 정수 ID, 0=None), `ItemDefinition`, `BuildingDefinition`, `EntityRegistry`(런타임 인스턴스 ID, 프리리스트 재활용, 세대 비트는 TODO), `Registries.Items/Buildings/Entities`, `Registries.EnsureDefaults()`(철광석/구리광석/석탄/컨베이어 아이템, 컨베이어 건물). `BuildingDefinition.ItemKey`(설치 시 소모·철거 시 반환되는 아이템), `Registries.BuildingForItem(itemId)`, 건물/아이템 추가 등록 후 `LinkBuildingItems()` 재호출 필요. |
| `RuntimeSprites.cs` | 아트 없는 요소용 런타임 생성 스프라이트(1×1 유닛, 피벗 중앙): `Square`, `Outline`(커서), `Player`(원 + 코). |

### World/
| 파일 | 내용 |
|---|---|
| `TileCell.cs` | `TerrainKind{DeepWater,ShallowWater,Sand,Grass,Forest}`, `OreType{None,Iron,Copper,Coal}`, **12바이트 순수 데이터 struct** `TileCell{terrain, ore, height(byte 0~255), flags, oreAmount(ushort), reserved, entityId(int)}`. 시각 참조·좌표 없음(플라이웨이트). |
| `ChunkData.cs` | 16×16, `TileCell[256]` 1차원 행우선 배열, `Shift=4`, `Mask=15`, `IsGenerated`, `IsDirty`, `WorldOrigin`, ref 반환 인덱서 `this[lx,ly]`. |
| `WorldGrid.cs` | `Dictionary<Vector2Int, ChunkData>` 희소 그리드. `WorldToChunk`는 `>> 4`, 로컬 좌표는 `& 15`(음수 좌표에서도 floor). `TryGetTile / TryGetTileRef / SetTile / SetEntityId / GetOrCreateChunk`. Unity 오브젝트 참조 없음. |
| `WorldGenerator.cs` | `OreLayer{type, scale, threshold, maxAmount}`, `WorldGenSettings`(seed, noiseScale 50, octaves 4, persistence 0.5, lacunarity 2, 지형 임계값 0.2/0.3/0.35/0.6, oreMinHeight 0.45, oreEdgeNoiseScale 10, oreNoiseBlend 0.5, ores 3종 scale 20 / threshold 0.6). `NoiseUtil.Fbm`(snoise FBM, 0~1 정규화), `NoiseUtil.CellularDensity`(cellular F1 반전). `WorldGenerator.Generate(chunk)`, `SampleTile(wx,wy)`: 자원은 **승자 독식**(모든 층 계산 후 임계값 초과 중 최대 하나) + 심플렉스 곱셈 테두리 마스크, 매장량은 중심일수록 높음(최소 20%). 시드 오프셋은 `SeedOffset(seed, salt)`(`math.hash` → `Unity.Mathematics.Random`, x/y 독립 `float2`, 0~1000): salt 0=지형, 1=자원 테두리 마스크, i+2=자원 층 i. (2026-09-15 수정: 예전엔 x/y에 같은 스칼라를 더해 시드가 대각선 평행이동에 불과했고, 테두리 마스크엔 시드가 없었음.) 순수 함수 → Job/Burst 이식 용이. |
| `WorldManager.cs` | 로직 진입점 싱글톤(`Instance`). `Grid`, `Generator`, `Conveyors`(ConveyorNetwork) 소유. 청크 생성 요청 큐 + 프레임당 예산(기본 8). `RequestChunk`, `GetChunkNow`, `TryGetTile`, `RegenerateAll()`, `ChunkGenerated` 이벤트. **벨트 설치/철거는 `TryPlaceBelt`/`RemoveBelt`를 거칠 것** — `IsBuildable` 검사 후 `BeltEntity`(타일당 1개)를 `Registries.Entities`에 등록하고 `TileCell.entityId`에 기록/해제한다. `ConveyorNetwork.PlaceBelt`를 직접 부르면 점유가 기록되지 않음. `IsWalkable(pos)`(미로드·깊은 물·벨트 아닌 건물은 통행 불가), `TryMineOre(pos, out itemId)`(매장량 1 감소, 0이면 자원 제거 + dirty), `OreItemId(OreType)`. Start에서 TickSystem에 Conveyors 등록. |
| `TileSet.cs` | `TileArt{color, sprites[]}`, `TileSet`(tileSize 기본 16, material 선택, 지형 5종 + 자원 3종 아트, 기본색은 예전 TilePalette 값), `TileAtlas`(행=종류 8, 열=변형 최대 4, RGBA32 한 장, 반 텍셀 인셋 UV. 스프라이트는 `tileSize` 정사각형 + 텍스처 Read/Write 필수, 아니면 경고 후 임시 타일). 임시 타일: 지형은 변형별 밝기 차 + 점 무늬, 자원은 투명 배경 위 덩어리. |
| `ChunkView.cs` | 청크 1개 = `Mesh` 1개 + `MeshFilter/MeshRenderer`. 1층 지형 쿼드 256개, 2층 자원 덮개 쿼드(HasOre 타일만). 변형은 `TileHash(wx, wy)`로 결정. 정적 공유 버퍼로 재구축, bounds 고정. `Init(atlas, material, sortingOrder)`, `Bind/Unbind/Refresh`, `IsDirty`일 때만 재구축. (예전 1px/타일 텍스처 방식은 삭제됨 — 미니맵이 필요하면 다시 만들 것) |
| `WorldView.cs` | Awake에서 `TileAtlas` + 머티리얼(URP 2D Sprite-Unlit-Default → Sprites/Default 순으로 탐색) 생성. 카메라 가시 범위(+chunkPadding)의 청크만 ChunkView를 붙이고 밖은 풀로 회수. 미생성 청크는 `RequestChunk`, 생성 완료 이벤트로 표시. **데이터는 화면 밖에서도 유지**. `terrainSortingOrder` 기본 0(벨트 10, 아이템 12, 플레이어 20, 커서 50). |
| `WorldDebugController.cs` | 디버그 전용(Input System). F1 맵 튜닝 모드 토글 — 켜면 `GameInput.PlayerInputEnabled=false`, `CameraFollow.Follow=false`. 튜닝 모드에서만: Q/W scale, E/R octaves, T/Y persistence, U/I lacunarity, Space 새 시드, G/H 자원 scale, J/K 자원 threshold, 방향키 자유 카메라. 항상: F3 통계 HUD 토글, P 틱 일시정지, O 한 틱 스텝. HUD는 화면 우상단. |

### Logistics/
| 파일 | 내용 |
|---|---|
| `Direction.cs` | `Direction{North,East,South,West}` + 확장(`ToVector`, `Opposite`, `RotateCW/CCW`, `ToAngleDeg`). |
| `ConveyorSegment.cs` | 같은 방향 직선 벨트 묶음. `Tiles`(tail→head), `Items`(**pos 내림차순**, `BeltItem{itemId, pos 0~Length, moving, transferred}`), `ItemSpacing=0.25`(타일당 4개), `Next`/`NextEntryPos`(정면·코너 진입 0, 측면 투입 j+0.5). `Tick(speed)`: 앞 아이템부터 전진, 끝 넘으면 `Next.TryInsert`, 막히면 대기. `transferred` 플래그로 한 틱 이중 이동 방지(네트워크 Tick 시작 시 `ClearTransferFlags`로 일괄 해제). `ItemWorldPosition(pos)`, `IndexOfNearest(pos, maxDist)`(줍기용). |
| `ConveyorNetwork.cs` | `ITickable`. `Speed=0.125` 타일/틱(60tps → 7.5타일/초). `PlaceBelt`(같은 방향 앞뒤 연결 시 병합/앞쪽 삽입, 방향 다르면 새 세그먼트 + Next 링크, 링 닫힘은 병합 안 함), `RemoveBelt`(세그먼트 분할, 아이템 재분배, 철거 타일 위 아이템 소실), `TryInsertItem`, `RelinkAround`/`RelinkFeeders`/`LinkNext`, `StructureVersion`. 세그먼트 처리 순서는 리스트 순서로 고정(결정론). `TryPeekItem/TryPickupItem(pos)`(타일 중앙 ±0.5의 가장 가까운 아이템). |
| `ConveyorDebugView.cs` | 런타임 생성 흰 스프라이트로 벨트(사각형+방향 표시)와 아이템(색은 `ItemDefinition.DebugColor`) 임시 시각화. `TickSystem.Alpha`로 보간(moving인 아이템만). |

### Gameplay/ (2026-09-16 추가, 네임스페이스 `FactoryGame.Gameplay`)
| 파일 | 내용 |
|---|---|
| `GameInput.cs` | 정적 입력 창구. `Keyboard/Mouse/Gamepad.current` 직접 읽기. `PlayerInputEnabled`(튜닝 모드에서 false), `Move`(WASD/방향키/왼쪽 스틱), `PlaceHeld/PlacePressed`(좌클릭), `MineHeld`(우클릭), `RotatePressed`(R), `PickupPressed`(F), `HotbarPressed`(1~9), `ZoomSteps`(휠, 항상 동작), `PointerScreenPosition`. 리바인딩이 필요해지면 InputAction 에셋으로 옮기되 이 클래스 API는 유지. |
| `Inventory.cs` | `ItemStack{itemId, count}`, 고정 슬롯 `Inventory`(순수 C#). `Add`(남은 수량 반환, 기존 스택 먼저), `Remove`, `TryTakeFromSlot`, `CanAdd/SpaceFor/CountOf`, `Changed` 이벤트. 스택 크기는 `ItemDefinition.StackSize`. |
| `PlayerController.cs` | 연속 이동(기본 6타일/초), AABB 반폭 0.3을 축별로 밀어 `WorldManager.IsWalkable` 타일에 막힘(0.4타일 단위로 서브스텝). 프레임 기반이며 틱과 무관. Start에서 위치가 풀/숲이 아니면 나선 탐색(반경 96)으로 이동, 필요한 청크는 `GetChunkNow`. `Body` 자식 스프라이트를 바라보는 방향으로 회전. `Inventory`는 지연 생성(18칸). |
| `PlayerInteraction.cs` | 커서 타일(`CursorTile`, `CursorInReach` 사거리 6), 핫바 선택(`SelectedSlot`, 같은 번호 재입력 시 해제), `BeltDir`. 좌클릭: 손이 건물 아이템이면 `TryPlaceBelt` 후 슬롯 1 소모(홀드 드래그 설치), 일반 아이템이면 커서 벨트에 `TryInsertItem`. 우클릭 홀드: 벨트는 0.25초 후 `RemoveBelt` + 컨베이어 아이템 반환, 자원은 1초마다 `TryMineOre`. 인벤토리가 가득 차면 진행 안 함. F: 커서 벨트 아이템 줍기. `OnGUI` 플레이어 HUD(좌상단 커서 정보/손/벨트 방향/진행률, 좌하단 핫바 9칸, 커서 아래 진행 바). 커서 시각은 `RuntimeSprites.Outline` + 방향 화살표(건물 들었을 때만). |
| `CameraFollow.cs` | Main Camera에 부착. `target`(씬에서 Player 트랜스폼 지정, 비면 PlayerController 탐색)을 지수 보간으로 추적, 휠 줌 4~40. `Follow=false`면 `Pan(dir)`로 자유 이동. 실행 순서 -10(WorldView보다 먼저). |

### 기존 프로토타입 (건드리지 말 것)
- `MapGenerator.cs`: 타일당 프리팹 Instantiate 방식, FBM/셀룰러/승자독식 최초 구현, 빈 `ChunkGenerator`/`ResourceGenerator` 클래스 포함.
- `MapVisualizer.cs`: 맵 전체를 Texture2D 하나로 그리고 코루틴으로 청크 열마다 yield.
- `TileData.cs`: `TerrainType` enum, GameObject/SpriteRenderer 참조가 들어 있는 struct(플라이웨이트 이전 상태).

## 4. 씬 설정 (YAML 직접 편집으로 적용, 플레이 검증은 아직)

`Assets/Scenes/SampleScene.unity` 구성 (fileID 19000000xx는 수동 편집분):
- Main Camera: Orthographic size **10**, `CameraFollow`(fileID 1900000010, target = Player 트랜스폼).
- **World**(1900000001~6): `WorldManager`, `WorldView`, `WorldDebugController`, `ConveyorDebugView`.
- **Player**(1900000020~23): 태그 Player, 위치 (8, 8), `PlayerController`, `PlayerInteraction`.
- MapGenerator(비활성), MapVisualizer(비활성), Global Light 2D, EventSystem.
- 필드는 전부 스크립트 기본값(YAML에 생략). 타일 스프라이트는 World → WorldView → Tile Set에 넣는다.
- 새 스크립트의 `.meta`는 2줄(fileFormatVersion + guid)로 직접 작성했고 씬에서 그 guid를 참조한다. 에디터가 다시 쓰더라도 guid는 유지된다.

아래는 최초 적용 절차의 기록.

1. 빈 GameObject `World` 생성 → `WorldManager`, `WorldView`, `WorldDebugController`, `ConveyorDebugView` 네 컴포넌트 추가 (TickSystem은 자동 생성, 카메라 필드는 비우면 `Camera.main`).
2. Main Camera는 Orthographic(2D 템플릿 기본).
3. 기존 `MapGenerator`, `MapVisualizer` 오브젝트는 원점에 겹쳐 그려지므로 **비활성화**(삭제하지 말 것).
4. 플레이 → 맵이 청크 단위로 스트리밍되고 HUD가 뜨는지, 벨트 설치/아이템 흐름이 되는지 확인.

## 5. Unity MCP 연결 상태 (진행 중)

- `Packages/manifest.json`에 `"com.unity.ai.assistant": "2.19.0-pre.2"` 추가 완료(6000.0.60f1 이상 지원). 패키지 설치·컴파일 완료, 툴바에 `AI` 메뉴 생김.
- Edit → Project Settings → AI → Unity MCP Server: Unity Bridge **Running**. 하지만 서드파티 약관 **Accept가 반영되지 않음** — "Account API did not become accessible within 30 seconds" 경고, 콘솔에 `UserNotInOrganization` 오류(generators.ai.unity.com PointsBalanceResult) 반복. 원인: Unity 계정 세션/조직 연결 문제. 중간에 "License returned" 대화창이 떴다가 "License added"로 자동 복구된 이력 있음.
- **사용자가 해야 할 일**: 에디터에서 Unity 계정 재로그인 → Project Settings → Services에서 조직에 프로젝트 연결 → Accept. 그 후 "Tools (7 of 54 enabled)" 목록에서 씬·스크립트·콘솔 편집 도구를 켜기로 합의됨(에셋 생성 AI 계열은 제외, 사용자가 "씬·스크립트·콘솔 편집 도구 전부" 선택).
- Claude 데스크톱 앱 설정: `C:\Users\smin\AppData\Roaming\Claude` 폴더가 존재하지 않았음. 설정 → 개발자 → 설정 편집으로 생성해서 아래를 넣고 앱 재시작 필요. (Claude Code에서는 `claude mcp add unity-mcp -- "C:\Users\smin\.unity\relay\relay_win.exe" --mcp` 또는 `.mcp.json`으로 동일하게 등록 가능.) 릴레이 바이너리는 Unity가 `%USERPROFILE%\.unity\relay\relay_win.exe`에 설치하며 `--mcp` 플래그 필수.
  ```json
  { "mcpServers": { "unity-mcp": { "command": "C:\\Users\\smin\\.unity\\relay\\relay_win.exe", "args": ["--mcp"] } } }
  ```
- 첫 연결 시 Unity에 "Pending Connection"이 뜨면 Project Settings → AI → Unity MCP에서 Accept로 승인.
- Claude Code용 Unity 공식 플러그인(스킬 29종 + Unity CLI)도 마켓플레이스에 있음(`unity`).

## 6. 남은 과제 (우선순위 순)

1. 씬 설정 후 실제 플레이 검증 (4절). 컴파일은 통과했지만 런타임은 아직 한 번도 안 돌려봄.
2. Unity MCP 연결 완료(5절) → 이후 씬 조작을 MCP로 직접.
3. 컨베이어: 순환(ring) 세그먼트 지원, 철거 시 아이템 바닥 드롭, 이중 이동 방지 로직의 단위 테스트, 벨트 위 플레이어 이동(현재는 걷기만 됨).
4. 건물 시스템: 채굴기(자원 타일 → 벨트에 `TryInsertItem`), 인서터. 설치 로직을 `PlayerInteraction`의 벨트 전용에서 `BuildingDefinition` 기반 일반 배치로 확장. 다중 타일 건물의 `IsWalkable` 처리.
4-1. 아트: 사용자가 타일 스프라이트를 제공하면 `TileSet`에 배치. 오토타일(지형 경계 전환), Pixel Perfect Camera, 미니맵(예전 1px/타일 방식 재구현).
4-2. UI: IMGUI HUD를 UGUI/UI Toolkit으로 교체, 전체 인벤토리 창.
5. `MapGenerator`/`MapVisualizer`와 중복되는 노이즈 코드는 새 구조가 검증되면 사용자와 상의 후 정리(임의 삭제 금지).
6. 청크 생성 Job/Burst 이식, 청크 언로드 정책, 저장/로드.
7. 성능 프로파일링: 청크 수·아이템 수 대비 메모리/틱 시간/프레임 타임 측정해 병목 확인.

## 7. 알려진 주의점

- 이 환경에서 컴파일러 없이 코드 리뷰로만 검증한 뒤 Unity에서 컴파일 통과를 확인함. 런타임 버그 가능성은 남아 있음.
- 새 코드는 전부 Input System(`GameInput`)을 쓴다. 기존 프로토타입(`MapGenerator`, `MapVisualizer`)이 레거시 `Input`을 쓰므로 Active Input Handling은 "Both"(현재 설정값 2) 유지.
- 타일 스프라이트를 넣을 때: 텍스처 임포트에서 Read/Write Enabled 켜기, Filter Mode Point, 크기는 `TileSet.tileSize`와 같은 정사각형. 자원 스프라이트는 투명 배경(지형 위에 덮어 그림).
- 지형 메시는 URP 2D `Sprite-Unlit-Default` 셰이더를 쓰므로 Global Light 2D의 영향을 받지 않는다. 플레이어/벨트/커서는 SpriteRenderer 기본 머티리얼(Lit).
- 플레이어 이동은 `Update` 프레임 기반이라 결정론 시뮬레이션(틱) 밖에 있다. 멀티플레이/리플레이가 필요해지면 틱으로 옮길 것.
- `RegenerateAll()`은 로드된 청크를 전부 다시 생성함 — 디버그 용도. `entityId`(건물 점유)는 보존하고, 물이 된 타일 위 벨트는 철거. 매장량(`oreAmount`)은 초기화됨.
- 시드 오프셋 방식이 바뀌어 같은 시드(12345)라도 이전과 다른 맵이 나옴.
- Unity 콘솔의 `UserNotInOrganization` / `PointsBalanceResult` 오류는 AI Assistant 포인트 조회 실패이며 게임 코드와 무관.
