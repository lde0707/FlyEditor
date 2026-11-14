# 블렌더 방식 키패드 시점 — 2026-11-13

## 조작
- 키패드 1 / 3 / 7: 정면 / 오른쪽 / 위. Ctrl을 함께 누르면 뒷면 / 왼쪽 / 아래. 축 보기로 이동하면 직교 투영이다.
- 키패드 2 / 4 / 6 / 8: 15° 회전. Ctrl 조합은 화면 이동, Shift+4 / 6은 15° 화면 기울이기다.
- 키패드 5: 원근 / 직교 전환. 축 고정 보기에서 회전을 시작하면 블렌더 기본 Auto Perspective처럼 원근으로 전환된다. 자유 직교 보기에서 회전할 때는 직교를 유지한다.
- 키패드 9: 축 보기의 반대편. 자유 시점에서는 높이를 유지하며 수평으로 180° 회전한다. 현재 투영 모드는 유지한다.
- 키패드 + / −: 확대 / 축소. 방향·확대 키를 누르고 있으면 반복된다.
- 키패드 .: 현재 접힐 면 또는 선택한 점에 초점. 선택이 없으면 전체 종이에 맞춤.
- Home: 전체 종이에 맞춤.

화면 왼쪽 위에 현재 방향과 직교/원근을 표시하며, 작업대 아래에 조작키를 적었다. 기존 마우스 우클릭 회전과 휠 버튼 이동도 사용할 수 있다. 게임 작업대의 시점 탐색 키를 맞춘 것으로, Blender의 카메라 오브젝트 편집·렌더 카메라 전환(0), 로컬 뷰(/) 기능을 새로 만든 것은 아니다.

## 기준
- [Blender 공식 기본 내비게이션 설명](https://studio.blender.org/training/blender-fundamentals-45-lts/blender_4-5_lts_viewport-navigation/)
- [Blender 공식 Navigation](https://docs.blender.org/manual/en/4.0/editors/3dview/navigate/navigation.html)
- [기본 키맵](https://github.com/blender/blender/blob/main/scripts/presets/keyconfig/keymap_data/blender_default.py)
- [View Orbit 구현](https://github.com/blender/blender/blob/main/source/blender/editors/space_view3d/view3d_navigate_view_orbit.cc)

Unity의 Y-up 좌표에 맞춰 정면·측면·위 방향을 대응시켰다. 별도 설정이 가능한 Blender의 모든 개인화 옵션을 그대로 가져온 것은 아니다. 게임의 기존 확대 범위는 유지한다.

## 공통 선택 보완
원근 카메라의 시선으로 표면 앞뒤와 점 표시 위치를 판정한다. 박스 선택은 직교 깊이 또는 원근의 음의 역깊이 평면을 이용해 가려진 영역을 제외한다. 카메라 가까운 평면에서 잘린 부분은 투영 전 클리핑하여 눈 뒤의 종이가 화면 반대쪽으로 뒤집혀 선택되지 않도록 했다. 종이 형상과 접기 충돌 규칙은 바꾸지 않았다.

## 검증
- BlenderFoldViewVerification PASS: 투영 왕복, 정면/뒷면/좌/우/위/아래, 축/자유 시점 반대, 회전 방향, Auto Perspective, Ctrl 이동, Shift 기울이기, 확대, 선택/전체 맞춤.
- 원근 선택/접기 4건 및 near-plane 가림 판정 PASS. 보이는 겹과 전체 겹, 비스듬한 카메라, 면적·이음새·재생.
- BoxSelectionVerification PASS: 42건 / 196개 자세. 기존 날개 검사 64개 순서 / 3968개 자세와 180° 접기 16개 순서도 통과.
- 실제 플레이 입력: 25개 캡처. 기본/반대 축 보기, 원근과 반대 보기의 투영 유지, 회전/이동/기울이기/확대/키 반복/Home을 확인했다. 키 입력 전후 종이 재질 좌표가 그대로 유지됐다.
- 실제 원근 보기에서 V 박스로 윗겹만 선택한 뒤 26° 드래그 접기와 저장/재생 PASS. 렌더링된 초록 면적과 실제 이동 면적을 비교하고 독립 재질 좌표 검사로 아랫겹 고정을 확인했다.
- 실제 . 선택 맞춤은 자동 API 검사이며, 실제 키/마우스 입력 캡처와 별도로 분류한다. 모든 키보드/OS 구성 및 모든 고난도 종이 모형을 검증했다는 의미는 아니다.
- 시작 종이는 공통 API로 준비한 1회 반 접기 저장본이다. 사용자 저장 파일을 건드리지 않고 격리 임시 저장을 사용했다.

## 재현
- BlenderFoldViewVerification.Run: 시점/선택 회귀.
- BlenderViewPlayerVerification.Run: FLYEDITOR_BLENDER_PLAYER_ROOT에 이 폴더를 지정. API 회귀와 실제 기록된 입력 결과를 검증한다.
- core-final/result.json: 최종 기본 종이접기 기술 회귀 결과.

입력 원문은 session-a/input.jsonl, 이미지와 카메라 상태는 full-*.png 및 geometry-*.json, 접기와 저장은 recipe-*.json 및 progress-024.json이다. 도움말 문구를 짧게 정리한 뒤 최종 소스로 회귀 검사를 다시 실행했다.
