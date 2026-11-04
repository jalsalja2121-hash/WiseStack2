# WiseStack2 전체 UX/UI 리디자인

## 조사와 기존 문제

Unity 6000.3.14f1의 uGUI 프로젝트이며 HomeScene, MeasureScene, ARMain, DangerScene이 빌드에 포함되어 있다. 720×1280 너비 기준 CanvasScaler와 런타임 UI 생성 코드를 사용한다. 화면 회전은 기존 세로 정책을 유지했다.

기존 화면의 문제는 큰 제목과 버튼, 무거운 텍스트형 하단 탭, 화면별로 다른 여백과 글자 크기, 큰 고정 높이 패널이었다. 홈은 창고와 팔레트 입력의 계층이 약했고 오류가 입력 항목과 떨어져 있었다. 측정은 대기 상태에서도 큰 결과 패널을 사용했다. 적재는 상단 패널이 카메라 영역을 크게 가렸다.

실제 씬을 PreviewScene으로 열고 기존 컨트롤러의 UI 초기화 코드를 실행해 변경 전후를 캡처했다. 이 캡처는 실제 Unity UI이며, 측정/AR 배경은 중립적인 Editor 배경이다. 실기기 카메라 또는 현장 적재 사진으로 제시하지 않는다.

## 디자인 시스템

- 네이비 #14243A, 브랜드 블루 #1769D2, 배경 #F5F7FB, 흰색 Surface, 본문 #172638.
- 보조 글자색은 제안된 회색보다 진한 #56677C로 조정했다. 성공·경고·오류 상태용 색상도 정의했다.
- 720 논리 단위 기준 제목 34, 섹션 28, 본문 26, 보조 22, 버튼 28. 한국어 줄바꿈을 사용하고 임의의 폰트 축소를 피한다.
- 8 기반 간격, 좌우/카드 패딩 24, 카드 사이 16. 공통 둥근 Surface와 96 이상의 주요 버튼 높이.
- 하단 탭은 높이 104, 실제 각 탭 터치 영역은 높이 96. 기본 uGUI 선 도형 아이콘과 짧은 텍스트를 사용한다.
- WiseStackUI는 카드, Surface, 버튼, 텍스트, 가로/세로 레이아웃, ScrollRect, 페이지 헤더, Safe Area, 내비게이션을 제공한다. 별도 이미지 다운로드나 폰트 아이콘 의존성이 없다.
- 선택·누름·비활성 상태를 ColorTint로 구분한다. 불필요한 움직임을 추가하지 않았다.

## 화면별 변경

| 화면 | 변경 전 | 변경 후 |
| --- | --- | --- |
| 홈 | 큰 브랜드 헤더, 단일 설정 카드, 멀리 떨어진 오류 안내 | 작업 단계, 창고/팔레트 그룹, 입력 옆 단위, 해당 입력 아래 오류, 기본/수정/확인 상태, 주요 다음 단계 버튼 |
| 측정 | 큰 고정 결과 박스, 큰 액션 바 | 카메라 중심 공간, 작은 작업 상태, 내용에 따른 결과 높이, 명시적 가로·세로·높이·기준 무게, 추정값 안내, 분석/완료/실패 상태 |
| 적재 | 498 높이의 상단 요약, 큰 전략/액션 버튼 | 216 높이 요약, 작은 A/B/C 선택, 명확한 선택 색, 96 높이 액션, 생성 중 중복 실행 방지 |
| 추천 | 사각 카드, 테두리와 큰 색상 블록 | 둥근 Hero·지표·비교 카드, 완료 상태, 배경 통일, 공통 버튼과 아이콘 탭 |

홈의 기존 InputField와 음성 입력 버튼 객체는 재사용해 Inspector 및 음성 컨트롤러 연결을 보존했다. 새 필수 입력 오류는 화면의 항목 옆에 나타나며 잘못된 값으로 다음 단계로 이동하지 않는다. 음성 입력은 스크롤 아래의 별도 카드에서 계속 사용할 수 있다.

측정의 YOLO/Gemini 호출과 분석 계산은 유지했다. 분류 후 저장되는 값은 분류별 대표 규격이며 사용자가 실측·확정한 수치라고 표현하지 않는다. Gemini 실패 시 기존 다시 분석 기능과 오류 상세를 유지했다.

적재는 기존 배치·다시 배치·전략 전환 이벤트를 그대로 사용한다. 상자 생성 중 주요 버튼은 비활성화하고 완료 시 다시 활성화한다. 카메라/AR Foundation 처리와 512개 표시 제한은 유지했다.

추천의 75:25 평가, 후보 제외, 동률 처리 및 AR 전달 경로는 변경하지 않았다. WarehouseStrategyComparison.cs의 작업 전후 SHA-256은 모두 3A26EBE1C5460043DB89E9D6F947B4C6D0A72AD987193B1597CBAF993618CEFE이다.

## 캡처

| 화면 | 변경 전 | 변경 후 |
| --- | --- | --- |
| 홈 | [이전](Artifacts/UIReview/before-HomeScene-720x1280.png) | [개선](Artifacts/UIReview/after-HomeScene-720x1280.png) |
| 측정 | [이전](Artifacts/UIReview/before-MeasureScene-720x1280.png) | [개선](Artifacts/UIReview/after-MeasureScene-720x1280.png) |
| 적재 | [이전](Artifacts/UIReview/before-ARMain-720x1280.png) | [개선](Artifacts/UIReview/after-ARMain-720x1280.png) |
| 추천 | [이전](Artifacts/UIReview/before-DangerScene-720x1280.png) | [개선](Artifacts/UIReview/after-DangerScene-720x1280.png) |

