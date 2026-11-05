# FlyEditor 선택·강화 버튼 상태

2026-11-04 · 기존 Research UI Kit v1의 SVG 스타일 확장

## 시각 규칙

- 후보: 흰 바탕, 검정 단일 테두리.
- 선택: 흰 바탕, 검정 이중 테두리.
- 강화 완료: 오른쪽 위 작은 체크. 단계만큼 하단 표시를 채움.
- 강화 후 선택: 체크·단계를 유지하면서 이중 테두리 추가.
- 최대 단계: 검정 바탕, 흰 심볼, 모든 단계 표시 채움.
- 최대 단계 선택: 검정 바탕을 유지하면서 바깥 선택 테두리 추가.
- 업그레이드 직후: 약 0.21초 동안 모서리의 짧은 검정 선이 바깥으로 이동하는 피드백 제안. 네온·발광·색상 점멸은 사용하지 않음.

선택 상태와 구매 단계는 서로 독립이다. 구매 성공의 기준은 저장 성공이며, 잔액 부족·저장 실패 때 완료 체크나 강화 연출을 재생하지 않는다.

## 분리 파일

| 분류 | 규칙 | 개수 |
|---|---|---:|
| 공용 프레임 | Frame_{Square/Diamond/Hexagon}_{Available/Selected/Owned/OwnedSelected/Max/MaxSelected} | 18 |
| 현재 구현 3종 완성형 버튼 | Node_{A1/G1/C1}_Rank{0/1/2/3} 및 _Selected | 24 |
| 구매 피드백 오버레이 | Feedback_Upgrade_0~2 | 3 |
| 설명창 구매 버튼 | Action_Ready / Pressed / Success / Max | 4 |
| 합계 | PNG와 동일 이름의 SVG 원본 | 49 |

PNG는 투명 배경이며 글자는 굽지 않았다. 완성형 Node 이미지는 프레임·아이콘·체크·단계 표시를 한 이미지로 합친 것으로 현재 구현된 기본 연구 3종에 해당한다. 단일 단계 증강이나 다른 최대 단계 수에는 공용 프레임을 사용하고 단계를 별도 표시한다.

Node와 Frame 캔버스는 160×192px이며 프레임 중심은 (80,80), 하단 단계 중심 높이는 171.5px다. 기존 128×128 프레임에 붙일 경우 프레임 중심 기준으로 정렬한다. 공용 프레임에는 심볼과 단계가 없다. 두 방식의 프레임·심볼을 중복으로 겹치지 않는다.

## Unity 적용

- Sprite / Single / Full Rect / Alpha Is Transparency / Mipmap Off / 압축 없음 메타를 포함했다.
- 새 리소스 폴더: Assets/_Project/UI/ResearchUI_States_v1.
- 노드 완성형은 Image Type=Simple. Canvas가 늘어날 때 비율을 유지한다.
- 설명창 Action 버튼은 14px border, Image Type=Sliced를 사용한다.
- 검정 Action 버튼 위 글자는 흰색, 흰 Action 버튼 위 글자는 검정색.
- 실제 구매 순간에는 Ready → Pressed → 저장 성공 → Success(짧게) → 다음 Ready 또는 Max로 표시한다.
- 구매 연출 이미지는 동일 위치의 별도 비상호작용 Image로 표시하고 Raycast Target을 끈다.
- 현재 UI는 IMGUI다. 이번 작업은 이미지와 상태 표현을 제공하며 상태 변경·구매 로직에 리소스를 연결하지 않았다.

## 파일 안내

- Previews/Selection_Upgrade_States.png: 세 가지 기본 연구와 버튼 상태 비교.
- Sprites/: 개별 PNG 49개와 .meta.
- Vectors/: 동일한 SVG 원본 49개.
- manifest.json: 크기·border·피드백 프레임 시간.
- build_states.py: 기존 SVG 아이콘을 재사용해 만든 상태 에셋 재현 도구. 실행에는 이전 research-ui-v1 폴더가 형제 경로에 있어야 한다.

이번 결과는 기존 코드 기반 벡터 자산의 확장이며 이미지 생성 도구를 추가 호출하지 않았다. 프레임 크기·투명도·파일 수·GUID 중복·비교 시안을 확인했다. 실제 버튼 동작과 애니메이션은 아직 연결·검증하지 않았다.
