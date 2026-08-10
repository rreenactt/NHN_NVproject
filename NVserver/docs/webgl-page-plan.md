# WebGL 진입 페이지(index.html) 개선 계획

[client-deploy-plan.md](client-deploy-plan.md)(빌드·배포)의 자매 문서. 배포된 `play.nhn-backroom.kro.kr` 이 **처음 보여 주는 화면**을 이 게임의 것으로 만드는 설계를 적는다. 아직 구현하지 않았다 — 이 문서가 먼저다.

## 무엇이 문제인가

지금 나오는 `index.html` 은 우리가 쓴 것이 아니다. `ProjectSettings.asset` 의 `webGLTemplate: APPLICATION:Default` 가 가리키는 **유니티 내장 Default 템플릿**이고, `NVproject/Assets/WebGLTemplates/` 는 존재하지 않는다. 그래서 현재 페이지는 이렇다.

| 나오는 것 | 어디서 오는가 |
|---|---|
| 탭 제목 `Unity Web Player \| NVproject` | Default 템플릿의 `<title>` + `{{{ PRODUCT_NAME }}}`. `productName` 이 `NVproject`, `companyName` 이 `DefaultCompany` 로 방치되어 있다 |
| 파비콘이 유니티 아이콘 | `TemplateData/favicon.ico` |
| 로딩바 위 유니티 로고, 하단 바의 유니티 워드마크 | `#unity-logo`, `#unity-logo-title-footer` |
| 회색 배경에 960×600 고정 캔버스 | Default 의 데스크톱 분기가 캔버스를 `WIDTH`×`HEIGHT` px 로 못박는다 |
| 로드 실패가 `alert(message)` | Default 의 `.catch` |

**앞선 커밋에서 실행 파일의 유니티 스플래시는 껐다.** 그런데 웹 빌드의 첫 화면은 스플래시보다 먼저 나오는 이 페이지이므로, 로고를 지운 자리에 유니티 로고가 두 개 더 있는 상태다. 두 작업은 사실상 하나다.

## 원칙

- **페이지는 게임의 일부다.** 이 게임의 UI 규칙은 이미 문서화되어 있다(`Assets/Resources/UI/game-hud.uss` 머리 주석). 그 규칙을 HTML/CSS 로 옮기는 것이지 새 디자인 언어를 만드는 것이 아니다.
- **로딩 화면은 로딩 중에만 존재한다.** WebGL 산출물은 수십 MB 이고 첫 방문자는 그 시간을 빈 화면으로 본다. 그 시간이 이 페이지가 일하는 유일한 시간이다 — 게임이 뜨면 사라져야 하고, 게임 위에 남는 장식이어서는 안 된다.
- **빌드 파이프라인은 건드리지 않는다.** `BuildRunner`, `BuildMenu`, `client-deploy.yml`, `client-deploy.sh` 는 한 줄도 바뀌지 않는다. 템플릿은 `Assets/` 안의 자산이고, 빌드가 알아서 산출물에 복사한다.
- **웹폰트를 싣지 않는다.** 이 페이지의 가치는 "빨리 무언가 보여 주는 것"인데 웹폰트는 첫 페인트 앞에 네트워크 왕복을 하나 더 놓는다. 시스템 폰트로 충분하다. (게임 **안**의 한글은 별개 문제고 이미 `NotoSansKR` 로 해결되어 있다 — `game-hud.uss:28`.)

## 1. 만들 것

```
NVproject/Assets/WebGLTemplates/NVBackrooms/
  index.html          ← 페이지 전체. CSS·JS 인라인
  thumbnail.png       ← Player 설정 화면의 미리보기 (128×128, 유니티 요구)
  TemplateData/
    favicon.svg       ← 파비콘. 별도 이미지 자산 없이 SVG 하나
```

그리고 `ProjectSettings.asset` 의 `webGLTemplate` 을 `PROJECT:NVBackrooms` 로 바꾼다.

**Default 를 복사해서 고치지 않고 Minimal 을 뼈대로 새로 쓴다.** Default 의 `TemplateData/` 는 12개 파일 중 4개가 유니티 로고 이미지이고, 나머지 진행바 이미지도 우리 팔레트가 아니다 — 고칠 것이 남기는 것보다 많다. 유니티가 요구하는 것은 매크로(`{{{ LOADER_FILENAME }}}` 등)와 `createUnityInstance` 호출뿐이다.

## 2. 화면 설계

**팔레트는 레벨의 것을 그대로 쓴다** (`game-hud.uss` 의 정의):

