using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ARLogistics.Detection;

namespace ARLogistics.AR
{
    /// <summary>
    /// YOLOv8 탐지 결과(2D 스크린 좌표)를 AR 3D 월드 좌표로 변환하는 브릿지.
    ///
    /// 파이프라인:
    ///   YoloDetector.OnDetectionResultsUpdated
    ///       → BoundingBox 중심점 (정규화 0~1)
    ///       → Screen 픽셀 좌표
    ///       → ARRaycastManager.Raycast()
    ///       → Pose (3D 월드 위치·회전)
    ///       → OnObjectsLocated 이벤트 발행
    /// </summary>
    // RequireComponent 제거: ARRaycastManager는 XR Origin에 있으므로
    // FindFirstObjectByType 으로 씬에서 탐색
    public class ARDetectionBridge : MonoBehaviour
    {
        // ─────────────────────────────────────────────
        // Inspector 설정
        // ─────────────────────────────────────────────

        [Header("Dependencies")]
        [Tooltip("YoloDetector 컴포넌트 (자동 탐색 또는 직접 할당)")]
        [SerializeField] private YoloDetector yoloDetector;

        [Tooltip("AR 세션 카메라 (AR Camera GameObject 의 Camera 컴포넌트)")]
        [SerializeField] private Camera arCamera;

        [Header("Raycast Settings")]
        [Tooltip("탐지 대상 AR 평면 유형 (수평면 + 수직면)")]
        [SerializeField] private TrackableType raycastLayers =
            TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds;

        [Tooltip("같은 물체를 중복 처리하지 않을 최소 월드 거리 (m)")]
        [SerializeField] private float deduplicateRadius = 0.15f;

        [Header("Performance")]
        [Tooltip("추론 결과를 AR Raycast 에 반영하는 최소 간격 (초)")]
        [SerializeField] private float processInterval = 0.3f;

        // ─────────────────────────────────────────────
        // 이벤트 (Observer → WarehouseManager / Gemini API)
        // ─────────────────────────────────────────────

        /// <summary>
        /// 2D 탐지 + 3D 위치가 모두 확정된 물체 목록을 발행합니다.
        /// </summary>
        public System.Action<List<LocatedObject>> OnObjectsLocated;

        // ─────────────────────────────────────────────
        // 내부 상태
        // ─────────────────────────────────────────────

        private ARRaycastManager _raycastManager;
        private readonly List<ARRaycastHit> _hitBuffer = new();
        private float _lastProcessTime;

        // ─────────────────────────────────────────────
        // 생명주기
        // ─────────────────────────────────────────────

        private void Awake()
        {
            // ARRaycastManager는 XR Origin에 있으므로 씬 전체에서 탐색
            _raycastManager = FindFirstObjectByType<ARRaycastManager>();
            if (_raycastManager == null)
                Debug.LogError("[ARDetectionBridge] ARRaycastManager를 씬에서 찾을 수 없습니다!");

            // YoloDetector 자동 탐색
            if (yoloDetector == null)
                yoloDetector = FindFirstObjectByType<YoloDetector>();

            // AR 카메라 자동 탐색
            if (arCamera == null)
                arCamera = Camera.main;

            if (yoloDetector == null)
                Debug.LogError("[ARDetectionBridge] YoloDetector를 찾을 수 없습니다!");

            if (arCamera == null)
                Debug.LogError("[ARDetectionBridge] AR Camera를 찾을 수 없습니다!");
        }

        private void OnEnable()
        {
            if (yoloDetector != null)
                yoloDetector.OnDetectionResultsUpdated += HandleDetections;
        }

        private void OnDisable()
        {
            if (yoloDetector != null)
                yoloDetector.OnDetectionResultsUpdated -= HandleDetections;
        }

        // ─────────────────────────────────────────────
        // 이벤트 핸들러
        // ─────────────────────────────────────────────

