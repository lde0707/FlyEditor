# FlyEditor

<!-- WINDOWS-RELEASE-START -->
## Windows에서 바로 플레이

**[FlyEditor · Windows 실행 파일 다운로드](https://github.com/lde0707/FlyEditor/releases/tag/portfolio-windows-2026-11-14)**

종이를 접어 비행체를 만들고 비행·연구를 이어가는 개발 중 프로젝트.

1. 위 Releases 페이지의 `FlyEditor-Windows-x64.zip`을 내려받습니다. `Source code` 파일은 실행 파일이 아닙니다.
2. ZIP 전체를 한 폴더에 압축 해제합니다.
3. `FlyEditor.exe`를 실행합니다. Unity 설치는 필요 없습니다.

`FlyEditor_Data`, `UnityPlayer.dll`, 나머지 동봉 폴더를 함께 유지해야 합니다. Windows 64비트용이며 모바일·macOS에서는 실행되지 않습니다.

**조작:** 마우스로 접기 도구와 메뉴를 선택합니다. 각 단계의 화면 안내를 따르세요.

[실행·검증 범위](PLAY.md) · [QA 포트폴리오](https://leedoeon.vercel.app/qa/index.html)
<!-- WINDOWS-RELEASE-END -->

Unity를 활용한 1인 게임 개발 프로젝트입니다.

## 현재 상태

`FlyEditor/` 폴더에 Unity 프로젝트가 있습니다.

- Unity 에디터: `6000.5.3f1`
- 렌더 파이프라인: URP

## 시작하기

1. 저장소를 복제합니다: `git clone https://github.com/lde0707/FlyEditor.git`
2. Unity Hub에서 Unity `6000.5.3f1`을 설치합니다.
3. Unity Hub에서 저장소 내부의 `FlyEditor/` 폴더를 프로젝트로 추가하고 엽니다.
4. 패키지 복원과 에셋 임포트가 끝나면 `Assets/_Project/Scenes/Main.unity`를 엽니다.

프로젝트 버전은 `FlyEditor/ProjectSettings/ProjectVersion.txt`, 패키지는 `FlyEditor/Packages/`에서 관리합니다.

## 버전 관리

- `Assets`와 함께 `.meta` 파일을 반드시 커밋합니다.
- `Packages`와 `ProjectSettings`를 커밋해 프로젝트 구성과 에디터 버전을 함께 관리합니다.
- `Library`, `Temp`, `Logs`, 빌드 결과물과 로컬 비밀 정보는 `.gitignore`로 제외합니다.
- 대용량 원본 아트·오디오 파일은 추가하기 전에 Git LFS 적용 여부를 결정합니다.
- 동작을 확인한 작은 변경 단위로 커밋합니다.

## 개발 방향

- 개발 형태: 1인 개발
- 엔진: Unity
- 플랫폼: PC (Windows 64비트 개발 빌드 기준)
- 장르: 추후 결정

외부 에셋을 사용할 때는 출처와 이용 조건을 함께 기록합니다.

## 개발 환경

폴더 구조, 기본 설정과 개발 메뉴는 [개발 안내](Docs/DEVELOPMENT.md)를 참고하세요.