| 이름 | 값 | 쓰임 |
|---|---|---|
| brown-black | `#0E0C08` | 페이지 배경 |
| wall | `#C9B36B` | 테두리, 진행바 채움 |
| trim | `#A8924E` | 보조선 |
| fluorescent | `#FFF6D6` | 강조 |
| bone | `#F2E7C2` | 본문 글자 |
| fog | `#B7AC7E` | 흐린 글자 |

지켜야 하는 두 규칙도 그대로다 — **순백도 순흑도 쓰지 않는다**(그 값들은 소프트웨어처럼 읽히고, 위 값들은 꺼져 가는 형광등 아래 표지판처럼 읽힌다), **패널은 사각이고 테두리가 얇고 반투명하다**(둥근 카드·그림자·그라디언트 없음).

화면 구성:

- **배경** — 브라운블랙 바탕에 아주 약한 비네트. 스캔라인은 CSS `repeating-linear-gradient` 한 줄로 낸다(게임 안처럼 텍스처를 만들 필요가 없다). 게임 HUD 의 `opacity: 0.16` 을 따른다.
- **가운데** — 제목, 한 줄 부제, 진행바, 상태 문구(`자산 내려받는 중… 42%`). 진행바는 사각형에 얇은 `trim` 테두리, 채움은 `wall`.
- **깜빡임** — 형광등 한 번씩 나가는 느낌을 제목에만 준다. `prefers-reduced-motion` 에서는 끈다.
- **하단** — 아주 작은 글씨로 조작 안내 한 줄(마우스·WASD·이 게임은 마우스 잠금을 쓴다는 것). 로딩이 심심하지 않게 하는 것이 아니라, **처음 들어온 사람이 로딩 중에 읽을 수 있는 유일한 곳**이라서 둔다.
- **게임이 뜨면 전부 사라진다.** 로딩 UI 는 `createUnityInstance` 의 `.then` 에서 제거한다.

## 3. 캔버스 — 창 전체를 쓴다

**여기가 이 작업에서 실제로 게임이 달라지는 부분이다.** 로고와 색은 첫인상이지만, 캔버스 크기는 플레이 내내 남는다.

Default 는 데스크톱 분기에서 캔버스를 `canvas.style.width = "{{{ WIDTH }}}px"` 로 **못박는다** — 지금 `defaultScreenWidthWeb: 960 / defaultScreenHeightWeb: 600` 이므로 4K 모니터에서도 960×600 짜리 사각형이 회색 여백 한가운데 떠 있다. 마우스 잠금을 쓰는 1인칭 FPS 에서 이건 창 크기 문제가 아니라 **시야 문제**다. 이 게임은 열쇠와 문을 찾아 어두운 미로를 보는 게임이고, 화면이 작으면 그만큼 덜 보인다.

**캔버스를 뷰포트 전체로 만든다.**

- CSS 로 `position: fixed; inset: 0; width: 100%; height: 100%; display: block`. `body` 는 `margin: 0; overflow: hidden`. Default 의 `#unity-container` 레이아웃과 그 `TemplateData/style.css` 는 쓰지 않는다.
- **높이에 `100vh` 를 쓰지 않는다.** 모바일 브라우저에서 `100vh` 는 주소창을 뺀 높이가 아니라서 화면 아래가 잘린다. `100%` + `html, body { height: 100% }`, 또는 `100dvh`.
- **JS 로 리사이즈를 따라가지 않는다.** 유니티의 `matchWebGLToCanvasSize` 기본값이 켜져 있어, 엔진이 매 프레임 캔버스의 DOM 크기(×`devicePixelRatio`)에 렌더 타깃을 맞춘다. 손으로 `resize` 리스너를 다는 것은 엔진이 이미 하는 일을 두 번 하는 것이고, 두 계산이 어긋나면 화면이 늘어난다. Default 가 이 리스너를 안 다는 것도 같은 이유다.

**`defaultScreenWidthWeb`/`defaultScreenHeightWeb` 은 그래도 올린다 — `1920×1080` 로.** CSS 로 덮으므로 최종 크기와는 무관하지만, 이 값이 캔버스 엘리먼트의 `width`/`height` **속성**(백킹 스토어)이 되어 첫 리사이즈 전까지의 초기 프레임 해상도를 정한다. 960×600 으로 두면 로딩 직후 한 박자가 저해상도로 그려졌다가 커진다. 이 두 줄이 `ProjectSettings` 를 고치는 세 번째 이유다.

**고해상도 디스플레이는 레버로만 남긴다.** `devicePixelRatio` 를 그대로 따르면 4K·레티나에서 실제 픽셀 수가 4배가 되고, WebGL 에서 그 비용은 그대로 프레임레이트다. 기본값은 유니티 기본 동작(=DPR 따름)으로 두되, 느리다는 신호가 나오면 `config.devicePixelRatio = 1` 한 줄이 답이라는 것을 템플릿 주석에 남긴다 — 미리 깎지 않는 이유는 이 게임의 부하가 아직 측정되지 않았기 때문이다.

