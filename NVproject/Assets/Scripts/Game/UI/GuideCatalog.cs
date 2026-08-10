using System.Collections.Generic;

namespace NV.Game.UI
{
    /// <summary>
    /// 게임 안내서의 내용 전부. UI 와 분리된 순수 데이터라, 새 맵·역할·장치·조작이 생기면
    /// 여기에 주제나 섹션을 더하는 것으로 끝난다 — 그리는 쪽(<see cref="GuideOverlayController"/>)
    /// 은 목록을 순서대로 그릴 뿐 내용을 모른다.
    ///
    /// 숫자는 규칙서(.claude/skills/game-rules/references/ruleset.md)의 것을 그대로 적는다.
    /// 규칙이 바뀌면 규칙서 → GameConfig → 여기 순으로 고친다.
    /// </summary>
    public static class GuideCatalog
    {
        /// <summary>주제 하나 — 안내서의 탭 하나.</summary>
        public sealed class Topic
        {
            public readonly string Id;
            public readonly string Title;
            public readonly Section[] Sections;

            public Topic(string id, string title, params Section[] sections)
            {
                Id = id;
                Title = title;
                Sections = sections;
            }
        }

        /// <summary>소제목 하나와 그 아래 줄들. 줄은 그대로 한 항목씩 그려진다.</summary>
        public sealed class Section
        {
            public readonly string Heading;
            public readonly string[] Lines;

            public Section(string heading, params string[] lines)
            {
                Heading = heading;
                Lines = lines;
            }
        }

        /// <summary>매치에서 열었을 때 먼저 펼칠 탭. 역할이 없으면 소개 탭.</summary>
        public static string TopicFor(Role role)
        {
            if (role == Role.Runner) return "runner";
            if (role == Role.Seeker) return "seeker";
            return "overview";
        }

        public static IReadOnlyList<Topic> Topics => _topics;

