using System.Collections.Generic;
using UnityEngine;
using ARLogistics.Detection;
using ARLogistics.Data;

namespace ARLogistics.DangerScene
{
    public enum DangerLevel { Safe, Warning, Danger }

    public class BoxStackAnalyzer : MonoBehaviour
    {
        private const float SafeHeight     = 1.2f;
        private const float DangerHeight   = 2.0f;
        private const int   DangerBoxCount = 8;
        private const float SafeWeight     = 25f;
        private const float DangerWeight   = 40f;

        [Header("시각적 높이 추정 (카메라 기반)")]
        [Tooltip("카메라 수직 화각(도). 스마트폰 기본 카메라 기준 약 60°")]
        [SerializeField] private float cameraVerticalFovDegrees = 60f;

        [Tooltip("박스 적재물까지의 추정 거리(m). 현장 환경에 맞게 조정하세요.")]
        [SerializeField] private float assumedDistanceM = 2.0f;

        // YOLO 탐지 결과 기반 분석 (메인)
        public DangerLevel Analyze(List<DetectionResult> detections,
                                   out int   boxCount,
                                   out float estimatedHeightM,
                                   out float totalWeightKg)
        {
            boxCount         = detections != null ? detections.Count : 0;
            estimatedHeightM = 0f;
            totalWeightKg    = 0f;

            if (detections != null && detections.Count > 0)
            {
                // 높이: 화면 내 바운딩박스 전체 범위 → 실세계 높이 변환
                estimatedHeightM = EstimateVisualHeight(detections);

                // 무게: ProductSpecTable 기반 클래스별 평균 무게 합산
                foreach (var det in detections)
                    totalWeightKg += ProductSpecTable.Get(det.classId).WeightKg;
            }

            return Evaluate(boxCount, estimatedHeightM, totalWeightKg);
        }

        // 프리셋 데모용 수동 입력 오버로드
        public DangerLevel Analyze(int boxCount, float heightM, float weightKg)
            => Evaluate(boxCount, heightM, weightKg);

        /// <summary>
        /// YOLO 바운딩박스의 화면 상 Y범위를 카메라 FOV와 거리를 통해 실세계 높이(m)로 변환합니다.
        /// union_height(비율) × 2 × distance × tan(vFOV/2)
        /// </summary>
        private float EstimateVisualHeight(List<DetectionResult> detections)
        {
            float minY = float.MaxValue; // 화면 가장 위쪽 (박스 스택 상단)
            float maxY = float.MinValue; // 화면 가장 아래쪽 (박스 스택 하단)

            foreach (var det in detections)
            {
                float top    = det.boundingBox.y;
                float bottom = det.boundingBox.y + det.boundingBox.height;
                if (top    < minY) minY = top;
                if (bottom > maxY) maxY = bottom;
            }

            float unionHeightNorm = Mathf.Clamp01(maxY - minY);

            // 카메라 수직 FOV 기준 화면 전체가 커버하는 실세계 높이
            float vFovRad        = cameraVerticalFovDegrees * Mathf.Deg2Rad;
            float viewableHeight = 2f * assumedDistanceM * Mathf.Tan(vFovRad * 0.5f);

            return unionHeightNorm * viewableHeight;
        }

        private DangerLevel Evaluate(int boxCount, float heightM, float weightKg)
        {
            if (heightM > DangerHeight || boxCount >= DangerBoxCount || weightKg > DangerWeight)
                return DangerLevel.Danger;

            if (heightM > SafeHeight || boxCount >= 5 || weightKg > SafeWeight)
                return DangerLevel.Warning;

            return DangerLevel.Safe;
        }
    }
}
