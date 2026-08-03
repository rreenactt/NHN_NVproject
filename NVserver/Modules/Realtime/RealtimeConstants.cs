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

        /// 매치 판정 파라미터. 표시가 아니라 판정이므로 `MatchConstants` 가 아니라 여기다.
        ///
        /// 기준은 하나다 — **클라이언트가 이 값으로 무언가를 계산해 화면에 그리는가.**
        /// 아래 값들은 그렇지 않다. 배치 간격은 서버가 목표물을 놓을 때만 쓰고, 클라이언트는
        /// 결과 좌표를 받아 그리므로 간격을 알 필요가 없다.
        internal static class Match
        {
            /// 열쇠끼리, 그리고 이미 놓인 것과 떨어뜨릴 최소 거리(m).
            ///
            /// 기획서에 없다. 클라이언트의 배치가 쓰던 값을 그대로 옮겼다
            /// (`MatchManager.PlaceObjectives`) — 열쇠가 문간이나 서로 위에 겹쳐 생기면
            /// 목표가 우연히 짧아진다.
            public const float KeySpacing = 4f;

            /// 장치끼리 떨어뜨릴 최소 거리(m). 열쇠보다 크다 — 장치는 부피가 있고
            /// 상호작용 반경(`MatchConstants.DeviceUseRadius` 2.2m)이 겹치면 어느 것을
            /// 쓰는지 모호해진다.
            public const float DeviceSpacing = 5f;

            /// 간격 조건을 만족하는 자리를 찾는 시도 횟수.
            ///
            /// 다 쓰면 간격을 포기하고 아무 자리나 쓴다. 무한히 시도하지 않는 이유는
            /// 좁은 맵에서 조건을 만족하는 자리가 아예 없을 수 있기 때문이다 — 그때
            /// 목표물이 하나도 안 생기는 것보다 겹쳐서라도 생기는 편이 낫다.
            public const int PlacementAttempts = 64;

            /// 놓을 장치의 효과 조합. 배열 길이가 곧 최대 장치 수다.
            ///
            /// 기획서 §5 는 8~9개를 놓으라고 하고 효과는 6종이므로 남는 자리가 생긴다.
            /// 그 자리를 **다회 사용 효과**에 주는 것이 클라이언트의 선택이었고
            /// (`MatchManager.PlaceDevices`) 그대로 옮겼다 — 1회용을 두 개 놓으면 그
            /// 효과의 총량이 두 배가 되지만, 다회용을 두 개 놓으면 걸어가는 거리만 줄어든다.
            ///
            /// 룰셋이 "the mix of effects is a level-design choice" 로 위임한 부분이다.
            public static readonly MatchDeviceType[] DeviceMix =
            {
                MatchDeviceType.AddTime,
                MatchDeviceType.FullMapView,
                MatchDeviceType.StopBleeding,
                MatchDeviceType.FreezeAndXray,
                MatchDeviceType.SeekerCameraView,
                MatchDeviceType.Teleport,
                MatchDeviceType.Teleport,
                MatchDeviceType.FullMapView,
                MatchDeviceType.StopBleeding,
            };
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
