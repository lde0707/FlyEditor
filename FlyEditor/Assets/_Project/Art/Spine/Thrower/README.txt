Spine 4.2 편집

파일을 한 폴더에 압축 해제한 후 Spine 4.2의 Import Data에서 Thrower.json을 엽니다.
images 하위 폴더는 부위별 원본이며, throw-right 애니메이션의 release 이벤트가 비행 시작점입니다.
원하는 이름으로 Spine 프로젝트를 저장한 뒤 뼈의 회전 키를 조정할 수 있습니다.
위팔 길이는 30, 아래팔 길이는 28입니다. 앞발은 바닥에 고정하고, 체중이 앞으로 실릴 때 뒷발은 발끝을 축으로 뒤꿈치만 살짝 들어 올립니다. 반대팔의 두 슬롯은 몸통과 두 다리보다 먼저 그립니다.

FlyEditor 프로젝트

Unity에 포함한 캐릭터는 이 Spine 리그를 Unity 클립과 프리팹으로 구워 재생합니다.
스파인에서 수정한 뒤 같은 폴더로 4.2 JSON을 다시 내보내고 Unity 메뉴 Tools > FlyEditor > Rebuild Thrower from Spine JSON을 실행하세요.
변환 메뉴는 이 캐릭터의 기본 스킨, 단일 region 슬롯, 선형 이동·회전 키, grip 뼈, throw-right / release 이벤트용입니다.
팔꿈치 관절은 그림끼리 겹치도록 배치했습니다. 게임에서는 paper 슬롯 대신 실제 3D 기체가 손에 붙었다가 날아갑니다.
