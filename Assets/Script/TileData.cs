using UnityEngine;

// 타일의 종류를 정의
public enum TerrainType
{
    DeepWater,
    ShallowWater,
    Sand,
    Grass,
    Forest
}

public struct TileData
{
    public TerrainType terrain;
    public float heightValue; // 노이즈 결과값 (디버깅용 또는 부가 로직용)
    public GameObject testObj;
    public SpriteRenderer sprite;
    // 향후 oreType (자원 종류) 등이 추가될 수 있음
}