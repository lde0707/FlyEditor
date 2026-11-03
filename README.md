# FlyEditor

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
