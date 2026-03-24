using UnityEngine;
using Unity.Mathematics; // 패키지 매니저에서 Mathematics 설치 필요

public class FlyweightPattern : MonoBehaviour
{
    TileData[,] testData;
    SpriteRenderer[,] tileObjects;
    [SerializeField] private GameObject testWater; // 타일 프리팹
    [SerializeField] private GameObject testSand; // 타일 프리팹

    [SerializeField] private Vector2Int size; // 타일 프리팹

    void Start()
    {
        tileObjects = new SpriteRenderer[chunkSize * size.x, chunkSize * size.y];

        for (int i = 0; i < size.x; i++)
            {
                for (int j = 0; j < size.y; j++)
                {
                    testData = GenerateChunkData(new Vector2Int(i, j));
                }
            }
    }

    void Update()
    {
        for (int i = 0; i < size.x; i++)
            {
                for (int j = 0; j < size.y; j++)
                {
                    ReColor(new Vector2Int(i, j));
                }
            }

        if(Input.GetKeyDown(KeyCode.Q)) noiseScale += 10f;
        if(Input.GetKeyDown(KeyCode.W)) noiseScale -= 10f;

        if(Input.GetKeyDown(KeyCode.E)) noiseOctaves += 1;
        if(Input.GetKeyDown(KeyCode.R)) noiseOctaves -= 1;

        if(Input.GetKeyDown(KeyCode.T)) noisePersistence += 0.1f;
        if(Input.GetKeyDown(KeyCode.Y)) noisePersistence -= 0.1f;

        if(Input.GetKeyDown(KeyCode.U)) noiseLacunarity += 0.5f;
        if(Input.GetKeyDown(KeyCode.I)) noiseLacunarity -= 0.5f;

        if(Input.GetKeyDown(KeyCode.Space)) mapSeed = UnityEngine.Random.Range(0f, 99999f);
    }

    public float noiseScale = 50f;
    public int noiseOctaves = 4;
    public float noisePersistence = 0.5f;
    public float noiseLacunarity = 2f;
    public float mapSeed = 12345f; // 랜덤 시드

    public int chunkSize = 16;

    void ReColor(Vector2Int chunkCoordinate)
    {
        float startWorldX = chunkCoordinate.x * chunkSize;
        float startWorldY = chunkCoordinate.y * chunkSize;

        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                float worldX = startWorldX + x;
                float worldY = startWorldY + y;

                float currentHeight = NoiseGenerator.GenerateFbmNoise
                (worldX, worldY, noiseScale, noiseOctaves, noisePersistence, noiseLacunarity, mapSeed);

                // 노이즈 값에 따라 지형 결정
                TerrainType currentTerrain = DetermineTerrainType(currentHeight);

                tileObjects[(int)worldX, (int)worldY].color = currentTerrain switch
                {
                    TerrainType.DeepWater => Color.blue,
                    TerrainType.ShallowWater => Color.cyan,
                    TerrainType.Sand => Color.yellow,
                    TerrainType.Grass => Color.gray,
                    TerrainType.Forest => Color.green,
                    _ => throw new System.NotImplementedException()
                };
            }
        }
    }

    // 특정 청크 좌표(예: 0,0 또는 1,0)에 대한 타일 데이터를 생성하여 반환
    public TileData[,] GenerateChunkData(Vector2Int chunkCoordinate)
    {
        TileData[,] chunkTiles = new TileData[chunkSize, chunkSize];

        // 청크의 실제 월드 시작 좌표 계산
        float startWorldX = chunkCoordinate.x * chunkSize;
        float startWorldY = chunkCoordinate.y * chunkSize;

        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                // 각 타일의 절대적인 월드 좌표
                float worldX = startWorldX + x;
                float worldY = startWorldY + y;

                // FBM 노이즈 값 추출 (0.0 ~ 1.0)
                float currentHeight = NoiseGenerator.GenerateFbmNoise
                (worldX, worldY, noiseScale, noiseOctaves, noisePersistence, noiseLacunarity, mapSeed);

                // 노이즈 값에 따라 지형 결정
                TerrainType currentTerrain = DetermineTerrainType(currentHeight);

                // 타일 데이터 저장
                chunkTiles[x, y] = new TileData
                {
                    terrain = currentTerrain,
                    heightValue = currentHeight
                };
                //Debug.Log($"Tile at ({x}, {y}): {currentTerrain}, Height: {currentHeight} / World Pos: ({worldX}, {worldY})");
                    
                if (tileObjects[(int)worldX, (int)worldY] == null)
                {
                    tileObjects[(int)worldX, (int)worldY] = Instantiate(testSand, new Vector3(worldX, worldY, 0), Quaternion.identity, this.transform).GetComponent<SpriteRenderer>();
                }

                tileObjects[(int)worldX, (int)worldY].color = currentTerrain switch
                {
                    TerrainType.DeepWater => Color.blue,
                    TerrainType.ShallowWater => Color.cyan,
                    TerrainType.Sand => Color.yellow,
                    TerrainType.Grass => Color.gray,
                    TerrainType.Forest => Color.green,
                    _ => throw new System.NotImplementedException()
                };

            }
        }

        return chunkTiles;
    }

    // 임계값(Threshold)을 기반으로 지형을 판별하는 함수
    private TerrainType DetermineTerrainType(float noiseHeight)
    {
        if (noiseHeight < 0.3f) return TerrainType.DeepWater;
        if (noiseHeight < 0.4f) return TerrainType.ShallowWater;
        if (noiseHeight < 0.45f) return TerrainType.Sand;
        if (noiseHeight < 0.75f) return TerrainType.Grass;
        return TerrainType.Forest;
    }
}