720×1280, 1080×1920, 1080×2400, 1440×3200의 각 화면 PNG를 같은 폴더에 저장했다. 첫 렌더링에서 누락된 탭 아이콘, 측정의 빈 공간, 추천의 외곽 배경을 발견하고 수정한 뒤 다시 렌더링했다.

재실행 메뉴: WiseStack/Review/Capture product after. 새 결과는 Temp/ProductUIReview에 저장된다.

## 검증 결과

- 전체 솔루션 dotnet build 성공: C# 오류 0, 기존 참조 버전 충돌 경고 2.
- Unity Editor 재컴파일 후 C# 오류 없음.
- 추천/계산 Edit Mode 17/17, 기존 Play Mode 4/4 통과.
- 4개 화면 × 4개 해상도 렌더링 및 활성 텍스트 높이 검사 통과.
- 각 해상도에서 탭 아이콘 존재와 최소 터치 영역 검사 통과.
- 콘텐츠가 화면보다 길 때 ScrollRect를 하단까지 이동할 수 있음을 확인.
- 홈의 필수 면적 입력이 비었을 때 저장 거부 및 해당 항목의 오류 표시 확인.
- 추천 상세의 기본 접힘, 펼침, 다시 접힘 콜백 확인.
- 기존 4개 씬의 빌드 포함 상태와 컨트롤러/버튼 연결을 코드·실제 씬에서 확인.
- 관련 수정 파일 git diff --check 통과.
- 전체 작업 트리 검사에서는 기존 MeasureScene.unity:965의 trailing whitespace가 보고된다. 이번 작업에서 씬 YAML을 변경하지 않았으므로 그대로 보존했다.
- Android 개발용 APK 빌드 성공: 오류 0, 경고 3,361, 소요 시간 약 6분 32초. 경고 전체를 분류·해소한 상태는 아니다.
- 생성 APK: [WiseStack2.apk](Builds/UIReview/WiseStack2.apk), 실제 파일 크기 83,069,818 bytes (약 79.2 MiB). 빌드 작업 ID: build-dc483cf52d.
- Android 빌드 중 임시 이동된 XR Simulation 파일은 빌드 종료 후 복원되었으며 관련 삭제/임시 파일이 Git 상태에 남지 않았다.

빌드 명령: `dotnet build WiseStack2.sln --no-restore -m:1 -p:UseSharedCompilation=false -clp:ErrorsOnly`.
UI 검사 기록: [validation.txt](Artifacts/UIReview/validation.txt).

## 수정·추가 파일

- Assets/Scripts/UI/WiseStackTheme.cs: 공통 색상·타이포·간격 토큰.
- Assets/Scripts/UI/WiseStackUI.cs 및 .meta: 공통 uGUI 컴포넌트, 아이콘 내비게이션.
- Assets/Scripts/UI/HomeSetupView.cs 및 .meta: 홈 레이아웃과 항목별 오류.
- Assets/Scripts/UI/HomeInputController.cs: 새 표시 계층 연결, 필드별 오류, 다음 단계 검증.
- Assets/Scripts/UI/StackingPreviewUI.cs: AR 요약·전략·액션 레이아웃.
- Assets/Scripts/UI/WarehouseRecommendationView.cs: 공통 Surface·버튼·탭과 시각적 계층.
- Assets/Scripts/Features/MeasureSceneController.cs: 결과 표시, 상태, 화면 배치.
- Assets/Scripts/Features/ARMainPalletStackPreview.cs: 생성 중 버튼 상태.
- Assets/Scripts/Navigation/SceneNavigator.cs: 공통 탭 및 Safe Area 갱신.
- Assets/Editor/ProductUIReview.cs 및 .meta: 실제 씬 캡처와 UI 검사.
- ProductUIReview.md, Artifacts/UIReview/: 보고서·검사 기록·PNG.

## 미검증 범위와 남은 한계

- 실기기 한글 폰트 대체, 실제 손가락 터치, 소프트 키보드, 노치/시스템 바는 기기에서 직접 확인하지 않았다. 캡처의 한글은 Unity Editor에서 확인했다.
- 새 UI에서 실제 YOLO 카메라 인식, Gemini/음성 API 통신, ARCore 평면 인식과 현장 배치는 재실행하지 않았다.
- 실제 화면 전환을 끝까지 조작하는 단말 테스트는 하지 않았다. 씬 포함/연결과 데이터 계획 일치 테스트를 확인했다.
- 모든 오류·권한 거부·네트워크 지연 상태를 렌더링한 것은 아니다. 기존 예외/재시도 경로를 유지하고 해당 UI 상태를 구현했다.
- Safe Area는 공통 코드에서 상하좌우를 반영하며 화면 크기 변경에 재배치한다. 요청한 해상도의 Editor 캡처는 실제 기기의 Safe Area 검증을 대체하지 않는다.
- 가로 모드 지원을 새로 추가하지 않았으며 기존 세로 화면 정책을 유지했다.

## Git 보호

기존 미커밋 변경과 미추적 파일을 보존했다. 이번 디자인 작업에서 씬 YAML을 저장하거나 기존 추천 계산 파일을 수정하지 않았다. 이전 ShowPlans 컴파일 수정도 유지했다. commit, merge, push는 실행하지 않았다. 변경 사항은 모두 미커밋 상태다.
