using UnityEngine;

namespace FactoryGame.Gameplay
{
    /// <summary>
    /// 플레이어를 부드럽게 따라가는 직교 카메라 + 휠 줌.
    /// WorldView가 카메라 위치로 가시 청크를 계산하므로 그보다 먼저 실행한다(-10).
    /// Follow를 끄면(맵 튜닝 모드) Pan으로 자유 이동한다.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField, Tooltip("클수록 빨리 따라붙음")] private float smoothing = 10f;
        [SerializeField] private float minZoom = 4f;
        [SerializeField] private float maxZoom = 40f;
        [SerializeField, Range(0.01f, 0.5f), Tooltip("휠 한 눈금당 배율 변화")] private float zoomStep = 0.12f;
        [SerializeField, Tooltip("자유 카메라 속도 (화면 반높이 배수/초)")] private float panSpeed = 1.5f;

        /// <summary>false면 타깃을 따라가지 않는다 (디버그 자유 카메라).</summary>
        public bool Follow { get; set; } = true;

        private Camera cam;

        private void Awake()
        {
            cam = GetComponent<Camera>();
        }

        private void Start()
        {
            if (target == null)
            {
                var player = FindFirstObjectByType<PlayerController>();
                if (player != null) target = player.transform;
            }
            Snap();
        }

        private void LateUpdate()
        {
            int zoom = GameInput.ZoomSteps;
            if (zoom != 0)
                cam.orthographicSize = Mathf.Clamp(cam.orthographicSize * (1f - zoom * zoomStep), minZoom, maxZoom);

            if (!Follow || target == null) return;
            Vector3 p = transform.position;
            Vector3 t = target.position;
            t.z = p.z;
            float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            transform.position = Vector3.Lerp(p, t, k);
        }

        /// <summary>타깃 위치로 즉시 이동 (스폰 직후 등).</summary>
        public void Snap()
        {
            if (target == null) return;
            Vector3 t = target.position;
            t.z = transform.position.z;
            transform.position = t;
        }

        /// <summary>자유 카메라 이동. dir은 정규화된 방향.</summary>
        public void Pan(Vector2 dir)
        {
            transform.position += (Vector3)(dir * (panSpeed * cam.orthographicSize * Time.deltaTime));
        }
    }
}