// 타일의 종류를 정의
public enum TerrainType
{
    DeepWater,
    ShallowWater,
    Sand,
    Grass,
    Forest
}

// 가벼운 데이터 구조체 (Flyweight)
public struct TileData
{
    public TerrainType terrain;
    public float heightValue; // 노이즈 결과값 (디버깅용 또는 부가 로직용)
    public GameObject testObj;
    // 향후 oreType (자원 종류) 등이 추가될 수 있음
}

public static class NoiseGenerator
{
    // FBM 기반 노이즈 생성 함수
    public static float GenerateFbmNoise(
        float xPosition, float yPosition, float scale, int octaves,
        float persistence, float lacunarity, float seedOffset)
    {
        if (scale <= 0) scale = 0.0001f;

        float totalNoise = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float maxValue = 0f; // 정규화를 위한 최대값 누적

        for (int i = 0; i < octaves; i++)
        {
            // 노이즈의 샘플링 좌표 계산 (시드값과 주파수 적용)
            float sampleX = (xPosition / scale) * frequency + seedOffset;
            float sampleY = (yPosition / scale) * frequency + seedOffset;

            // Unity.Mathematics의 2D Simplex Noise 호출 (결과값은 대략 -1 ~ 1)
            float noiseValue = noise.snoise(new float2(sampleX, sampleY));

            totalNoise += noiseValue * amplitude;
            maxValue += amplitude;

            // 다음 레이어를 위한 진폭과 주파수 조절
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        // -1 ~ 1 형태의 결과를 0 ~ 1 사이로 정규화하여 반환
        float normalizedNoise = (totalNoise / maxValue + 1f) / 2f;

        // 간혹 범위를 미세하게 벗어나는 수학적 오차 보정
        return Mathf.Clamp(normalizedNoise, 0f, 1f);
    }
}

public class ChunkGenerator
{
    // // 노이즈 설정 변수들
    // public float noiseScale = 50f;
    // public int noiseOctaves = 4;
    // public float noisePersistence = 0.5f;
    // public float noiseLacunarity = 2f;
    // public float mapSeed = 12345f; // 랜덤 시드

    // public int chunkSize = 16;

    // // 특정 청크 좌표(예: 0,0 또는 1,0)에 대한 타일 데이터를 생성하여 반환
    // public TileData[,] GenerateChunkData(Vector2Int chunkCoordinate)
    // {
    //     TileData[,] chunkTiles = new TileData[chunkSize, chunkSize];

    //     // 청크의 실제 월드 시작 좌표 계산
    //     float startWorldX = chunkCoordinate.x * chunkSize;
    //     float startWorldY = chunkCoordinate.y * chunkSize;

    //     for (int x = 0; x < chunkSize; x++)
    //     {
    //         for (int y = 0; y < chunkSize; y++)
    //         {
    //             // 각 타일의 절대적인 월드 좌표
    //             float worldX = startWorldX + x;
    //             float worldY = startWorldY + y;

    //             // FBM 노이즈 값 추출 (0.0 ~ 1.0)
    //             float currentHeight = NoiseGenerator.GenerateFbmNoise
    //             (worldX, worldY, noiseScale, noiseOctaves, noisePersistence, noiseLacunarity, mapSeed);

    //             // 노이즈 값에 따라 지형 결정
    //             TerrainType currentTerrain = DetermineTerrainType(currentHeight);

    //             // 타일 데이터 저장
    //             chunkTiles[x, y] = new TileData 
    //             { 
    //                 terrain = currentTerrain, 
    //                 heightValue = currentHeight
    //             };
    //             //Debug.Log($"Tile at ({x}, {y}): {currentTerrain}, Height: {currentHeight} / World Pos: ({worldX}, {worldY})");
    //         }
    //     }

    //     return chunkTiles;
    // }

    // // 임계값(Threshold)을 기반으로 지형을 판별하는 함수
    // private TerrainType DetermineTerrainType(float noiseHeight)
    // {
    //     if (noiseHeight < 0.3f) return TerrainType.DeepWater;
    //     if (noiseHeight < 0.4f) return TerrainType.ShallowWater;
    //     if (noiseHeight < 0.45f) return TerrainType.Sand;
    //     if (noiseHeight < 0.75f) return TerrainType.Grass;
    //     return TerrainType.Forest;
    // }
}