        private void HandleDetections(List<DetectionResult> detections)
        {
            // 처리 주기 제한 (모바일 성능 최적화)
            if (Time.time - _lastProcessTime < processInterval) return;
            _lastProcessTime = Time.time;

            if (detections == null || detections.Count == 0) return;
            if (_raycastManager == null || arCamera == null) return;

            var located = new List<LocatedObject>(detections.Count);
            var usedPositions = new List<Vector3>(detections.Count);

            foreach (var det in detections)
            {
                // 1. BoundingBox 중심점 → 화면 픽셀 좌표
                Vector2 screenPoint = NormalizedToScreen(det.boundingBox);

                // 2. AR Raycast (평면 교차점 탐색)
                if (!_raycastManager.Raycast(screenPoint, _hitBuffer, raycastLayers))
                    continue;

                // 가장 가까운 히트 사용
                var hit = _hitBuffer[0];
                Vector3 worldPos = hit.pose.position;

                // 3. 중복 제거 (같은 위치에 여러 탐지가 겹치는 경우)
                if (IsDuplicate(worldPos, usedPositions)) continue;
                usedPositions.Add(worldPos);

                // 4. LocatedObject 생성
                var obj = new LocatedObject(
                    detection: det,
                    worldPose: hit.pose,
                    screenRect: det.boundingBox,
                    trackableId: hit.trackableId
                );
                located.Add(obj);

                Debug.Log($"[ARBridge] {det.className} → 3D({worldPos.x:F2}, {worldPos.y:F2}, {worldPos.z:F2})  conf={det.confidence:F2}");
            }

            if (located.Count > 0)
                OnObjectsLocated?.Invoke(located);
        }

        // ─────────────────────────────────────────────
        // 좌표 변환 유틸
        // ─────────────────────────────────────────────

        /// <summary>
        /// 정규화 BoundingBox 중심점(0~1) → Unity 스크린 픽셀 좌표.
        /// AR Foundation Raycast 는 Screen Space 픽셀 좌표를 입력으로 받습니다.
        /// </summary>
        private static Vector2 NormalizedToScreen(Rect bbox)
        {
            float centerX = (bbox.x + bbox.width  * 0.5f) * Screen.width;
            // Unity Rect의 y=0은 상단, Screen Space y=0은 하단 → 반전
            float centerY = (1f - (bbox.y + bbox.height * 0.5f)) * Screen.height;
            return new Vector2(centerX, centerY);
        }

        /// <summary>반경 내 이미 처리된 위치가 있으면 중복으로 판단합니다.</summary>
        private bool IsDuplicate(Vector3 pos, List<Vector3> usedPositions)
        {
            foreach (var used in usedPositions)
                if (Vector3.Distance(pos, used) < deduplicateRadius) return true;
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 데이터 클래스
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 2D 탐지 결과 + 3D AR 공간 위치가 결합된 최종 물체 정보.
    /// WarehouseManager 와 Gemini API 가 이 데이터를 소비합니다.
    /// </summary>
    [System.Serializable]
    public class LocatedObject
    {
        // ── 탐지 정보 (YOLOv8) ──
        public DetectionResult Detection;

        // ── AR 공간 정보 ──
        /// <summary>AR 평면 위의 3D 위치·회전 (월드 좌표).</summary>
        public Pose WorldPose;

        /// <summary>물체가 놓인 AR 평면의 TrackableId.</summary>
        public TrackableId TrackableId;

        // ── 스크린 정보 ──
        /// <summary>정규화 BoundingBox (0~1). UI 오버레이에 활용.</summary>
        public Rect ScreenRect;

        // ── 편의 프로퍼티 ──
        public string  ClassName   => Detection.className;
        public int     ClassId     => Detection.classId;
        public float   Confidence  => Detection.confidence;
        public Vector3 Position    => WorldPose.position;

        public LocatedObject(DetectionResult detection, Pose worldPose, Rect screenRect, TrackableId trackableId)
        {
            Detection   = detection;
            WorldPose   = worldPose;
            ScreenRect  = screenRect;
            TrackableId = trackableId;
        }

        public override string ToString() =>
            $"[{ClassName}] conf={Confidence:F2}  3D=({Position.x:F2},{Position.y:F2},{Position.z:F2})";
    }
}
