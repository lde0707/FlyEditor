# FlyEditor 작업 지침

- Unity 프로젝트 루트는 `FlyEditor/`이며, 직접 작성하는 게임 코드와 에셋은 `FlyEditor/Assets/_Project/`에 둡니다.
- Unity 버전은 `FlyEditor/ProjectSettings/ProjectVersion.txt`를 따릅니다. 현재 개발 대상은 PC입니다.
- 스크립트 작성·수정 전 [FSM 및 최적화 규칙](.cursor/rules/unity-fsm.mdc)을 읽고 적용합니다. 이 규칙은 해당 편집기 기능의 지원 여부와 관계없이 저장소 작업 지침입니다.
- 앞으로 개발하는 상태 기반 게임 로직은 FSM과 상태별 함수로 구성하고, 반복 연산·GC 할당을 줄입니다. 성능 개선 주장은 실제 측정 결과를 근거로 합니다.
- 에셋의 `.meta`와 GUID를 보존하고, `Library`, `Temp`, `Logs`, `UserSettings`, `Builds`는 커밋하지 않습니다.
- 사용자의 최신 명시적 요구를 우선하며, 현재 작업과 무관한 변경은 보존합니다.
- 검증은 변경 범위에 맞게 수행합니다. 단순 설정·문서 변경은 값과 diff를 확인하고, 게임 로직·빌드 구조 변경은 컴파일·씬·관련 동작을 검증합니다.
- 폴더 구조와 개발 메뉴는 [개발 안내](Docs/DEVELOPMENT.md)를 참고합니다.