**전체화면과 포인터 락.**

- 전체화면은 `unityInstance.SetFullscreen(1)` 로 계속 제공한다. 다만 Default 의 하단 바를 통째로 들고 오지는 않는다 — 그 바는 유니티 워드마크와 한 몸이다. 게임 화면 위에 남는 유일한 요소가 되므로 구석의 최소 크기 버튼으로 하고, 마우스가 화면 근처에 없을 때는 흐리게 둔다.
- **로드가 끝나면 캔버스에 포커스를 준다.** 캔버스는 Default 대로 `tabindex="-1"` 을 유지한다. 포커스가 `body` 에 있으면 첫 클릭이 포커스를 옮기는 데 쓰이고 마우스 잠금·키 입력이 한 박자 늦는데, 그 증상은 "가끔 첫 클릭이 씹힌다" 로만 보인다.

## 4. 실패했을 때 무엇을 보여 주는가

여기가 Default 가 가장 나쁜 부분이고, 개선의 절반이다.

- **`alert(message)` 를 없앤다.** 로드 실패가 브라우저 기본 대화상자로 뜨면 게임 화면이 아니라 브라우저 화면이고, 무엇이 잘못됐는지도 알려 주지 못한다. 페이지 안 패널로 바꾼다.
- **Brotli 함정을 이름으로 부른다.** 이 저장소의 문서화된 함정이다(`BuildSelection` 주석, `Caddyfile.example` 의 `@wasmBr` 블록) — 정적 서버가 `Content-Encoding` 을 붙이지 않으면 Brotli 빌드는 **검은 화면**이 된다. 그 상황에서 나오는 실패 문구가 "무언가 잘못됨"이면 아무도 원인에 도달하지 못한다. 실패 패널은 원문 오류를 접어서 보여 준다.
- **WebGL 2 미지원 브라우저**를 로더보다 먼저 감지해 안내한다. 지금은 수십 MB 를 다 받은 뒤에 실패한다.
- **모바일 안내.** 이 게임은 키보드+마우스 전제다(마우스 잠금, WASD). Default 의 모바일 분기는 캔버스를 화면에 채워 주지만 그건 조작할 수 없는 화면을 꽉 채우는 것뿐이다. 접속은 막지 않되 "데스크톱 브라우저에서 하라"를 먼저 띄운다.
- **`unityShowBanner`** 는 Default 의 노랑/빨강 대신 우리 팔레트로 다시 칠한다.

## 5. 건드리면 깨지는 것들

- **초대 링크의 쿼리스트링.** `InviteLink.ReadCodeFromLaunchUrl()` 이 `Application.absoluteURL` 을 읽어 `?code=XXXXXX` 를 꺼낸다(`Assets/Scripts/Net/Session/InviteLink.cs:40`). 템플릿에서 `history.replaceState` 로 주소를 정리하거나 해시로 옮기면 **초대 링크가 조용히 죽는다.** 주소는 손대지 않는다.
- **`{{{ PRODUCT_NAME }}}` 계열 매크로**는 유니티가 빌드 시 치환한다. 매크로를 지우면 그 값이 사라지는 것이 아니라 페이지가 틀린 파일명을 요청한다.
- **`#if DEVELOPMENT_PLAYER` / `#if SHOW_DIAGNOSTICS` 블록은 남긴다.** 지우면 개발 빌드의 프로파일러·진단 아이콘이 사라진다 — 릴리스 페이지를 예쁘게 만들면서 개발 빌드의 계측을 버리는 거래가 된다.
- **템플릿만 만들고 `webGLTemplate` 을 안 바꾸면 아무 일도 일어나지 않는다.** 조용한 실패다. 이 계획에서 가장 잊기 쉬운 한 줄.
- **MCP 브리지가 지금 죽어 있다.** `Unity_RunCommand` 가 어떤 코드로도 `No logs available` 로 실패한다. 그래서 `webGLTemplate` 변경은 파일 직접 편집 + 에디터에서 눈으로 확인이 된다 (스플래시 작업과 같은 절차).
- **CI Library 캐시 키가 `ProjectSettings.asset` 해시다**(`client-deploy.yml:51`). 설정을 바꾸면 캐시가 한 번 미스나고 그 빌드만 느려진다. 정상이다.
- **배포 검증은 영향받지 않는다.** 워크플로와 `client-deploy.sh` 는 `index.html` 과 `Build/` 의 **존재**만 본다 — 내용은 보지 않는다.
- **Caddy 설정도 그대로다.** 새로 생기는 정적 파일은 `TemplateData/` 아래이고, 캐시 규칙은 `/index.html`(no-cache)과 `/Build/*`(5분)에만 걸려 있다. `TemplateData/` 는 기본 동작을 받는다 — 파일명에 해시가 없으므로 **오래 캐시하면 안 되는 부류**라는 점은 기억해 둔다. 문제가 되면 규칙 한 줄을 더한다.

