# 프로젝트 병합 후 점검

Unity 버전: `ProjectSettings/ProjectVersion.txt` 기준.

프로젝트 루트에서 실행:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Test-UnityIntegrity.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Test-Stacking.ps1
```

첫 검사는 메타 파일, GUID 참조, 충돌 표시, 미복원 LFS 포인터와 패키지 JSON을 확인합니다.
패키지 참조 검사를 위해 Unity에서 패키지 복원이 완료되어 있어야 합니다. `rg`가 필요합니다.
두 번째 검사는 적재 계산과 과도한 높이 입력의 회귀를 확인합니다.
이 검사만으로 Unity 컴파일 또는 Android 빌드 성공을 보장하지는 않습니다.

Unity 컴파일 완료 후 ARMain을 Play Mode로 열고 다음 메뉴를 실행합니다.

- `WiseStack/Review/Check active stacking flow`
- `WiseStack/Review/Check box rendering`

두 메뉴는 임시 측정값으로 검사합니다. 실행 후 Play Mode를 종료하세요.
HomeScene, ARMain, MeasureScene, DangerScene의 Missing Script/Prefab 여부도 확인하세요.

## 유지해야 할 동작

- 측정과 적재는 `StackingCalculator`를 함께 사용합니다.
- 기본 창고 높이는 3m, 시뮬레이션 높이 상한은 팔레트 포함 1.8m입니다.
- 적재 UI는 `StackingPreviewUI` 한 구현을 사용합니다. 옛 `LayoutPreference` UI와 섞지 마세요.
- Android는 ARCore, PC 에디터는 SimulationLoader를 사용합니다.
- XR의 `m_InitManagerOnStart: 1`을 유지합니다. `XRGeneralSettings`가 초기화와 종료를 담당합니다.
  하위 XRManager의 `m_AutomaticLoading/Running: 0`은 초기화 중복과 미초기화 OnDisable 호출을 막습니다.
  현재 설치된 XRGeneralSettings도 초기화 시 이 두 값을 false로 설정합니다.

Android 카메라, 실제 바닥 인식, 외부 AI 호출은 별도로 기기에서 검증해야 합니다.
빌드 백업 폴더(`*_BackUpThisFolder_ButDontShipItWithYourGame`)는 실행 소스가 아닙니다.
이미 Git에 추적된 백업 파일은 `.gitignore`만 추가해도 추적이 해제되지 않습니다.
