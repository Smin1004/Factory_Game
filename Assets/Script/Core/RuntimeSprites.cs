using System;
using UnityEngine;

namespace FactoryGame.Core
{
    /// <summary>
    /// 아트 에셋이 아직 없는 요소(플레이어, 커서, 디버그 표시)에 쓰는 런타임 생성 스프라이트.
    /// 전부 1×1 유닛, 피벗 중앙. 실제 아트가 들어오면 사용처의 스프라이트만 교체하면 된다.
    /// </summary>
    public static class RuntimeSprites
    {
        private static Sprite square, outline, player;

        /// <summary>흰 사각형.</summary>
        public static Sprite Square
        {
            get
            {
                if (square == null) square = Build(4, (x, y, s) => true);
                return square;
            }
        }

        /// <summary>테두리만 있는 사각형(안은 투명). 타일 커서용.</summary>
        public static Sprite Outline
        {
            get
            {
                if (outline == null) outline = Build(32, (x, y, s) => x < 3 || y < 3 || x >= s - 3 || y >= s - 3);
                return outline;
            }
        }

        /// <summary>원 몸통 + 오른쪽(+X)으로 튀어나온 코. 회전시켜 바라보는 방향을 표현한다.</summary>
        public static Sprite Player
        {
            get
            {
                if (player == null)
                    player = Build(32, (x, y, s) =>
                    {
                        float cx = x + 0.5f - 13f, cy = y + 0.5f - 16f;
                        if (cx * cx + cy * cy <= 11f * 11f) return true;          // 몸통 (중심을 살짝 왼쪽으로)
                        float nx = x + 0.5f;                                       // 코: x 20~32에서 점점 좁아지는 삼각형
                        return nx >= 20f && Mathf.Abs(cy) <= (32f - nx) * 0.55f;
                    });
                return player;
            }
        }

        private static Sprite Build(int size, Func<int, int, int, bool> filled)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "RuntimeSprite",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = filled(x, y, size) ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
