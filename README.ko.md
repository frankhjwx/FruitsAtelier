# FruitsAtelier

![FruitsAtelier](assets/branding/wordmark-en.svg)

[English](README.md) | [简体中文](README.zh-CN.md) | [日本語](README.ja.md) | **한국어**

Windows와 macOS에서 사용할 수 있는 독립 osu!catch 비트맵 편집기입니다. 곡 선택부터 패턴 제작, 테스트 플레이까지 하나의 작업 공간에서 진행하세요.

[공식 웹사이트](https://fruitsatelier.himiko.moe/) · [다운로드](https://github.com/frankhjwx/FruitsAtelier/releases/latest) · [Discord](https://discord.gg/Dwe7bshYHY) · [Himiko의 osu! 프로필](https://osu.ppy.sh/users/1806962)

**현재 버전: 0.9.9**

![FruitsAtelier의 편집 캔버스, 오브젝트 타임라인, Catch 미리 보기](website/public/assets/eureka.png)

*Σvreka — Halv vs. kuro · Ascendance [Chronosync]. 화면에 표시된 비트맵과 스킨 아트워크는 각 제작자의 소유입니다.*

## 다음 Catch 비트맵을 만들어 보세요

- **라이브러리에서 시작하세요.** osu!stable의 Songs를 탐색하고 검색하거나 폴더와 `.osz`를 가져올 수 있습니다. 저장한 프로젝트를 이어서 편집하고, 탭으로 여러 난이도를 전환하며 별점을 확인할 수 있습니다.
- **패턴을 직접 편집하세요.** 과일과 바나나 샤워를 배치하고, 선택한 오브젝트를 이동·복제·좌우 반전·삭제할 수 있습니다. 실행 취소와 다시 실행도 지원합니다. 제어점이나 베지어 핸들로 FSlider를 만들고, 반복을 추가하거나 가져온 슬라이더를 변환하고 경로를 따라 과일 스트림을 생성할 수 있습니다.
- **배치와 타이밍을 조정하세요.** 비트 분할, 가로 격자, 거리 스냅을 사용할 수 있습니다. 빨간선과 초록선을 편집하고, 탭으로 템포를 설정하며, 오브젝트를 다시 스냅하고 메트로놈으로 박자를 확인하세요. 새 콤보와 히트사운드, 각 슬라이더 끝점의 사운드도 설정할 수 있습니다.
- **음악을 듣고, 확인하고, 플레이하세요.** MP3·OGG·WAV와 히트사운드를 10%·25%·50%·75%·100%·150% 속도로 재생합니다. NM·Easy·Hard Rock 미리 보기와 현재 위치부터의 테스트 플레이를 지원합니다. 이동 키 설정, 대시, 콤보 표시, 자동 플레이도 사용할 수 있습니다.
- **익숙한 스킨과 언어를 사용하세요.** osu!stable 스킨을 불러오거나 `.osk`를 가져올 수 있습니다. UI는 영어, 중국어 간체, 중국어 번체, 일본어, 한국어, 러시아어, 스페인어, 프랑스어, 폴란드어, 네덜란드어, 필리핀어, 인도네시아어, 태국어를 지원합니다.
- **프로젝트를 저장하고 내보내세요.** 편집 가능한 슬라이더와 난이도 데이터를 저장하고, `.osu` 파일을 내보내거나 osu!stable에 새 난이도를 추가할 수 있습니다.

Catch `.osu`는 v12–v14 및 stable과 호환되는 lazer v128을 읽을 수 있으며, v14로 내보냅니다. 0.9에서는 동영상과 스토리보드 재생을 지원하지 않습니다.

## 시작하기

### Windows

1. [Releases](https://github.com/frankhjwx/FruitsAtelier/releases/latest)에서 Windows x64 ZIP을 다운로드하세요.
2. ZIP 전체를 압축 해제한 뒤 `FruitsAtelier.exe`를 실행하세요. 압축 해제한 파일은 함께 보관해야 합니다. .NET이 포함되어 있으며, Windows 10/11과 DirectX 11이 필요합니다.
3. 첫 실행 안내에 따라 폴더, 언어, 스킨, 오디오, 테스트 플레이를 설정하세요. 이 옵션들은 **라이브러리 > 설정**에서도 변경할 수 있습니다.
4. 비트맵을 가져오거나 새 프로젝트를 만든 뒤, 난이도를 열어 편집을 시작하세요.

### macOS

소스 빌드에는 .NET SDK **8.0.419**와 Xcode Command Line Tools가 필요합니다. 저장소의 루트에서 실행하세요.

```bash
bash scripts/Install-Mac-SDK.sh
./Run-Editor-Mac.command
```

`bash scripts/Publish-Mac.sh`로 독립 실행 앱을 만들 수 있습니다. 자세한 내용은 [macOS 가이드(영어)](docs/MACOS.md)를 참고하세요.

## 사용 안내와 커뮤니티

[사용자 설명서(영어)](docs/USER_MANUAL.md)는 설정, 편집, 저장, 테스트 플레이를 설명합니다. [키보드 및 마우스 조작 안내(영어)](docs/KEY_BINDINGS.md)에서 전체 단축키를 확인할 수 있습니다.

내보내기로 난이도와 파일을 연결한 뒤에는 **Ctrl+S로 연결된 `.osu`도 업데이트됩니다**. **Ctrl+Alt+E**로 내보내기 옵션을 열 수 있습니다.

[Discord](https://discord.gg/Dwe7bshYHY)에 참여해 알파 버전을 테스트하고 의견을 나눠 주세요. 버그는 [GitHub Issues](https://github.com/frankhjwx/FruitsAtelier/issues)에 보고하거나 [Himiko의 osu! 프로필](https://osu.ppy.sh/users/1806962)을 통해 연락할 수 있습니다.

## 개발

C# 12와 .NET 8을 사용합니다. Windows 소스 빌드는 `global.json`에 지정된 SDK **10.0.400**을 사용하며, [Run-Editor.cmd](Run-Editor.cmd)로 빌드하고 실행할 수 있습니다. macOS 스크립트는 `macOS/`에 지정된 SDK를 사용합니다.

- [빌드와 테스트](docs/TESTING.md) · [패키징과 릴리스](docs/RELEASING.md)
- [편집 조작](docs/EDITOR_UI.md) · [작업 공간과 파일](docs/WORKSPACE.md)
- [아키텍처](docs/ARCHITECTURE.md) · [프로젝트 모델](docs/PROJECT_MODEL.md) · [파일 형식](docs/STABLE_FORMAT.md)
- [현지화](docs/LOCALIZATION.md) · [서드 파티 라이선스](THIRD_PARTY_NOTICES.md)

기술 문서와 사용자 설명서는 영어로 관리합니다.
