using System.Collections.Generic;
using ARLogistics.Data;
using ARLogistics.Detection;
using UnityEngine;

namespace ARLogistics.DangerScene
{
    public enum DangerLevel { Safe, Warning, Danger }

    public class BoxStackAnalyzer : MonoBehaviour
    {
        [Header("Risk Thresholds")]
        [SerializeField, Min(0f)] private float warningHeightM = 1.2f;
        [SerializeField, Min(0f)] private float dangerHeightM = 2.0f;
        [SerializeField, Min(1)] private int warningBoxCount = 5;
        [SerializeField, Min(1)] private int dangerBoxCount = 8;
        [SerializeField, Min(0f)] private float warningWeightKg = 25f;
        [SerializeField, Min(0f)] private float dangerWeightKg = 40f;

        [Header("Camera Based Height Estimate")]
        [Tooltip("Vertical camera FOV used by the distance-based fallback estimate.")]
        [SerializeField, Min(1f)] private float cameraVerticalFovDegrees = 60f;

        [Tooltip("Fallback distance to the detected stack in meters.")]
        [SerializeField, Min(0.1f)] private float assumedDistanceM = 2.0f;

        [Header("Product Based Height Estimate")]
        [Tooltip("Use product dimensions and bbox aspect ratio to estimate stack height.")]
        [SerializeField] private bool useProductAspectHeight = true;

        [Tooltip("Slightly bias visual estimates upward to avoid marking tall stacks safe.")]
        [SerializeField, Min(0.1f)] private float aspectHeightSafetyMultiplier = 1.1f;

        [Tooltip("Use the latest measured box dimensions when they match the detected class.")]
        [SerializeField] private bool preferMeasuredDimensions = true;

        [Tooltip("Upper guardrail for visual estimates.")]
        [SerializeField, Min(0.5f)] private float maxEstimatedHeightM = 6f;

        [SerializeField] private bool logAnalysisDetails;

        public DangerLevel Analyze(
            List<DetectionResult> detections,
            out int boxCount,
            out float estimatedHeightM,
            out float totalWeightKg)
        {
            boxCount = 0;
            estimatedHeightM = 0f;
            totalWeightKg = 0f;

            if (detections == null || detections.Count == 0)
                return Evaluate(boxCount, estimatedHeightM, totalWeightKg);

            var groups = GroupByClass(detections);
            foreach (var group in groups)
            {
                BoxSpec spec = ResolveSpec(group.Key);
                Rect unionRect = UnionNormalizedRects(group.Value);
                float stackHeightM = EstimateStackHeight(unionRect, spec);
                int estimatedCount = EstimateBoxCount(group.Value.Count, stackHeightM, spec);
                float stackWeightKg = estimatedCount * spec.WeightKg;

                boxCount += estimatedCount;
                estimatedHeightM = Mathf.Max(estimatedHeightM, stackHeightM);
                totalWeightKg += stackWeightKg;

                if (logAnalysisDetails)
                {
                    Debug.Log(
                        $"[BoxStackAnalyzer] class={group.Key}, detections={group.Value.Count}, " +
                        $"estimatedCount={estimatedCount}, bbox={FormatRect(unionRect)}, " +
                        $"height={stackHeightM:F2}m, weight={stackWeightKg:F1}kg");
                }
            }

            return Evaluate(boxCount, estimatedHeightM, totalWeightKg);
        }

        public DangerLevel Analyze(int boxCount, float heightM, float weightKg)
            => Evaluate(boxCount, heightM, weightKg);

        private DangerLevel Evaluate(int boxCount, float heightM, float weightKg)
        {
            if (heightM >= dangerHeightM || boxCount >= dangerBoxCount || weightKg >= dangerWeightKg)
                return DangerLevel.Danger;

            if (heightM >= warningHeightM || boxCount >= warningBoxCount || weightKg >= warningWeightKg)
                return DangerLevel.Warning;

            return DangerLevel.Safe;
        }

        private static Dictionary<int, List<DetectionResult>> GroupByClass(List<DetectionResult> detections)
        {
            var groups = new Dictionary<int, List<DetectionResult>>();
            foreach (DetectionResult detection in detections)
            {
                if (!groups.TryGetValue(detection.classId, out List<DetectionResult> group))
                {
                    group = new List<DetectionResult>();
                    groups[detection.classId] = group;
                }

                group.Add(detection);
            }

            return groups;
        }

