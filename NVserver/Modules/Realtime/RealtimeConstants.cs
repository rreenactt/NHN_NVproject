using NV.Shared.Contracts.Enums;
using NV.Shared.Simulation;

namespace NV.Realtime
{
    /// 모듈의 판정·용량 파라미터. 이 값들의 유일한 출처다.
    ///
    /// `SimConstants` 와 역할이 다르다. 그쪽은 클라이언트도 같은 값으로 계산해야 하는
    /// 시뮬레이션 파라미터이고, 여기는 서버가 혼자 정하는 판정과 용량이다.
    /// 클라이언트가 알아야 하는 값이 생기면 여기가 아니라 `Shared` 로 간다.
    ///
    /// 상수를 쓰는 파일에 두지 않고 모아 둔다. 룸 정원·입력 상한·버퍼 크기는
    /// 함께 조정해야 맞는 값들이라, 흩어져 있으면 하나만 바꾸고 나머지를 놓친다.
    /// 이유가 있는 값은 이유를 값 옆에 적는다 — 숫자만 남으면 다음 사람이 못 바꾼다.
    ///
    /// 설정 파일로 빼지 않는다. 룸 정원은 스냅샷 버퍼 크기를 정하고, 입력 상한은
    /// 클라이언트의 재전송 폭과 맞물린다. 재기동 없이 바뀌면 안 되는 값들이다.
    /// 런타임에 바뀌어야 하는 것은 `RealtimeOptions` 로 간다.
    internal static class RealtimeConstants
    {
        /// 룸 하나의 용량과 진행 규칙.
        internal static class Rooms
        {
            /// 룸당 인원. 스냅샷 버퍼와 슬롯 배열 크기가 이 값에서 나온다.
            public const int MaxPlayers = 8;

            /// 룸 id 최대 길이. 형식은 소문자·숫자·하이픈만 허용한다.
            /// 초대 코드는 이보다 짧지만, 설정으로 열어 두는 정적 룸의 id 도 같은 규칙을 쓴다.
            public const int MaxRoomIdLength = 32;

            /// 코드 공간을 룸 수의 몇 배로 유지할지.
            ///
            /// 이 값이 클수록 코드가 겹칠 확률이 낮아지고 길이는 빨리 늘어난다. 10만이면
            /// 코드 하나를 만들 때 겹칠 확률이 10만분의 1 이하이고, 6자로 약 8,800 룸까지
            /// 버틴다 — 현실적인 배포에서는 코드가 6자에 머무른다.
            ///
            /// 동시 룸 수에는 상한을 두지 않는다. 룸은 `POST /rooms` 로만 생기고 비면
            /// 회수되므로, 상한 대신 생성 요청 자체를 제한한다(Api 의 요청 제한).
            public const int CodeSpaceMargin = 100_000;

            /// 코드가 겹쳤을 때 같은 길이로 다시 만들어 보는 횟수.
            ///
            /// 무한 재시도를 두지 않는다 — 알파벳이나 길이를 줄이는 변경이 들어오면
            /// 이 자리가 조용히 무한 루프가 된다. 이 횟수를 다 쓰면 길이를 한 자
            /// 늘려 다시 시도하므로, 정상적인 경우 실패로 끝나지 않는다.
            public const int CodeGenerationAttempts = 8;

            /// **아직 아무도 들어오지 않은** 룸을 회수할 때까지의 틱. 30Hz 기준 30초.
            ///
            /// 한 번이라도 사람이 있었던 룸은 비는 즉시 회수하고, 이 값은 만들어졌지만
            /// 아직 아무도 붙지 않은 룸에만 쓴다. 둘을 구분해야 하는 이유는 방을 만드는
            /// 절차에 있다 — `POST /rooms` 로 룸이 생긴 뒤 방장이 WebSocket 으로 붙기
            /// 전까지 참가자가 0이다. 그 구간에서 즉시 회수하면 모든 방이 만든 사람이
            /// 들어오기 전에 사라지고, 증상은 "방을 만들었는데 없는 코드라고 한다" 다.
            ///
            /// 그 구간은 왕복 한 번이며 접속 실패 시 재시도(0.5·1·2·4초)까지 감안해도
            /// 몇 초다. 30초는 그보다 넉넉하고, 코드를 받아 두고 붙지 않은 방을 오래
            /// 남기지 않을 만큼 짧다.
            public const uint UnjoinedExpiryTicks = 30 * 30;

