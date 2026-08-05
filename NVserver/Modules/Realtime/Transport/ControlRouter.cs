using System;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;

namespace NV.Realtime.Transport
{
    /// 제어 프레임 하나를 룸 커맨드 하나로 옮긴다.
    ///
    /// **`GameSession` 에서 뺀 이유는 시험할 수 없었기 때문이다.** 그 클래스는 `WebSocket` 을
    /// 들고 있어 소켓 없이 만들 수 없고, 그래서 이 변환은 테스트가 닿지 않는 자리에 있었다.
    ///
    /// 그 사이에 실제로 기능이 죽었다. 대기방의 제어 넷을 추가했는데 `MessageCodec.ReadControl`
    /// 의 화이트리스트를 고치지 않아 넷 다 여기서 버려졌고, 룸의 판정은 32개 테스트로 통과하는데
    /// 화면에서는 아무 일도 일어나지 않았다 — 룸 테스트가 `RoomCommand` 를 큐에 직접 넣어 이
    /// 경로를 지나지 않았기 때문이다. `conventions.md` 에 그 함정을 적어 두었다.
    ///
    /// 이 경로에는 **손으로 유지하는 목록이 둘** 있다: 코덱의 정의된 종류 목록과 아래의 `switch`.
    /// 둘 다 빠뜨리면 조용히 실패하므로, 열거형의 모든 값이 커맨드로 변환되는지 검사하는
    /// 테스트가 둘을 함께 못질한다.
    internal static class ControlRouter
    {
        /// <param name="sessionId">요청한 세션. 자격 판정은 룸이 틱 경계에서 한다.</param>
        /// <param name="error">거부 사유. 실패했을 때만 채워진다 — 로그에만 쓴다.</param>
        /// <returns>커맨드를 만들었는가. 거짓이면 아무것도 붙이지 않는다.</returns>
        internal static bool TryRoute(
            ReadOnlySpan<byte> payload,
            int sessionId,
            out RoomCommand command,
            out string? error)
        {
            command = default;

            ControlMessage message;

            try
            {
                message = MessageCodec.ReadControl(payload);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                error = exception.Message;
                return false;
            }

            error = null;

            switch (message.Kind)
            {
                case ControlKind.StartMatch:
                    command = RoomCommand.Start(sessionId);
                    return true;

                case ControlKind.EndMatch:
                    command = RoomCommand.EndMatch(sessionId, message.Value);
                    return true;

                case ControlKind.ReturnToLobby:
                    command = RoomCommand.ReturnToLobby(sessionId);
                    return true;

                case ControlKind.SetReady:
                    // 0 이 아니면 참으로 읽는다. 손상된 값을 거부하기보다 받아들이는 쪽이
                    // 안전하다 — 준비는 되돌릴 수 있고, 거부하면 화면이 눌린 채로 남는다.
                    command = RoomCommand.SetReady(sessionId, message.Value != 0);
                    return true;

                case ControlKind.SetCharacter:
                    // 범위와 중복은 룸이 본다. 여기서 미리 거르면 한 틱 뒤의 진실과 어긋난다 —
                    // 그 사이에 다른 사람이 같은 캐릭터를 집을 수 있다.
                    command = RoomCommand.SetCharacter(sessionId, message.Value);
                    return true;

                case ControlKind.KickPlayer:
                    command = RoomCommand.Kick(sessionId, message.Value);
                    return true;

                case ControlKind.TransferHost:
                    command = RoomCommand.TransferHost(sessionId, message.Value);
                    return true;

                default:
                    // 코덱이 통과시켰지만 여기 자리가 없는 종류. 코덱과 이 목록 중 한쪽만
                    // 고친 상태이며, 그것이 조용한 실패의 정체다.
                    error = $"옮길 자리가 없는 제어 종류다: {(byte)message.Kind}";
                    return false;
            }
        }
    }
}
