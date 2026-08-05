using System;
using NV.Realtime.Simulation;
using NV.Realtime.Transport;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 제어 프레임이 룸 커맨드에 **도달하는가.**
    ///
    /// 룸 테스트는 `RoomCommand` 를 큐에 직접 넣는다. 그것은 판정이 옳은지를 검사하는 옳은
    /// 방법이지만 **요청이 그 판정에 도달하는지는 아무것도 말하지 않는다.** 실제로 대기방 제어
    /// 넷이 코덱의 화이트리스트에서 버려지는 동안, 룸 판정 테스트 32개가 전부 통과하고 있었다.
    ///
    /// 이 파일이 그 구멍이다. 경로에 손으로 유지하는 목록이 둘 있고(코덱의 정의된 종류,
    /// 라우터의 `switch`) 둘 중 하나만 고쳐도 조용히 실패한다.
    public class ControlRouterTests
    {
        private const int SessionId = 7;

        /// **모든 종류가 커맨드로 옮겨져야 한다.**
        ///
        /// 목록을 여기에 다시 적지 않고 열거형을 훑는다 — 적으면 같은 실수를 두 곳에서 하게
        /// 되고, 그때 이 테스트는 잘못된 것을 통과시킨다.
        [Fact]
        public void 모든_제어_종류가_커맨드로_옮겨진다()
        {
            foreach (ControlKind kind in Enum.GetValues(typeof(ControlKind)))
            {
                if (kind == ControlKind.None)
                {
                    continue;
                }

                var routed = ControlRouter.TryRoute(Frame(kind, 1), SessionId, out var command, out var error);

                Assert.True(routed, $"{kind} 를 옮기지 못했다: {error}");
                Assert.Equal(SessionId, command.SessionId);
            }
        }

        /// 짝을 바이트로 받는다. `RoomCommandKind` 는 `internal` 이고 xUnit 은 `public` 메서드만
        /// 찾으므로, 그 타입을 인자로 두면 접근성이 어긋난다.
        [Theory]
        [InlineData(ControlKind.StartMatch, (byte)RoomCommandKind.Start)]
        [InlineData(ControlKind.EndMatch, (byte)RoomCommandKind.EndMatch)]
        [InlineData(ControlKind.ReturnToLobby, (byte)RoomCommandKind.ReturnToLobby)]
        [InlineData(ControlKind.SetReady, (byte)RoomCommandKind.SetReady)]
        [InlineData(ControlKind.SetCharacter, (byte)RoomCommandKind.SetCharacter)]
        [InlineData(ControlKind.KickPlayer, (byte)RoomCommandKind.Kick)]
        [InlineData(ControlKind.TransferHost, (byte)RoomCommandKind.TransferHost)]
        public void 종류가_짝이_맞는_커맨드가_된다(ControlKind kind, byte expected)
        {
            Assert.True(ControlRouter.TryRoute(Frame(kind, 1), SessionId, out var command, out _));
            Assert.Equal(expected, (byte)command.Kind);
        }

        /// 값을 싣는 종류는 값을 잃지 않아야 한다.
        ///
        /// 잃으면 캐릭터 선택이 0번 고정이 되고, 강제 퇴장과 방장 위임이 0번 슬롯을 가리킨다 —
        /// 셋 다 "눌렀는데 엉뚱한 일이 난다" 로만 나타난다.
        [Theory]
        [InlineData(ControlKind.SetCharacter, 5)]
        [InlineData(ControlKind.SetCharacter, 7)]
        [InlineData(ControlKind.KickPlayer, 3)]
        [InlineData(ControlKind.TransferHost, 4)]
        [InlineData(ControlKind.EndMatch, 2)]
        public void 값을_싣는_종류는_값을_옮긴다(ControlKind kind, byte value)
        {
            Assert.True(ControlRouter.TryRoute(Frame(kind, value), SessionId, out var command, out _));
            Assert.Equal(value, command.Value);
        }

        /// 준비는 0/1 로 정규화된다. 0 이 아니면 참이다.
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(200, 1)]
        public void 준비는_0이_아니면_참이_된다(byte sent, byte expected)
        {
            Assert.True(ControlRouter.TryRoute(Frame(ControlKind.SetReady, sent), SessionId, out var command, out _));
            Assert.Equal(expected, command.Value);
        }

        [Fact]
        public void 정의되지_않은_종류는_버린다()
        {
            var payload = new byte[ControlMessage.WireSize];
            payload[0] = (byte)MessageOpcode.Control;
            payload[1] = 200;      // 없는 종류
            payload[2] = 0;

            Assert.False(ControlRouter.TryRoute(payload, SessionId, out _, out var error));
            Assert.False(string.IsNullOrEmpty(error), "버린 이유가 있어야 한다.");
        }

        /// 비워 둔 2번. 자발적 퇴장을 두었다 뺀 자리이며 되살아나면 안 된다.
        [Fact]
        public void 비워_둔_2번은_버린다()
        {
            var payload = new byte[ControlMessage.WireSize];
            payload[0] = (byte)MessageOpcode.Control;
            payload[1] = 2;
            payload[2] = 0;

            Assert.False(ControlRouter.TryRoute(payload, SessionId, out _, out _));
        }

        [Fact]
        public void 다른_opcode_는_버린다()
        {
            var payload = new byte[ControlMessage.WireSize];
            payload[0] = (byte)MessageOpcode.Input;
            payload[1] = (byte)ControlKind.SetCharacter;
            payload[2] = 3;

            Assert.False(ControlRouter.TryRoute(payload, SessionId, out _, out _));
        }

        /// 클라이언트가 보내는 것과 **같은 방법으로** 만든다. 손으로 바이트를 적으면 코덱을
        /// 지나지 않아 이 테스트가 코덱의 버그를 놓친다.
        private static byte[] Frame(ControlKind kind, byte value)
        {
            var payload = new byte[ControlMessage.WireSize];
            MessageCodec.WriteControl(payload, new ControlMessage(kind, value));
            return payload;
        }
    }
}
