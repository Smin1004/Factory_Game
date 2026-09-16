using UnityEngine;
using UnityEngine.InputSystem;

namespace FactoryGame.Gameplay
{
    /// <summary>
    /// Input System 장치를 직접 읽는 얇은 입력 창구. 키 배치가 한곳에 모이고,
    /// 나중에 InputAction 에셋(리바인딩·게임패드)으로 옮길 때 이 클래스만 바꾸면 된다.
    /// PlayerInputEnabled가 false(맵 튜닝 모드)면 플레이어 조작 입력은 전부 무시된다. 줌은 예외.
    ///
    /// 이동 WASD/방향키/왼쪽 스틱 · 좌클릭 설치/투입 · 우클릭(홀드) 채굴 · R 회전 · F 줍기 · 1~9 핫바 · 휠 줌
    /// </summary>
    public static class GameInput
    {
        public static bool PlayerInputEnabled = true;

        public static Vector2 Move
        {
            get
            {
                if (!PlayerInputEnabled) return Vector2.zero;
                Vector2 v = Vector2.zero;
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1f;
                    if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1f;
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1f;
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
                }
                var gp = Gamepad.current;
                if (gp != null) v += gp.leftStick.ReadValue();
                return Vector2.ClampMagnitude(v, 1f);
            }
        }

        public static bool PlaceHeld => PlayerInputEnabled && Mouse.current != null && Mouse.current.leftButton.isPressed;
        public static bool PlacePressed => PlayerInputEnabled && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        public static bool MineHeld => PlayerInputEnabled && Mouse.current != null && Mouse.current.rightButton.isPressed;
        public static bool RotatePressed => PlayerInputEnabled && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
        public static bool PickupPressed => PlayerInputEnabled && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;

        public static Vector2 PointerScreenPosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

        /// <summary>휠 한 눈금 = ±1. 튜닝 모드에서도 동작한다.</summary>
        public static int ZoomSteps
        {
            get
            {
                var m = Mouse.current;
                if (m == null) return 0;
                float y = m.scroll.ReadValue().y;
                return y > 0f ? 1 : y < 0f ? -1 : 0;
            }
        }

        /// <summary>이번 프레임에 눌린 핫바 번호(1~9). 없으면 0.</summary>
        public static int HotbarPressed
        {
            get
            {
                if (!PlayerInputEnabled) return 0;
                var kb = Keyboard.current;
                if (kb == null) return 0;
                for (int i = 0; i < 9; i++)
                    if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) return i + 1;
                return 0;
            }
        }
    }
}
