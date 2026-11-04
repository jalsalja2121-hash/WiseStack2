# 물류 적재 추천 개선 및 검증

## 작업 범위

아래 기록은 알고리즘 구현 당시의 검증 결과다. PNG·테스트 출력·APK 링크는 로컬 산출물이며 저장소 커밋 대상에서 제외한다. 후속 main 통합 시 재검증 결과는 Git 통합 보고에서 별도로 기록한다.

2026-11-04, `sangyeop` 브랜치. 요청한 `prompt.txt`는 없고 루트의 `codex_prompt.txt`에 해당 요구사항이 있어 이를 적용했다. 기존 미커밋 변경 위에 필요한 수정만 추가했다. 커밋·병합·푸시는 하지 않았다. 기존 `RecommendationReview.md`와 `ProductUIReview.md`의 과거 기록은 보존했다.

## 추천 정책 변경

이전 정책은 효율 75점과 높이 여유 25점의 합산이었다. 새 정책은 검증 가능한 제약조건 위반 후보를 제외한 뒤 다음 순서로 비교한다.

1. 팔레트당 수량 최대
2. 수량이 같으면 바닥면 활용률 최대
3. 위 두 지표가 같으면 총 적재 높이 최소
4. 기본 방향 우선, 그래도 같으면 A/B/C 순서 유지

수량·바닥면·공간 활용률을 합산하지 않는다. 높이에 임의의 보너스를 주지 않는다. 비교 시 기존 부동소수 허용오차(바닥면 0.01%p, 높이 0.001m)를 유지한다. 이는 WiseStack2 자체 정책이며 ISO 공식이나 전체 3차원 배치 문제의 전역 최적해가 아니다.

## 제약조건과 계산식

길이는 m, 면적은 m², 부피는 m³, 무게는 kg이다. 기본 방향과 바닥면 90° 회전을 각각 계산하며 화물을 옆으로 눕히지는 않는다. 한 층을 채우는 직교 격자 모델을 유지한다.

| 항목 | 계산 / 의미 |
| --- | --- |
| 유효 높이 한도 | Hmax = min(1.8, 천장 높이 − 0.3), 팔레트 포함 |
| 팔레트 높이 | Hp = 0.15m, 기존 앱 정책 |
| 사용 가능 높이 | Havailable = Hmax − Hp; 0 이하이면 적재 후보 없음 |
| 수량 | N = nx × ny × nz |
| 바닥면 활용률 | F = nx × ny × 회전 적용 화물 폭 × 길이 / 팔레트 면적 |
| 공간 활용률 | V = N × 화물 폭 × 길이 × 높이 / (팔레트 면적 × Havailable) |
| 총 높이 | Htotal = Hp + nz × 화물 높이 ≤ Hmax |
| 높이 여유 | R = Hmax − Htotal |
| 화물 하중 | Wcargo = N × 개당 기준 무게 ≤ 허용하중 |

F와 V는 화면에서 백분율로 표시한다. 창고 천장 높이를 팔레트별 적재 높이와 구분한다. 허용하중은 기존 데이터 사용 방식대로 **화물 중량 기준**이며 팔레트 자체 중량을 임의로 더하지 않는다. 입력·계획의 NaN, Infinity, 음수, 유효하지 않은 치수, 경계 초과, 높이·하중 초과, 전략과 AR 계획 불일치를 제외한다. 0 허용하중은 적재 불가로 유지한다.

화물 무게 0은 이 API에서 **누락 표시값**이다. 이 경우 높이·경계만 계산한 조건부 예상안을 표시하고 하중 검증 완료로 처리하지 않는다. 음수·NaN·Infinity 무게는 입력 오류다. 분류별 자료가 없을 때 기존 범용 fallback 무게를 추천 하중 계산에 사용하지 않는다. 알려진 무게도 분류별 대표값이며 실측 무게로 주장하지 않는다. 0.01kg 이하의 양수 무게도 하중 제한을 적용하도록 수정했다.

## 국제표준 및 지침 참고 범위