            /// 한 틱에 적용할 입력의 상한. 지터로 밀려 쌓인 입력을 따라잡되,
            /// 무제한으로 적용하면 한 틱에 순간이동한다.
            public const int MaxInputsPerTick = 2;

            /// 매치를 시작할 수 있는 최소 인원. 룰셋의 하한이다 — Seeker 하나와
            /// Runner 하나가 있어야 술래잡기가 성립한다. 혼자 시작하면 Runner 가 0명이라
            /// 승리 조건이 즉시 충족되거나 아예 평가되지 않는다.
            public const int MinPlayersToStart = 2;

            /// 룸 상태 전문을 보내는 간격(틱). 30Hz 기준 2Hz 다.
            ///
            /// 상태가 바뀐 틱에는 이 간격과 무관하게 즉시 보낸다. 간격만으로 보내면
            /// 시작 버튼과 화면 전환 사이에 최대 이 간격만큼 공백이 생긴다.
            public const int RoomStateIntervalTicks = 15;

            /// 매치 상태 전문의 간격(틱). 룸 상태와 같은 2Hz 다.
            ///
            /// 값을 다시 적지 않고 유도한다. 두 전문은 같은 이유로 같은 주기를 쓰므로
            /// (전문이지 알림이 아니고, 그 사이는 클라이언트가 자기 시계로 메운다)
            /// 하나를 조정하면 다른 하나도 따라가야 한다.
            public const int MatchStateIntervalTicks = RoomStateIntervalTicks;

            /// 새 입력이 없을 때 마지막 입력을 반복하는 상한.
            /// 짧은 손실은 흡수하고, 그 이상 끊기면 이동을 멈춘다.
            /// 무제한 허용하면 입력을 끊은 클라이언트가 계속 달린다.
            public const int MaxInputRepeatTicks = 3;
        }

        /// 매치의 **전송** 파라미터. 판정도 배치도 여기 없다.
        ///
        /// 기준은 하나다 — **클라이언트가 이 값으로 무언가를 계산하는가.** 배치 간격과 장치
        /// 조합표는 클라이언트가 오프라인 연습에서 같은 배치를 계산하는 데 쓰므로
        /// `MatchConstants`(`Shared`)로 갔다(ADR 0002). 남은 것은 전문을 얼마나 자주 보내는지
        /// 이고, 그것은 받는 쪽이 알 필요가 없다.
        internal static class Match
        {
            /// 목표물 전문의 간격(틱). 30Hz 기준 5초다.
            ///
            /// 다른 두 전문(2Hz)보다 훨씬 느리다. 배치는 매치 중에 거의 바뀌지 않으므로
            /// (열쇠가 주워질 때만) 자주 보낼 정보가 없고, 176B 를 2Hz 로 8명에게 보내면
            /// 2.8KB/s 가 더 붙는다 — 스냅샷 3.6KB/s 와 같은 자릿수다.
            ///
            /// **바뀐 틱에는 이 간격과 무관하게 즉시 보낸다.** 그러지 않으면 열쇠를 주운
            /// 뒤 최대 5초 동안 다른 클라이언트 화면에 그 열쇠가 남는다.
            public const int ObjectiveStateIntervalTicks = 5 * 30;

        }

        /// 소켓 없는 봇 참가자. **봇의 이동과 전투는 사람과 같은 상수를 쓴다** — 그것이
        /// 봇을 두는 목적이므로, 여기 있는 것은 두뇌가 목표를 정하는 방식뿐이다.
        ///
        /// 설정 파일로 빼지 않는다. 이 값들은 서로 맞물려 있고(도착 반경과 최소 전진량),
        /// 런타임에 바뀌어야 할 이유가 없다. 켤지·몇 명·어느 역할은 `BotOptions` 다.
        internal static class Bots
        {
            /// 봇 이름의 앞자리. `DisplayName.Sanitize` 규칙(출력 가능 ASCII) 안에 있으므로
            /// 코덱이 그대로 싣고 클라이언트가 명단에 그린다.
            public const string NamePrefix = "BOT ";

            /// 전진 입력의 크기. 축은 -127..127 로 정규화되어 있다(`InputFrame.MoveZ`).
            ///
            /// 최대값을 쓴다. 봇이 사람보다 느리게 걸으면 이동 판정에서 검증되는 구간이
            /// 좁아진다 — 속도 상한에 닿는 것이 이 단계가 보려는 것 중 하나다.
            public const sbyte ForwardAxis = 127;

