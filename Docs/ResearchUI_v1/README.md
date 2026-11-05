# FlyEditor Research UI Kit v1

2026-11-04 · 1920×1080 기준 · 흰 바탕 / 검정 선 / 각진 프레임 / 옅은 파스텔 환경

## 결과물

- `Previews/Full_Web_1920x1080.png`: 증강 20개와 중앙 시작점 전체를 표시한 기획용 보기.
- `Previews/Current_3_Nodes_1920x1080.png`: 현재 실제 구현 범위인 A1·G1·C1만 표시한 배치 제안. 실제 게임 캡처가 아닌 에셋 조합 시안.
- `Previews/Asset_Overview.png`: 아이콘·버튼·프레임 전체 확인용.
- `Previews/Generated_Style_Concept.png`: 내장 이미지 생성 도구로 만든 아트 시안. 정확한 연결 규칙은 layout.json이 기준.
- `Sprites/`: Unity에서 독립적으로 사용할 PNG 43개와 각각의 TextureImporter `.meta`.
- `Vectors/`: 프레임·심볼의 SVG 원본 42개. Unity에 별도 SVG 패키지를 설치할 필요 없이 PNG를 사용하면 된다.
- `layout.json`: 고정 위치, 주 경로, 전체 선행 조건, 스프라이트 크기와 9-slice border.
- `prompts.json`, `preview-prompt.txt`: 생성형 아트에 사용한 프롬프트.

## PNG 분류

| 구분 | 파일 | 개수 |
|---|---|---:|
| 배경 | Background_Research.png | 1 |
| 증강 아이콘 | Icon_A1~P3 | 20 |
| 중앙 비행기 | Icon_Root.png | 1 |
| 보조 심볼 | Icon_Check / Close / Currency | 3 |
| 버튼 | Button_Normal / Hover / Pressed / Disabled / Primary | 5 |
| 설명창 | Panel_Background / Panel_Dividers | 2 |
| 노드 프레임 | Square / Diamond / Hexagon 및 각 Selected | 6 |
| 단계 표시 | Rank_0~3 | 4 |
| 설명창 기체 그림 | Plane_Preview.png | 1 |

모든 프레임·아이콘은 배경이 투명하며, 버튼·패널·노드의 내부는 흰색 또는 검정 면이다. 배경 PNG는 불투명이다. 텍스트는 PNG에 포함하지 않는다. 상태·수치·설명은 Unity 텍스트로 표시한다.

## Unity 사용

1. Sprite PNG와 `.meta`를 함께 `Assets/_Project/UI/ResearchUI_v1/`에 둔다.
2. PNG는 Sprite (2D and UI), Single, Full Rect, Bilinear, Clamp, 압축 없음, Mipmap 없음 기준이다.
3. 버튼과 패널은 Unity UI Image의 Type을 **Sliced**로 사용한다. 버튼 Border는 12px, 패널은 10px로 지정했다.
4. 노드 프레임과 심볼은 별도 Image로 겹친다. 128px 프레임을 약 76~94px, 128px 심볼을 약 45~52px에 표시한 시안을 제공한다.
5. 버튼은 Sprite Swap으로 상태를 바꾼다. 검정 버튼 위 텍스트는 흰색, 다른 상태는 검정/회색을 사용한다.
6. `Panel_Dividers`는 선택적으로 덧댄다. 자유로운 높이 배치가 필요하면 구분선은 Unity Image로 직접 그리는 편이 좋다.
7. 배경은 화면에 맞춰 표시하되 비율을 유지한다. 생성된 원본 배경은 1672×941이며 완성 배치 시안은 1920×1080이다.

현재 `PaperFlightController.OnGUI()`는 프레임과 텍스트를 코드로 직접 그린다. 이 파일들을 임포트하는 것만으로 기존 화면이 자동 교체되지는 않는다. 이 작업은 분리형 리소스와 배치 자료를 제공하며 런타임 UI 연결 코드를 변경하지 않는다.

## 노출과 연결 규칙

- 실제 플레이: 구매한 노드와 모든 선행 조건을 만족한 후보만 노출. 미래 노드·선·물음표는 숨김.
- 현재 프로젝트: 연구는 첫 비행 완료 후 열리며, A1·G1·C1 세 종류만 구현됨. S1은 HP/충돌 모델 도입 전까지 가짜 후보로 표시하지 않음.
- 전체 보기: 검토용이므로 20개가 모두 표시됨. 전문화와 고급 연구가 현재 구현되었다는 의미는 아님.
- 웹에는 아이콘만 배치. 이름·효과·비용은 오른쪽 선택 패널에 표시.
- 주 경로만 선으로 그린다. 교차 연구의 추가 선행 조건은 layout.json의 prerequisites를 확인하며 AND 조건으로 판정.
- 숨긴 노드가 나타날 때 기존 노드 좌표는 유지.

## 제작·검수 범위

배경과 스타일 콘셉트는 내장 image_gen으로 제작했다. 재사용할 버튼·프레임·심볼은 선 굵기와 모서리를 정확히 맞춘 벡터 도형으로 정리하고 PNG/SVG로 내보냈다. `build_assets.py`는 이를 재현하는 도구다. 생성 과정의 초안은 `Sources/`에 보존하며, 실제 사용할 최종 스프라이트는 `Sprites/`만 사용한다.

PNG 파일 수·크기·투명도·메타 GUID 중복·노드와 아이콘 대응, 완성 배치 이미지의 글꼴과 잘림을 확인했다. 기존 사용자의 씬·스크립트·저장 데이터는 수정하지 않았다. 런타임 Sprite 교체 동작과 게임패드/키보드 탐색은 구현하지 않았으므로 검증 범위에 포함되지 않는다.
