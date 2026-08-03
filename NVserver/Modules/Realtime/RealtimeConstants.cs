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

            /// 동시에 열어 두는 룸 수 상한. 임의의 쿼리스트링으로 룸이 무한히 생기는 것을 막는다.
            public const int MaxRooms = 16;

            /// 룸 id 최대 길이. 형식은 소문자·숫자·하이픈만 허용한다.
            /// 초대 코드는 이보다 짧지만, 설정으로 열어 두는 정적 룸의 id 도 같은 규칙을 쓴다.
            public const int MaxRoomIdLength = 32;

            /// 코드가 겹쳤을 때 다시 만들어 보는 횟수.
            ///
            /// 룸이 16개뿐이므로 31^6 공간에서 충돌은 실질적으로 일어나지 않는다.
            /// 그래도 무한 재시도를 두지 않는다 — 알파벳이나 길이를 줄이는 변경이
            /// 들어오면 이 자리가 조용히 무한 루프가 된다.
            public const int CodeGenerationAttempts = 8;

            /// 아무도 없는 룸을 회수할 때까지의 틱. 30Hz 기준 60초.
            ///
            /// 방을 만들고 접속하지 않은 경우가 이 값으로 정리된다. 그 경로를 막지
            /// 않으면 룸 상한 16개가 아무도 들어오지 않는 방으로 차고, 증상은
            /// "방 만들기가 실패한다" 로만 나타난다.
            ///
            /// 단계별로 다른 값을 두지 않는다. 전원이 나가면 룸이 스스로 대기 단계로
            /// 돌아가므로 비어 있으면서 진행 중인 룸은 존재하지 않는다.
            public const uint EmptyExpiryTicks = 30 * 60;

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

            /// 새 입력이 없을 때 마지막 입력을 반복하는 상한.
            /// 짧은 손실은 흡수하고, 그 이상 끊기면 이동을 멈춘다.
            /// 무제한 허용하면 입력을 끊은 클라이언트가 계속 달린다.
            public const int MaxInputRepeatTicks = 3;
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
    }
}