            /// 목표점에 닿았다고 보는 수평 거리(m).
            ///
            /// 목표는 셀 중심이므로 정확히 도달할 필요가 없다. 좁게 두면 도착 직전에
            /// 미세하게 좌우로 흔들리고, 넓게 두면 배회가 잘게 끊긴다.
            public const float GoalReachRadius = 1.0f;

            /// 나아가지 못한 것으로 보는 틱 수. 30Hz 기준 0.5초다.
            ///
            /// 이것이 경로 탐색을 대신한다. 벽에 붙어 이 시간이 지나면 다른 목표를 뽑고,
            /// 결국 돌아서 나아간다. 짧게 두면 열린 방에서도 목표가 자꾸 바뀐다.
            public const int StuckTicks = 15;

            /// 한 틱에 이만큼도 나아가지 못하면 막힌 것으로 센다. 제자리 판정의 문턱이다.
            ///
            /// **한 틱 이동 거리에서 유도한다.** 숫자를 다시 적으면 이동 속도를 바꿀 때
            /// 이쪽이 남아, 느려진 봇이 영구히 "막혔다" 로 판정된다. 4분의 1 인 이유는
            /// 벽에 비스듬히 붙어 미끄러지는 경우를 막힌 것으로 세지 않기 위해서다.
            public const float MinStepSquared =
                SimConstants.MoveSpeed * SimConstants.TickDelta * 0.25f
                * (SimConstants.MoveSpeed * SimConstants.TickDelta * 0.25f);
        }

        /// 플레이어 하나의 상태와 입력 버퍼.
        internal static class Players
        {
            public const byte MaxHealth = 100;

            /// 지터를 흡수할 만큼만 담는다. 넘치면 오래된 입력을 버린다.
            /// `Rooms.MaxInputsPerTick` 으로 빠지는 속도보다 크게 잡는다.
            public const int InputBufferCapacity = 16;

            /// 클라이언트가 틱 카운터를 갑자기 크게 올리면 이후 입력이 전부 막힌다.
            /// 조작이든 버그든 플레이어가 영구히 굳으므로 도약을 이 폭으로 제한한다.
            public const uint MaxInputLead = 64;
        }

        /// 입력 판정. 계산은 `Shared` 가 하고 정당성 판단은 모듈이 한다.
        internal static class Validation
        {
            /// 스프린트까지 감안한 이론 최대 수평 속도. `SimConstants` 에서 유도한다 —
            /// 숫자를 다시 적으면 이동 속도를 바꿀 때 한쪽만 바뀐다.
            public const float MaxHorizontalSpeed = SimConstants.MoveSpeed * SimConstants.SprintMultiplier;

            /// 양자화 오차와 부동소수점 누적을 감안한 여유.
            public const float SpeedTolerance = 1.05f;
        }

        /// 접속 하나의 송수신 버퍼.
        internal static class Sessions
        {
            /// 밀리면 오래된 스냅샷을 버린다. 다음 틱이 대체하므로 유실이 문제되지 않는다.
            public const int OutboundCapacity = 32;

            /// 입력 메시지 최대 크기보다 크게 잡되, 이보다 큰 프레임은 끊는다.
            public const int ReceiveBufferBytes = 256;
        }

        /// 강제 퇴장. 룸의 판정과 전송 계층의 닫힘 코드가 이 한 곳을 본다.
        internal static class Kick
        {
            /// 룸이 전송에 넘기는 사유. **전송이 이 문자열로 닫힘 코드를 고른다.**
            ///
            /// 문자열로 가르는 것은 좋지 않지만, 대안은 `IServerTransport.Disconnect` 에
            /// 코드 인자를 더하는 것이고 그러면 `Shared` 의 전송 인터페이스가 게임 규칙을
            /// 알게 된다. 사유 문자열은 이미 그 인터페이스에 있으므로 새 표면을 만들지 않는다.
            public const string Reason = "kicked";

            /// 브라우저에 전달하는 닫힘 코드. 4000~4999 는 애플리케이션 용도로 열려 있다.
            ///
            /// **이 코드가 강제 퇴장의 유일한 신호다.** 없으면 클라이언트가 회선 절단으로
            /// 읽고 자동 재시도(0.5·1·2·4초)가 방금 내보낸 사람을 다시 데려온다.
            public const int CloseCode = 4003;
        }
    }
}
