# FruitsAtelier

![FruitsAtelier](assets/branding/wordmark-en.svg)

[English](README.md) | [简体中文](README.zh-CN.md) | [日本語](README.ja.md) | **한국어**

Windows와 macOS에서 사용할 수 있는 독립 osu!catch 비트맵 편집기입니다. 곡 선택부터 패턴 제작, 테스트 플레이까지 하나의 작업 공간에서 진행하세요.

[공식 웹사이트](https://fruitsatelier.himiko.moe/) · [다운로드](https://github.com/frankhjwx/FruitsAtelier/releases/latest) · [Discord](https://discord.gg/Dwe7bshYHY)

**현재 버전: 0.9.9**

![FruitsAtelier의 편집 캔버스, 오브젝트 타임라인, Catch 미리 보기](website/public/assets/eureka.png)

*Σvreka — Halv vs. kuro · Ascendance [Chronosync].*

## 다음 Catch 비트맵을 만들어 보세요

- **라이브러리에서 시작하세요.** osu!stable의 Songs를 탐색하고 검색하거나 폴더와 `.osz`를 가져올 수 있습니다. 저장한 프로젝트를 이어서 편집하고, 탭으로 여러 난이도를 전환하며 별점을 확인할 수 있습니다.
- **패턴을 직접 편집하세요.** 가로축은 Catch 위치, 세로축은 시간을 나타내는 캔버스에 과일과 바나나 샤워를 배치하세요. 화면 이동과 확대·축소, 선택한 오브젝트의 이동·복제·좌우 반전·삭제, 실행 취소와 다시 실행을 지원합니다.
- **타이밍과 히트사운드를 조정하세요.** 빨간선과 초록선 편집, 탭으로 템포 설정, 오브젝트 재스냅, 메트로놈을 지원합니다. 새 콤보와 각 슬라이더 끝점의 사운드도 설정할 수 있습니다. Hitsound Copier(Beta)로 적용 전에 결과를 확인하고 다른 난이도에서 사운드를 복사할 수 있습니다.
- **음악을 듣고, 확인하고, 플레이하세요.** MP3·OGG·WAV와 히트사운드를 10%·25%·50%·75%·100%·150% 속도로 재생합니다. NM·Easy·Hard Rock 미리 보기와 현재 위치부터의 테스트 플레이를 지원합니다. 이동 키 설정, 대시, 콤보 표시, 자동 플레이도 사용할 수 있습니다.
- **익숙한 스킨과 언어를 사용하세요.** osu!stable 스킨을 불러오거나 `.osk`를 가져올 수 있습니다. UI는 영어, 중국어 간체, 중국어 번체, 일본어, 한국어, 러시아어, 스페인어, 프랑스어, 폴란드어, 네덜란드어, 필리핀어, 인도네시아어, 태국어를 지원합니다.
- **프로젝트를 저장하고 내보내세요.** 편집 가능한 데이터를 저장하고 `.osu` 난이도나 `.osz` 비트맵 세트를 내보낼 수 있습니다. 연결된 난이도를 osu!stable과 동기화하고 외부 변경 충돌을 검토하세요. 버전 기록에서 저장된 난이도 스냅샷을 복원할 수도 있습니다.

## Catch를 위한 도구

- **FSlider.** 제어점이나 베지어 핸들로 시간에 따른 가로 이동을 직접 그리세요. 반복을 추가하거나 가져온 Legacy Slider를 변환할 수 있습니다. 일반 osu! 슬라이더로 내보내며, 편집 가능한 곡선은 프로젝트에 보관합니다.
- **드롭렛 직접 편집.** 슬라이더 안의 과일이나 드롭렛을 개별 선택해 가로로 드래그하거나 X 좌표를 입력할 수 있습니다. 격자와 거리 스냅으로 간격을 조정하세요.
- **무작위화와 무작위화 보정.** NM 또는 HR에 맞춰 TinyDroplet의 무작위 위치 변화를 보정하거나, 무작위화 강도와 시드를 설정할 수 있습니다. HR 보정을 사용할 때는 두 미리 보기 모드에서 결과를 확인할 수 있습니다.
- **DPB와 거리 스냅.** Distance Per Beat(비트당 거리)를 가로 간격의 기준으로 설정하고, 최대 8개의 거리 프리셋을 동시에 사용할 수 있습니다. 비트 스냅은 시간을, 격자 스냅은 가로 위치를 맞춥니다.
- **이동 표시와 분석.** 앞뒤 오브젝트 간격과 Stand·Walk·Dash·Hyperdash 연결을 캔버스에서 확인하세요. 곡 전체의 Movement strain 그래프로 이동 부담이 큰 구간을 찾아 바로 이동할 수 있습니다. AiMod는 오브젝트 시작 시간의 겹침을 검사합니다.
- **편집 가능한 스트림과 스택.** 곡선을 따라 지정한 비트 분할로 과일 스트림을 만들거나, 폭 변화를 설정해 좌우로 번갈아 배치되는 스택을 만들 수 있습니다. 부모 곡선과 개별 과일을 편집하거나 독립된 과일로 분리할 수도 있습니다. 둘 다 hit circle로 내보냅니다.

Catch `.osu`는 v12–v14 및 stable과 호환되는 lazer v128을 읽을 수 있으며, v14로 내보냅니다. 0.9에서는 동영상과 스토리보드 재생을 지원하지 않습니다.

## 시작하기

### Windows

1. [Releases](https://github.com/frankhjwx/FruitsAtelier/releases/latest)에서 Windows x64 ZIP을 다운로드하세요.
2. ZIP 전체를 압축 해제한 뒤 `FruitsAtelier.exe`를 실행하세요. 압축 해제한 파일은 함께 보관해야 합니다. .NET이 포함되어 있으며, Windows 10/11과 DirectX 11이 필요합니다.
3. 첫 실행 안내에 따라 폴더, 언어, 스킨, 오디오, 테스트 플레이를 설정하세요. 이 옵션들은 **라이브러리 > 설정**에서도 변경할 수 있습니다.
4. 비트맵을 가져오거나 새 프로젝트를 만든 뒤, 난이도를 열어 편집을 시작하세요.

업데이트 기능을 지원하는 설치본에서는 **설정 > 앱 업데이트 > 업데이트 확인**으로 다운로드한 뒤 저장하고 재시작할 수 있습니다. 프로젝트와 사용자 스킨은 앱의 `current/` 폴더 밖에 보관하세요.

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

작업 공간의 `.catchdiff` 파일에 편집 데이터를 보관하며, 이전 형식인 `.catchproj`도 열 수 있습니다. 전체 프로젝트 폴더와 참조 리소스를 함께 보관하세요. 버전 기록에서 스냅샷을 비교하고 난이도를 복원할 수 있으며, 보관 정책은 일반적으로 30일과 프로젝트당 100라운드로 제한됩니다. 자세한 내용은 [동기화와 복구(영어)](docs/SYNCHRONIZATION.md)를 참고하세요.

[Discord](https://discord.gg/Dwe7bshYHY)에 참여해 알파 버전을 테스트하고 의견을 나눠 주세요. 버그는 [GitHub Issues](https://github.com/frankhjwx/FruitsAtelier/issues)에 보고하거나 [osu!에서 저에게 연락해 주세요](https://osu.ppy.sh/users/1806962).

## 개발

C# 12와 .NET 8을 사용합니다. Windows 소스 빌드는 `global.json`에 지정된 SDK **10.0.400**을 사용하며, [Run-Editor.cmd](Run-Editor.cmd)로 빌드하고 실행할 수 있습니다. macOS 스크립트는 `macOS/`에 지정된 SDK를 사용합니다.

- [빌드와 테스트](docs/TESTING.md) · [패키징과 릴리스](docs/RELEASING.md)
- [편집 조작](docs/EDITOR_UI.md) · [작업 공간과 파일](docs/WORKSPACE.md)
- [아키텍처](docs/ARCHITECTURE.md) · [프로젝트 모델](docs/PROJECT_MODEL.md) · [파일 형식](docs/STABLE_FORMAT.md)
- [현지화](docs/LOCALIZATION.md) · [서드 파티 라이선스](THIRD_PARTY_NOTICES.md)

기술 문서와 사용자 설명서는 영어로 관리합니다.

## 크레딧과 라이선스

- [ppy/osu](https://github.com/ppy/osu) — osu!catch 알고리즘 및 변환, 게임플레이, 난이도 계산, 호환성 동작의 참고 자료.
- [Exsper/osucatch-editor-realtimeviewer](https://github.com/Exsper/osucatch-editor-realtimeviewer) — 매핑 중 실시간 Catch 게임플레이 미리 보기의 영감.
- [Phob144/DropletDerandomizer](https://github.com/Phob144/DropletDerandomizer) — 드롭렛 무작위화 보정과 Catch 슬라이더 패턴 제작의 영감.

FruitsAtelier의 자체 소스 코드는 [MIT 라이선스](LICENSE)로 공개합니다. 서드 파티 코드와 리소스는 각자의 라이선스를 유지합니다. 출처 표기와 보관된 라이선스 문서는 [서드 파티 고지](THIRD_PARTY_NOTICES.md)를 참고하세요. 포함된 CC BY-NC 4.0 osu! 리소스의 비상업적 사용 제한은 유지되며, SoundTouch.Net은 LGPL-2.1-or-later를 따릅니다.
