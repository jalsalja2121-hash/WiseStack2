using UnityEngine;

namespace ARLogistics.Data
{
    [System.Serializable]
    public readonly struct BoxMeasurement
    {
        public readonly float WidthM;
        public readonly float DepthM;
        public readonly float HeightM;
        public readonly int ClassId;
        public readonly string SourceName;

        public bool IsValid => WidthM > 0.01f && DepthM > 0.01f && HeightM > 0.01f;

        public BoxMeasurement(float widthM, float depthM, float heightM, int classId, string sourceName)
        {
            WidthM = widthM;
            DepthM = depthM;
            HeightM = heightM;
            ClassId = classId;
            SourceName = sourceName ?? string.Empty;
        }
    }

    /// <summary>MeasureScene과 ARMain 사이에서 상자 측정 결과를 미터 단위로 공유합니다.</summary>
    public static class BoxMeasurementStore
    {
        private const string HasValueKey = "WiseStack.BoxMeasurement.HasValue";
        private const string WidthKey = "WiseStack.BoxMeasurement.WidthM";
        private const string DepthKey = "WiseStack.BoxMeasurement.DepthM";
        private const string HeightKey = "WiseStack.BoxMeasurement.HeightM";
        private const string ClassIdKey = "WiseStack.BoxMeasurement.ClassId";
        private const string SourceNameKey = "WiseStack.BoxMeasurement.SourceName";

        public static void Save(float widthM, float depthM, float heightM, int classId, string sourceName)
        {
            var measurement = new BoxMeasurement(widthM, depthM, heightM, classId, sourceName);
            if (!measurement.IsValid)
            {
                Debug.LogWarning("[BoxMeasurementStore] 유효하지 않은 상자 크기는 저장하지 않습니다.");
                return;
            }

            PlayerPrefs.SetInt(HasValueKey, 1);
            PlayerPrefs.SetFloat(WidthKey, measurement.WidthM);
            PlayerPrefs.SetFloat(DepthKey, measurement.DepthM);
            PlayerPrefs.SetFloat(HeightKey, measurement.HeightM);
            PlayerPrefs.SetInt(ClassIdKey, measurement.ClassId);
            PlayerPrefs.SetString(SourceNameKey, measurement.SourceName);
            PlayerPrefs.Save();
        }

        public static bool TryGet(out BoxMeasurement measurement)
        {
            measurement = default;
            if (PlayerPrefs.GetInt(HasValueKey, 0) != 1)
                return false;

            measurement = new BoxMeasurement(
                PlayerPrefs.GetFloat(WidthKey, 0f),
                PlayerPrefs.GetFloat(DepthKey, 0f),
                PlayerPrefs.GetFloat(HeightKey, 0f),
                PlayerPrefs.GetInt(ClassIdKey, -1),
                PlayerPrefs.GetString(SourceNameKey, string.Empty));
            return measurement.IsValid;
        }
    }
}