- [ISO 6780:2003 공식 개요](https://www.iso.org/standard/30524.html): 팔레트 주요 치수·허용오차에 관한 범위를 참고했다. 앱의 미터 단위 처리와 치수 검증을 유지하며, 비표준 팔레트를 배제하지 않는다. 규격별 프리셋은 향후 이름·폭·길이·출처를 가진 참고 데이터로 분리할 수 있으나 강제 허용 목록은 도입하지 않았다.
- [ISO 8611-1:2025 공식 개요](https://www.iso.org/standard/87543.html): 시험 방법과 최대 작업 하중 평가 범위를 참고했다. 공통 허용하중을 표준에서 가져왔다고 주장하지 않는다. 사용자가 입력한 허용하중과 대표 화물 중량을 비교한다. 실제 허용하중은 제조사 자료·사용 조건·시험자료 확인이 필요하다.
- [IMO/ILO/UNECE CTU Code 공식 안내](https://unece.org/transport/intermodal-transport/imoilounece-code-practice-packing-cargo-transport-units-ctu-code): 적재·고정 관련 지침의 범위를 참고했다. 하중 분포·무게중심·고정 상태를 이 구현에서 검증하지 않는다고 화면에 명시했다.

ISO 유료 원문의 전체 조항 적합성이나 시험을 검증하지 않았다. 국제표준 준수·인증·실제 적재 안전성 보증을 주장하지 않는다. 위 공식 공개 개요를 참고한 범위와 구현한 기하학·입력 조건 검사를 구분한다.

## A/B/C 결과와 구조적 한계

팔레트 1.2×1m, 천장 3m, 허용하중 1000kg 기준이다.

| 사례 | 결과 |
| --- | --- |
| 0.35×0.48×0.315m, 3.008kg | A/B 30개·5층·1.725m·바닥면 84%; A 선정 |
| 위 사례의 C | 24개·4층·1.410m; A보다 6개 적고 0.315m 낮음 |
| 0.7×0.4×0.25m, 3.008kg | A 12개, 회전 B 18개, C 15개; B 선정 |
| 0.601×0.601×0.8m | A/B 2개, C 1개; 이전 C 선정에서 A 선정으로 변경 |
| 한 층만 가능한 화물 | C도 최소 1층; 동일 조건이면 A 우선 |
| 모든 후보 불가 | 추천 없음과 제외 사유 표시 |
| 무게 누락 | 조건부 예상안, 하중 확인 필요; AR에서도 동일 안내 |

대표 분류 10종의 렌더링 검사 결과는 A/B/C = 8/2/0이다. C는 B의 한 층을 줄이는 구조이므로 수량이 적거나 같고 일반적으로 최종 추천되지 않는다. 별도의 필요 수량·작업 높이 제한이 없는 현 구조에서 이를 오류로 취급하거나 인위적으로 보정하지 않는다.

## UI·AR 보존

공통 색상·타이포·카드·내비게이션·스크롤·상세 접기/펼치기를 유지했다. 추천 카드에는 수량·층수·총 높이·바닥면 활용률, 비교표에는 높이 여유·하중 상태, 상세에는 수식·입력·가정·미검증 항목을 표시한다. 활성 추천 코드에서 기존 가중 점수와 설명을 제거했다.

추천 화면과 AR은 `WarehouseStrategySession` / `WarehouseStrategyCalculator`의 동일 계산 경로와 `StackingPlan`을 사용한다. 전략 번호 전달과 1회 소비, 수량·층수·높이·무게·방향 일치를 테스트했다. AR 최대 512개 가상 화물 표시 제한은 유지하며 계산 수량과 렌더링 수량을 구분한다. 홈·측정·적재·추천 화면, YOLO·Gemini, 기존 `ShowPlans` 관련 수정은 제거하지 않았다.

## 이번 작업 수정 파일

- `Assets/Scripts/Data/StackingCalculator.cs`: 누락 무게 상태, 미소 양수 무게 하중 제한.
- `Assets/Scripts/Data/ProductSpecTable.cs`: 추천용 무게 조회에서 범용 fallback 제외.
- `Assets/Scripts/Data/WarehouseRecommendation.cs`: 누락 무게와 잘못된 무게 입력 구분.
- `Assets/Scripts/Data/WarehouseStrategyComparison.cs`: 사전식 선정, 계획 검증, 공간 활용률·높이 여유, 조건부 하중 상태.
- `Assets/Scripts/UI/WarehouseRecommendationView.cs`: 기존 UI 구조 안에서 지표·근거·하중 상태·표준 참고 한계 표시.
- `Assets/Scripts/Features/ARMainPalletStackPreview.cs`: 누락 무게 안내.
- `Assets/Editor/WarehouseStrategyChecks.cs`: 기존 검사 유지·정책 기대값 변경 및 경계 사례 추가.
- `Assets/Editor/RecommendationUIReview.cs`: 무게 누락 표시와 구 점수 문구 부재 검사 추가.
- `Assets/Tests/PlayMode/HomePopupChecks.cs`: 기존 옛 추천 UI 테스트를 현재 비교표·상세 토글 검사로 갱신.
- `LogisticsRecommendationReview.md`, `Artifacts/LogisticsRecommendation/`: 보고서와 검증 산출물.

## 검증 결과

- C# 빌드: `dotnet build WiseStack2.sln --no-restore -m:1 -p:UseSharedCompilation=false -clp:ErrorsOnly`, 오류 0, 경고 2.
- Unity Edit Mode: **30/30 통과**, 기존 17개 포함. 작업 ID `78f0464d36c14e129db6b0a09021216f`.
- Unity Play Mode: **6/6 통과**, 기존 전략 검사 4개와 UI 검사 2개. 작업 ID `b92732cd81c144628f81b921a24d6d8b`.
- 최초 Play Mode 실행은 옛 `StrategyChoices` UI를 찾는 테스트 1개가 실패했다. 기존 런타임 UI 구조에 맞춰 비교표·상세 토글 검사로 갱신한 뒤 전체 재실행을 통과했다.
- `Tools/Test-Stacking.ps1`: 통과. 높이·하중·0 하중·방향·큰 화물·비정상 입력 및 45개 조합, 높은 천장 회귀 검사 포함.
- `Tools/Test-UnityIntegrity.ps1`: 통과. 167개 asset GUID, 메타·YAML 참조·JSON·충돌 표시·LFS 포인터 검사.
- 추천 UI: 720×1280, 720×1600, 1080×1920, 1280×720에서 글자 영역, 접기/펼치기, 스크롤, 가상 Safe Area, 추천 없음, 무게 누락 표시 검사 통과. 720×1280 추천·상세 PNG를 직접 확인했다. [검사 기록](Artifacts/LogisticsRecommendation/ui-validation.txt), [추천 화면](Artifacts/LogisticsRecommendation/720x1280-collapsed.png), [계산 상세](Artifacts/LogisticsRecommendation/720x1280-details.png).
- UI 실행 도구의 인메모리 C# 실행은 Windows 명령 길이 문제로 실패하여 기존 Unity 메뉴로 같은 검사를 실행해 통과했다.
- 이번 수정 소스의 `git diff --check`는 통과했다. 전체 작업 트리에는 기존 `Assets/Scenes/MeasureScene.unity:965`의 trailing whitespace가 남아 있으며 해당 씬은 수정하지 않았다.
- Android 개발용 APK: **성공**, 오류 0·경고 990, 약 337.8초. 작업 ID `build-6a20f33abe`. [APK](Builds/LogisticsRecommendation/WiseStack2.apk), 실제 파일 크기 111,854,188 bytes (약 106.7 MiB). 도구의 전체 빌드 산출물 용량과 APK 파일 크기를 구분했다. 경고 전체를 분류·해소한 것은 아니다.
- 빌드 후 XR 임시 파일이 복원됐고 `Assets/XR/Temp`, 임시 `Assets/Resources` 및 테스트 실행용 EditorSettings 변경이 Git 상태에 남지 않았음을 확인했다.

## 미검증 범위와 후속 개선

이번 변경으로 실제 Android 기기에서 YOLO 인식, Gemini 호출, ARCore 바닥 인식·배치, 카메라·터치·권한·성능을 검증하지 않았다. Editor 렌더링과 APK 빌드는 실기기 성공을 뜻하지 않는다. 실제 무게 입력·제조사 허용하중 출처·부분층 및 혼합 회전·창고 실제 형상·포장 압축강도·고정·동적 하중은 향후 개선 대상이다.