        private BoxSpec ResolveSpec(int classId)
        {
            ProductSpecTable.Spec spec = ProductSpecTable.Get(classId);
            var resolved = new BoxSpec(
                widthM: spec.WidthM,
                depthM: spec.LengthM,
                heightM: spec.HeightM,
                weightKg: spec.WeightKg);

            if (!preferMeasuredDimensions)
                return resolved;

            if (!BoxMeasurementStore.TryGet(out BoxMeasurement measured) || measured.ClassId != classId)
                return resolved;

            return new BoxSpec(
                widthM: measured.WidthM,
                depthM: measured.DepthM,
                heightM: measured.HeightM,
                weightKg: resolved.WeightKg);
        }

        private float EstimateStackHeight(Rect normalizedRect, BoxSpec spec)
        {
            float fovHeightM = EstimateHeightFromCameraFov(normalizedRect);
            float aspectHeightM = useProductAspectHeight
                ? EstimateHeightFromProductAspect(normalizedRect, spec)
                : 0f;

            float heightM = Mathf.Max(spec.HeightM, Mathf.Max(fovHeightM, aspectHeightM));
            return Mathf.Clamp(heightM, 0f, maxEstimatedHeightM);
        }

        private float EstimateHeightFromCameraFov(Rect normalizedRect)
        {
            float rectHeight = Mathf.Clamp01(normalizedRect.height);
            float fovDegrees = Mathf.Clamp(cameraVerticalFovDegrees, 1f, 179f);
            float fovRad = fovDegrees * Mathf.Deg2Rad;
            float viewableHeightM = 2f * assumedDistanceM * Mathf.Tan(fovRad * 0.5f);
            return rectHeight * viewableHeightM;
        }

        private float EstimateHeightFromProductAspect(Rect normalizedRect, BoxSpec spec)
        {
            float rectWidth = Mathf.Clamp01(normalizedRect.width);
            float rectHeight = Mathf.Clamp01(normalizedRect.height);
            if (rectWidth <= 0.01f || rectHeight <= 0.01f)
                return 0f;

            float visibleAspect = rectHeight / rectWidth;
            float referenceWidthM = Mathf.Max(spec.WidthM, spec.DepthM);
            if (referenceWidthM <= 0.01f)
                return 0f;

            return visibleAspect * referenceWidthM * aspectHeightSafetyMultiplier;
        }

        private static int EstimateBoxCount(int detectedCount, float stackHeightM, BoxSpec spec)
        {
            int countFromHeight = spec.HeightM > 0.01f
                ? Mathf.Max(1, Mathf.RoundToInt(stackHeightM / spec.HeightM))
                : 1;

            return Mathf.Max(detectedCount, countFromHeight);
        }

        private static Rect UnionNormalizedRects(List<DetectionResult> detections)
        {
            float minX = 1f;
            float minY = 1f;
            float maxX = 0f;
            float maxY = 0f;

            foreach (DetectionResult detection in detections)
            {
                Rect rect = ClampNormalizedRect(detection.boundingBox);
                minX = Mathf.Min(minX, rect.xMin);
                minY = Mathf.Min(minY, rect.yMin);
                maxX = Mathf.Max(maxX, rect.xMax);
                maxY = Mathf.Max(maxY, rect.yMax);
            }

            if (maxX <= minX || maxY <= minY)
                return Rect.zero;

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private static Rect ClampNormalizedRect(Rect rect)
        {
            float minX = Mathf.Clamp01(Mathf.Min(rect.xMin, rect.xMax));
            float minY = Mathf.Clamp01(Mathf.Min(rect.yMin, rect.yMax));
            float maxX = Mathf.Clamp01(Mathf.Max(rect.xMin, rect.xMax));
            float maxY = Mathf.Clamp01(Mathf.Max(rect.yMin, rect.yMax));

            if (maxX <= minX)
                maxX = Mathf.Clamp01(minX + Mathf.Abs(rect.width));
            if (maxY <= minY)
                maxY = Mathf.Clamp01(minY + Mathf.Abs(rect.height));

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private static string FormatRect(Rect rect)
            => $"({rect.x:F2},{rect.y:F2},{rect.width:F2},{rect.height:F2})";

        private readonly struct BoxSpec
        {
            public readonly float WidthM;
            public readonly float DepthM;
            public readonly float HeightM;
            public readonly float WeightKg;

            public BoxSpec(float widthM, float depthM, float heightM, float weightKg)
            {
                WidthM = Mathf.Max(0f, widthM);
                DepthM = Mathf.Max(0f, depthM);
                HeightM = Mathf.Max(0f, heightM);
                WeightKg = Mathf.Max(0f, weightKg);
            }
        }
    }
}
