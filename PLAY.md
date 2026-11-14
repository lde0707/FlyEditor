# FlyEditor 실행 안내

<!-- WINDOWS-RELEASE-START -->
## Windows에서 바로 플레이

**[FlyEditor · Windows 실행 파일 다운로드](https://github.com/lde0707/portfolio-game-builds/releases/tag/flyeditor-2026-11-14)**

종이를 접어 비행체를 만들고 비행·연구를 이어가는 개발 중 프로젝트.

1. 위 Releases 페이지의 `FlyEditor-Windows-x64.zip`을 내려받습니다. `Source code` 파일은 실행 파일이 아닙니다.
2. ZIP 전체를 한 폴더에 압축 해제합니다.
3. `FlyEditor.exe`를 실행합니다. Unity 설치는 필요 없습니다.

`FlyEditor_Data`, `UnityPlayer.dll`, 나머지 동봉 폴더를 함께 유지해야 합니다. Windows 64비트용이며 모바일·macOS에서는 실행되지 않습니다.

**조작:** 마우스로 접기 도구와 메뉴를 선택합니다. 각 단계의 화면 안내를 따르세요.

[실행·검증 범위](PLAY.md) · [QA 포트폴리오](https://leedoeon.vercel.app/qa/index.html)
<!-- WINDOWS-RELEASE-END -->


## 배포 버전과 확인 범위

- Windows x64 / 검증일 2026-11-14
- 마우스로 발사·비행하고 착륙 보상으로 연구를 구매했습니다. 접기·되돌리기·적용 후 종료하고 다시 실행해 연구점과 접힌 형태가 유지되는지 확인했습니다.
- 모든 접기 조합·연구 노드·비행 조건을 검증한 결과는 아닙니다.
- [실제 입력 검증 기록](PLAYTEST.md)

## 소스에서 열기

Unity Hub에서 `FlyEditor` 폴더를 엽니다. `ProjectSettings/ProjectVersion.txt`와 같은 버전의 에디터 및 Windows Build Support가 필요합니다.
현재 Build Settings에 등록된 씬으로 Windows x64를 빌드합니다. `Library`, `Temp`, `Logs`, `Builds`는 재생성 파일이므로 Git에서 제외합니다.

## 문제가 있을 때

ZIP 전체를 다시 압축 해제하고 EXE와 Data 폴더가 같은 위치인지 확인하세요. 실행 오류를 보고할 때 OS·발생 화면·재현 순서를 함께 남겨주세요.
게임의 조작·기능 범위는 실제 빌드와 최신 코드가 기준이며, 아래 기획·과거 개발 문서에는 아직 구현되지 않은 내용이 포함될 수 있습니다.
