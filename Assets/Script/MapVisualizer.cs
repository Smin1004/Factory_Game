using UnityEngine;
using Unity.Mathematics;
using System.Collections; // 코루틴 사용을 위해 필수!

public class MapVisualizer : MonoBehaviour
{
    TileData[,] testTile;

    [Header("Rendering")]
    public SpriteRenderer mapRenderer; 
    private Texture2D mapTexture; 

    [SerializeField] private Vector2Int size = new Vector2Int(10, 10); 
    private Vector2Int lastSize; 

    [Header("Terrain Settings")]
    public float noiseScale = 50f;
    public int noiseOctaves = 4;
    public float noisePersistence = 0.5f;
    public float noiseLacunarity = 2f;
    public float mapSeed = 12345f; 
    public int chunkSize = 16;

    [Header("Ore Settings")]
    public float oreScale = 20f; 
    public float oreThreshold = 0.6f; 
    public float oreNoiseBlend = 0.5f;

    // 현재 진행 중인 코루틴 추적용 (연속 입력 시 캔슬하기 위함)
    private Coroutine mapGenCoroutine;

    void Start()
    {
        InitializeMap();
    }

    void InitializeMap()
    {
        lastSize = size;
        int totalWidth = chunkSize * size.x;
        int totalHeight = chunkSize * size.y;

        testTile = new TileData[totalWidth, totalHeight];
        
        if (mapTexture != null) Destroy(mapTexture);
        mapTexture = new Texture2D(totalWidth, totalHeight);
        mapTexture.filterMode = FilterMode.Point; 
        mapTexture.wrapMode = TextureWrapMode.Clamp;

        mapRenderer.sprite = Sprite.Create(mapTexture, 
            new Rect(0, 0, totalWidth, totalHeight), 
            new Vector2(0.5f, 0.5f), 1f); 
        
        transform.position = Vector3.zero; 

        // 맵 생성 시작 (기존 코루틴이 있다면 정지하고 새로 시작)
        if (mapGenCoroutine != null) StopCoroutine(mapGenCoroutine);
        mapGenCoroutine = StartCoroutine(GenerateMapAsync(totalWidth, totalHeight));
    }

    void Update()
    {
        

        bool needsUpdate = false;
        if (Input.GetKeyDown(KeyCode.Q)) { noiseScale += 10f; needsUpdate = true; }
        if (Input.GetKeyDown(KeyCode.W)) { noiseScale -= 10f; needsUpdate = true; }
        if (Input.GetKeyDown(KeyCode.E)) { noiseOctaves += 1; needsUpdate = true; }
        if (Input.GetKeyDown(KeyCode.R)) { noiseOctaves -= 1; needsUpdate = true; }
        if (Input.GetKeyDown(KeyCode.Space)) {  needsUpdate = true; } //mapSeed = UnityEngine.Random.Range(0f, 99999f);

        if (needsUpdate)
        {
            if (size != lastSize)
        {
            InitializeMap();
        }

            int totalWidth = chunkSize * size.x;
            int totalHeight = chunkSize * size.y;

            if (mapGenCoroutine != null) StopCoroutine(mapGenCoroutine);
            mapGenCoroutine = StartCoroutine(GenerateMapAsync(totalWidth, totalHeight));
        }
    }

    // 핵심: 메인 스레드 정지를 막는 비동기 코루틴 연산
    IEnumerator GenerateMapAsync(int totalWidth, int totalHeight)
    {
        float halfWidth = totalWidth / 2f;
        float halfHeight = totalHeight / 2f;

        // SetPixel의 병목을 없애기 위한 1차원 색상 버퍼 배열
        Color[] colorBuffer = new Color[totalWidth * totalHeight];

        for (int i = 0; i < size.x; i++)
        {
            for (int j = 0; j < size.y; j++)
            {
                int startX = i * chunkSize;
                int startY = j * chunkSize;

                for (int x = 0; x < chunkSize; x++)
                {
                    for (int y = 0; y < chunkSize; y++)
                    {
                        int texX = startX + x;
                        int texY = startY + y;

                        float sampleX = texX - halfWidth;
                        float sampleY = texY - halfHeight;

                        float height = NoiseGenerator.GenerateFbmNoise(sampleX, sampleY, noiseScale, noiseOctaves, noisePersistence, noiseLacunarity, mapSeed);
                        TerrainType type = DetermineTerrainType(height);
                        Color pixelColor = GetTerrainColor(type);

                        // testTile 배열 저장 (데이터 로직)
                        testTile[texX, texY] = new TileData { terrain = type, heightValue = height };

                        // (float oreVal, int oreIdx) = OreValue(sampleX, sampleY);
                        // if (height > 0.45f && oreVal > oreThreshold) pixelColor = GetOreColor(oreIdx);

                        // SetPixel 대신 1차원 배열 인덱스에 색상 저장 (매우 빠름)
                        int arrayIndex = texY * totalWidth + texX;
                        colorBuffer[arrayIndex] = pixelColor;
                    }
                }
            }
            // 청크 세로 1줄을 다 연산할 때마다 메인 스레드에 휴식 시간 제공(프레임 드랍 방지)
            yield return null; 
        }

        // 모든 연산이 끝난 후, 버퍼에 담긴 10만 개의 색상을 단 한 번의 호출로 텍스처에 덮어씌움
        mapTexture.SetPixels(colorBuffer);
        mapTexture.Apply();
    }

    // --- 이하 보조 함수 (이전과 완전히 동일) ---
    private Color GetTerrainColor(TerrainType type) => type switch {
        TerrainType.DeepWater => new Color32(0,110,255,255), TerrainType.ShallowWater => new Color32(0,160,255,255),
        TerrainType.Sand => Color.yellow, TerrainType.Grass => new Color32(234,197,75,255),
        TerrainType.Forest => new Color32(0,198,35,255), _ => Color.black
    };
    private Color GetOreColor(int idx) => idx switch { 0 => new Color32(225,119, 52,255), 1 => new Color32(207,207,207,255), 2 => new Color32(65,65,65,255), _ => Color.white };
    
    private float GenerateCellularNoise(float x, float y, float sc, float seed) {
        float2 pos = new(x / sc + seed, y / sc + seed);
        float oreDensity = math.saturate(1f - noise.cellular(pos).x);
        float edge = noise.snoise(new float2(x / 10f, y / 10f)) * 0.5f + 0.5f;
        return oreDensity * Mathf.Lerp(1f, edge, oreNoiseBlend);
    }
    
    private (float, int) OreValue(float x, float y) {
        float maxV = 0; int bestI = 0;
        for (int i = 0; i < 3; i++) {
            float v = GenerateCellularNoise(x, y, oreScale, mapSeed + i);
            if (v > maxV) { maxV = v; bestI = i; }
        }
        return (maxV, bestI);
    }
    
    private TerrainType DetermineTerrainType(float h) {
        if (h < 0.2f) return TerrainType.DeepWater; if (h < 0.3f) return TerrainType.ShallowWater;
        if (h < 0.35f) return TerrainType.Sand; if (h < 0.60f) return TerrainType.Grass;
        return TerrainType.Forest;
    }
}