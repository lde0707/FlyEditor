# FlyEditor 개발 안내

Unity 6000.5.3f1 · URP · PC 게임 · 1인 개발

## 시작 위치

Unity Hub에서 저장소 내부 `FlyEditor/`를 엽니다. 작업 시작 씬은 `Assets/_Project/Scenes/Main.unity`입니다. 기존 SampleScene의 GUID와 오브젝트를 보존해 이름과 위치만 변경했습니다. 현재 씬은 카메라와 조명이 있는 URP 기본 씬이며 게임 로직은 아직 없습니다.

## 폴더 사용법

```text
FlyEditor/Assets/
├── _Project/                  직접 만드는 게임 작업물
│   ├── Animations/            애니메이션 클립·컨트롤러
│   ├── Art/
│   │   ├── Materials/
│   │   ├── Models/
│   │   ├── Shaders/
│   │   ├── Sprites/
│   │   ├── Textures/
│   │   └── VFX/
│   ├── Audio/{Music,SFX}/
│   ├── Data/                  ScriptableObject 등 게임 데이터
│   ├── Prefabs/{Gameplay,UI}/
│   ├── Scenes/Main.unity
│   ├── Scripts/
│   │   ├── Runtime/{Core,Gameplay,UI}/
│   │   └── Editor/            개발 도구만 보관
│   ├── Settings/
│   │   ├── Input/             Input System 액션
│   │   └── Rendering/         기존 URP·품질·볼륨 에셋
│   └── UI/{Fonts,Icons}/
├── _ThirdParty/               외부 에셋·플러그인
├── TutorialInfo/              Unity 템플릿 도움말 원본
└── Readme.asset               Unity 템플릿 도움말 원본
```

- 기능 코드는 `Runtime/Gameplay/기능명`으로 묶고, 여러 기능에서 실제로 공유하는 코드만 `Core`로 옮깁니다.
- 네임스페이스는 `FlyEditor`로 시작합니다. 기능이 커지기 전에는 Unity 기본 어셈블리를 사용합니다. 패키지 참조를 별도로 연결해야 하는 asmdef 분리는 필요해질 때 도입합니다.
- 편집기 전용 코드는 `Editor` 폴더에 둡니다. 런타임 코드에서 `UnityEditor`를 참조하지 않습니다.
- 새 씬·프리팹·스크립트는 PascalCase로 이름을 붙입니다. `.meta`를 함께 커밋하고 에셋 이동은 Unity Project 창에서 합니다.
- 빈 폴더의 `.gitkeep`은 복제 후에도 폴더를 유지하기 위한 파일입니다.
- 외부 에셋은 원본을 보존하고 아래 출처 기록에 제품명, 출처 링크, 라이선스, 버전을 적습니다. 고정 설치 경로가 필요한 패키지는 공급자 안내를 따릅니다.

## 적용한 기본 설정

| 항목 | 설정 |
|---|---|
| Company / Product | lde0707 / FlyEditor |
| 버전 / PC 식별자 | 0.1.0 / com.lde0707.flyeditor |
| PC 초기 실행 | 1920×1080, 창 모드, 창 크기 조절 가능 |
| 백그라운드 실행 | 끔 |
| 직렬화 / 버전 관리 | Force Text / Visible Meta Files |
| 새 C# 줄바꿈 / 기본 네임스페이스 | LF / FlyEditor |
| Play Mode | 도메인·씬 모두 다시 로드 |
| 빌드 씬 | Main 하나를 첫 씬으로 등록 |
| 렌더링 / 입력 | 기존 URP·Linear / Input System 유지 |

PC의 해상도와 창 모드는 초기값입니다. 실행 후 사용자가 변경한 플레이어 환경 설정이 적용될 수 있습니다. 품질 프리셋, 물리, 태그·레이어, 프레임 제한은 게임 설계가 정해진 뒤 조정합니다. 모바일·WebGL 설정은 아직 맞추지 않았습니다.

## 개발 메뉴

Unity 상단 `Tools > FlyEditor`:

- **Open Main Scene**: 변경한 씬의 저장 여부를 확인한 뒤 Main을 엽니다.
- **Open Save Data Folder**: `Application.persistentDataPath`를 엽니다. 저장 시스템 자체를 구현하는 기능은 아닙니다.
- **Validate Project**: Main 씬과 공유 빌드 씬 목록, 직렬화 상태를 확인합니다. 게임 동작 검사는 별도로 해야 합니다.
- **Build Windows Development**: 공유 빌드 씬 목록으로 Windows 64비트 개발 빌드를 만듭니다. 출력은 `FlyEditor/Builds/Windows/FlyEditor.exe`이며 Git에서 제외합니다. 기존 개발 빌드 출력을 갱신합니다. 배포용 Release 빌드는 별도로 설정해야 합니다.

빌드 메뉴는 활성 Build Profile의 전용 씬 목록 대신 공유 `EditorBuildSettings` 씬 목록을 사용합니다. 초기에는 공유 목록으로 작업합니다.

## 작업 순서

1. Main 씬 또는 작업 중인 씬을 열고 작은 기능 하나를 구현합니다.
2. Play Mode에서 시작·종료·재시작을 확인하고 Console 오류를 정리합니다.
3. 씬과 프리팹을 저장하고 `Validate Project`를 실행합니다.
4. 기능 단위로 Windows 개발 빌드에서도 확인합니다.
5. Git 변경 파일을 확인해 코드·에셋·메타·설정만 커밋합니다. `Library`, `Logs`, `UserSettings`, `Builds`는 제외합니다.

비밀 키와 로그인 토큰을 Assets, ScriptableObject, 저장소에 넣지 않습니다. 원본 대용량 아트 파일은 추가 전 Git LFS 적용을 검토합니다.

## 외부 에셋 출처

| 에셋 | 출처 | 이용 조건 / 버전 |
|---|---|---|
| URP 기본 템플릿 및 패키지 | Unity Hub / Unity Package Manager | 설치한 Unity·패키지 이용 조건, 버전은 Packages 파일 참조 |

## 참고

- [Unity 특수 폴더와 Editor 코드](https://docs.unity.com/en-us/engine/6000.0/manual/get-started/project-configuration/special-folders)
- [Unity 어셈블리 개요](https://docs.unity.com/en-us/engine/6000.3/manual/programming-environment/script-compilation/assembly-definition-files/assembly-definitions-intro)

## 스크립트 설계 및 최적화 원칙

앞으로 작성하는 상태 기반 게임 로직은 유한상태머신(FSM)을 기본으로 사용합니다. 상태의 진입·갱신·물리 갱신·종료는 `Enter`, `Tick`, `FixedTick`, `Exit`로 구분하고 전이는 한 경로에서 관리합니다. 상태 객체를 재사용하고, 반복 참조를 캐싱하며, 매 프레임 불필요한 연산과 GC 할당을 줄입니다. 최적화 효과는 Unity Profiler로 확인합니다.

상태 전이가 없는 순수 계산·데이터·Editor 명령은 독립 함수로 유지합니다. 상세 기준은 [FSM 및 최적화 규칙](../.cursor/rules/unity-fsm.mdc)에 기록하며, 저장소의 `AGENTS.md`에서도 같은 규칙을 참조합니다.