        private static readonly Topic[] _topics =
        {
            new Topic("overview", "게임 소개",
                new Section("어떤 게임인가",
                    "두 층짜리 미로에서 벌어지는 비대칭 숨바꼭질 탈출 게임이다.",
                    "한 명의 무장한 시커가 나머지 비무장 러너들을 사냥한다.",
                    "러너는 열쇠를 모아 문을 열고 탈출해야 한다."),
                new Section("승리 조건",
                    "러너 승리 — 러너 2명이 탈출하면 즉시 승리 (러너가 1명뿐이면 1명).",
                    "시커 승리 — 제한 시간이 끝나거나, 러너 전원이 쓰러지면 승리.",
                    "탈출한 러너는 쓰러진 것이 아니다 — 전멸에 포함되지 않는다."),
                new Section("기억할 것",
                    "발소리가 핵심 정보다. 시커는 소리로 찾고, 러너는 소리로 들킨다.",
                    "총성은 발소리보다 세 배 멀리 들린다 — 한 발이 모두에게 위치를 알린다.")),

            new Topic("runner", "러너",
                new Section("목표",
                    "맵에 흩어진 열쇠 10개 중 필요한 만큼 모아 문에 꽂는다.",
                    "필요한 열쇠 수는 시작 인원으로 정해진다 — 2인 3개, 3인 5개, 4인 8개, 5인 10개.",
                    "문은 러너에게만 보인다. 시커는 문의 위치를 절대 알 수 없다.",
                    "마지막 열쇠가 꽂히면 문이 열린다 — 열린 문 앞에 잠시 머무르면 탈출.",
                    "탈출 진행도는 시커를 포함한 모두에게 보인다. 마지막 순간은 방해받을 수 있다."),
                new Section("피격당하면",
                    "한 발 맞으면 출혈 상태가 되고, 무작위 위치로 순간이동된다.",
                    "출혈 중에는 피 흔적이 남는다 — 시커와 나 자신에게만 보인다.",
                    "출혈은 지혈 장치(1회용)로만 멎는다.",
                    "출혈 중 두 번째로 맞으면 쓰러진다."),
                new Section("살아남는 법",
                    "Ctrl 을 누른 채 걸으면 발소리가 나지 않는다. 대신 느리다.",
                    "총성이 들리면 시커의 위치와 남은 탄이 줄었다는 사실을 동시에 안 것이다.",
                    "장치를 활용하라 — 지도, 시커 시점, 순간이동이 목숨을 구한다.")),

            new Topic("seeker", "시커",
                new Section("목표",
                    "제한 시간 동안 러너들의 탈출을 막는다. 러너 2명이 나가기 전에 시간을 끝내거나 전원을 쓰러뜨린다.",
                    "시커의 몸은 괴물이다 — 안개 너머 실루엣만으로도 서로를 구분할 수 있게 하기 위해서다."),
                new Section("무기와 사슬",
                    "권총의 탄창은 3발이다. 러너는 두 발에 쓰러진다.",
                    "3발을 다 쓰면 사슬이 나타나 시커를 끌고 간다 — 3초를 기다린 뒤 재장전된다.",
                    "탄은 비용이다. 빗나간 한 발은 위치를 알리고 사슬에 한 발 가까워진다."),
                new Section("달리기",
                    "달리기 게이지는 4초 분량 — 다 쓰면 회복될 때까지 달릴 수 없다.",
                    "빈 게이지가 가득 차는 데 10초 걸리고, 차는 중에도 남은 만큼은 쓸 수 있다.",
                    "달리기 속도는 러너 달리기의 1.6배 — 거리를 좁히는 유일한 수단이다."),
                new Section("추적",
                    "출혈 중인 러너의 피 흔적이 보인다. 흔적을 따라가라.",
                    "발소리를 들어라 — 조용히 걷는 러너는 느리다.",
                    "러너를 돕는 장치는 4발을 쏘면 파괴할 수 있다. 파괴된 장치는 되살아나지 않는다.")),

            new Topic("objects", "오브젝트 · 장치",
                new Section("오브젝트",
                    "열쇠 — 맵 곳곳에 10개. E 키로 주워서 소지하고, 문에 하나씩 꽂는다.",
                    "탈출문 — 매 판 무작위 위치에 생기며 러너에게만 보인다.",
                    "사슬 제단 — 시커가 탄을 다 썼을 때 끌려가는 곳."),
                new Section("장치 (맵에 8~9개, E 키로 사용)",
                    "시간 추가 (1회) — 남은 시간을 늘린다. 시간전에 맞서는 러너의 카드.",
                    "전체 지도 (반복) — 모두의 위치를 잠깐 보여 준다.",
                    "지혈 (1회) — 출혈과 피 흔적을 멎게 한다.",
                    "정지 + 투시 (1회) — 잠시 전원이 멈추고 벽이 비쳐 보인다.",
                    "시커 시점 (반복) — 시커가 있는 곳의 화면을 보여 준다.",
                    "순간이동 (반복) — 다른 곳으로 이동. 누군가 쓰면 12초간 모두 잠긴다."),
                new Section("주의",
                    "장치는 시커가 4발로 파괴할 수 있다 — 파괴되면 그 효과는 그 판에서 사라진다.")),

            new Topic("controls", "조작",
                new Section("이동",
                    "W A S D — 이동",
                    "마우스 — 시점",
                    "Shift — 달리기",
                    "Ctrl — 숨죽여 걷기 (발소리 없음, 대신 느림)",
                    "Space — 점프"),
                new Section("행동",
                    "E — 상호작용 (열쇠 줍기 · 문에 꽂기 · 장치 사용)",
                    "좌클릭 — 사격 (시커)",
                    "1 / 2 — 무기 넣기 / 꺼내기 (시커)"),
                new Section("화면",
                    "ESC — 시스템 메뉴",
                    "H — 이 안내서 열기 / 닫기")),

            new Topic("flow", "게임 진행",
                new Section("시작까지",
                    "대기방에서 방장이 아닌 모두가 READY 를 누른다.",
                    "방장의 START 가 곧 방장의 준비다 — 방장은 READY 를 따로 누르지 않는다.",
                    "시작되면 역할이 공개된다. 누가 시커인지는 그때 안다."),
                new Section("플레이 중",
                    "타이머가 줄어든다. 시간이 다 되면 시커의 승리다.",
                    "러너 — 초반에는 열쇠와 장치 위치를 파악하고, 발소리를 아껴 가며 모은다.",
                    "러너 — 출혈이 시작되면 흔적이 남는다. 지혈 장치를 찾아라.",
                    "러너 — 문이 열리면 탈출 홀드가 시작된다. 시커가 올 수 있으니 주변을 확인하라.",
                    "시커 — 발소리와 피 흔적을 따라가고, 탄과 달리기 게이지를 아껴 쓴다."),
                new Section("끝난 뒤",
                    "결과 화면 뒤 방은 대기방으로 돌아간다. 역할은 다음 판에 다시 정해진다.")),
        };
    }
}
