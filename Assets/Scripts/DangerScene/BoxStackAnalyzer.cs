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

        // YOLO 탐지 결과 기반 분석 (메인)
        public DangerLevel Analyze(List<DetectionResult> detections,
                                   out int   boxCount,
                                   out float estimatedHeightM,
                                   out float totalWeightKg)
        {
            boxCount         = detections != null ? detections.Count : 0;
            estimatedHeightM = 0f;
            totalWeightKg    = 0f;

            if (detections != null)
            {
                foreach (var det in detections)
                {
                    var spec          = ProductSpecTable.Get(det.classId);
                    estimatedHeightM += spec.HeightM;
                    totalWeightKg    += spec.WeightKg;
                }
            }

            return Evaluate(boxCount, estimatedHeightM, totalWeightKg);
        }

        // 프리셋 데모용 수동 입력 오버로드
        public DangerLevel Analyze(int boxCount, float heightM, float weightKg)
            => Evaluate(boxCount, heightM, weightKg);

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