## 6. 게임 이름 — **Backrooms Escape**

페이지 제목, 파비콘, 탭 이름이 전부 여기서 나온다. 이름은 **Backrooms Escape** 로 확정했다 — `game-hud.uss` 가 주석에서 이미 스스로를 그렇게 부르고 있었으므로, 새로 짓는 것이 아니라 코드 주석에만 있던 이름을 대외 이름으로 올리는 것이다.

`ProjectSettings` 도 같이 고친다 — `productName` 은 `NVproject` → **`Backrooms Escape`**, `companyName` 은 `DefaultCompany` → **`NHN AI HACKATHON`**. 페이지에 나오는 것은 `productName` 뿐이지만(`<title>` 과 로딩 화면의 제목이 같은 매크로에서 나온다), 두 값은 저장 경로를 함께 이루므로 한 번에 바꾸는 편이 싸다.

바꾸는 값이 페이지 밖에서도 쓰이는 것을 알고 바꾼다:

- **Windows 실행 파일 이름은 따라오지 않는다.** 보통 `productName` 이 실행 파일 이름이 되지만 이 저장소는 `BuildSelection.cs:93` 이 `NVClient.exe` 로 고정하고 있어, 이름을 바꿔도 산출물 경로와 `PlayerLaunchService` 가 띄우는 대상은 그대로다 — **Launch Clients (no build)** 도 계속 동작한다.
- `companyName`/`productName` 은 **`PlayerPrefs` 의 저장 경로**(레지스트리 `HKCU\Software\<company>\<product>`)다. 바꾸면 기존 빌드가 저장해 둔 로컬 설정이 새 경로로 갈라진다. 이 저장소가 거기에 두는 것은 환경별 로비 호스트(`nv.{id}.lobby.host`) 정도이고 다시 입력하면 그만이라, **지금 바꾸는 편이 나중보다 싸다.**
- WebGL 은 `PlayerPrefs` 를 IndexedDB 에 넣고 그 키도 같은 이름에서 나오므로, 배포된 사이트의 기존 방문자도 로컬 설정만 한 번 초기화된다. 계정이 없는 게임이라 잃을 것이 그 이상은 없다.

## 7. 작업 순서

1. `Assets/WebGLTemplates/NVBackrooms/` 를 만든다 — `index.html`, `thumbnail.png`, `TemplateData/favicon.svg`.
2. `ProjectSettings.asset` 을 다섯 줄 고친다 — `webGLTemplate` 을 `PROJECT:NVBackrooms` 로, `productName`/`companyName` 을 6절대로, `defaultScreenWidthWeb`/`defaultScreenHeightWeb` 을 3절대로.
3. 에디터에서 Project Settings ▸ Player ▸ Resolution and Presentation 에 템플릿이 뜨고 선택되어 있는지 확인한다. (MCP 가 죽어 있으므로 눈으로)
4. **Tools ▸ NV ▸ Build (current selection)** 을 WebGL·압축 Disabled 로 뽑고 `python -m http.server` 로 연다. 압축을 끄는 이유는 `BuildRunner.LogWebGlNextSteps` 가 이미 경고하는 그것이다 — 평범한 정적 서버는 `Content-Encoding` 을 붙이지 않는다.
5. **캔버스를 재 본다** — 창을 최대화·리사이즈하며 검은 여백이 남지 않는지, `Screen.width`/`Screen.height` 가 창을 따라가는지, 전체화면 진입·복귀 후에도 그런지. 3절의 실패 모드가 전부 여기서 보인다.
6. 실패 경로를 일부러 만들어 확인한다: `Build/` 안의 파일 하나를 지우고 실패 패널이 뜨는지, 모바일 에뮬레이션에서 안내가 뜨는지.
7. `?code=ABCDEF` 를 붙여 열고 초대 코드가 게임에 도달하는지 확인한다 (5절의 첫 항목).
8. Production(WebGL, Brotli)은 `main` 푸시로 CI 가 뽑는다. 배포 후 실제 도메인에서 한 번 더 본다.

**4~8 은 브라우저에서 사람이 보는 것으로만 확인된다.** 이 저장소에는 이것을 잡아 줄 테스트가 없고, 에디터 MCP 로도 렌더링을 볼 수 없다.
