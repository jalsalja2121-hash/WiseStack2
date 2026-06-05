using UnityEngine;

namespace ARLogistics.Data
{
    /// <summary>
    /// YOLOv8 클래스 하나에 대응하는 실물 제품의 물리 특성.
    /// ProductDatabase ScriptableObject 안에 배열로 보관됩니다.
    /// </summary>
    [System.Serializable]
    public class ProductSpec
    {
        [Tooltip("YOLOv8 클래스 인덱스 (classId)")]
        public int classId;

        [Tooltip("클래스 표시 이름 (예: 'Cardboard Box A')")]
        public string displayName;

        [Header("Physical Dimensions (m)")]
        public float width  = 0.4f;
        public float length = 0.6f;
        public float height = 0.3f;

        [Header("Weight & Load")]
        [Tooltip("단위 제품 무게 (kg)")]
        public float weightKg = 10f;

        [Tooltip("이 제품 위에 올릴 수 있는 최대 무게 (kg). 0 = 최상단 전용")]
        public float maxStackLoadKg = 50f;

        [Tooltip("팔레트 1단에 배치 가능한 최대 개수 (면적 기반 자동 계산 시 0으로 설정)")]
        public int maxPerLayer = 0;

        /// <summary>단면적 (m²) = width × length</summary>
        public float FootprintArea => width * length;

        /// <summary>단위 부피 (m³)</summary>
        public float Volume => width * length * height;
    }

    /// <summary>
    /// 모든 제품 스펙을 보관하는 ScriptableObject (프로젝트 전역 DB).
    /// Assets/Data/ProductDatabase.asset 으로 생성합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "ProductDatabase", menuName = "ARLogistics/Product Database")]
    public class ProductDatabase : ScriptableObject
    {
        [Tooltip("classId 순서로 정렬 권장 (탐색 성능)")]
        [SerializeField] private ProductSpec[] products = new ProductSpec[0];

        // ── 조회 ──────────────────────────────────────

        /// <summary>classId 로 ProductSpec 을 반환합니다. 없으면 null.</summary>
        public ProductSpec GetById(int classId)
        {
            foreach (var p in products)
                if (p.classId == classId) return p;
            return null;
        }

        /// <summary>displayName 으로 ProductSpec 을 반환합니다. 없으면 null.</summary>
        public ProductSpec GetByName(string displayName)
        {
            foreach (var p in products)
                if (p.displayName == displayName) return p;
            return null;
        }

        public int Count => products.Length;
    }
}
