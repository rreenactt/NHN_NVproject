using NV.Realtime;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using Xunit;

namespace NV.Modules.Tests.Serialization
{
    /// 와이어 크기가 바뀌면 클라이언트와 서버가 조용히 어긋난다.
    /// 프로토콜 버전을 올리지 않고 크기를 바꾸는 것을 막는 잠금장치다.
    public class WireSizeTests
    {
        [Fact]
        public void InputFrame은_7바이트다()
        {
            Assert.Equal(7, InputFrame.WireSize);
        }

        [Fact]
        public void EntityState는_13바이트다()
        {
            Assert.Equal(13, EntityState.WireSize);
        }

        [Fact]
        public void 스냅샷_헤더는_10바이트다()
        {
            Assert.Equal(10, SnapshotHeader.WireSize);
        }

        /// 정원만큼의 몸이 실린 한 틱. **정원에서 유도한다** — 8 을 적어 두면 정원이 바뀐 뒤에도
        /// 통과하면서 아무것도 말하지 않는 테스트가 된다.
        ///
        /// 114 였다(8명). 정원이 5 로 내려갔다.
        [Fact]
        public void 정원만큼의_스냅샷은_75바이트다()
        {
            Assert.Equal(75, MessageCodec.SnapshotWireSize(RealtimeConstants.Rooms.MaxPlayers));
        }

        /// 엔티티 하나당 얼마인가. 위의 값이 이것과 정원의 곱에 헤더를 더한 것이다.
        [Fact]
        public void 스냅샷은_엔티티당_13바이트씩_늘어난다()
        {
            Assert.Equal(
                MessageCodec.SnapshotWireSize(4) + EntityState.WireSize,
                MessageCodec.SnapshotWireSize(5));
        }

        [Fact]
        public void 세프레임_입력은_27바이트다()
        {
            Assert.Equal(27, MessageCodec.InputWireSize(3));
        }

        [Fact]
        /// 15 였다. **배치 씨드 4바이트가 빠졌다** — 그 필드를 받은 Seeker 는 문의 좌표를
        /// 계산할 수 있었고, 그것이 이 게임의 정보 규칙을 어기는 경로였다. 다시 늘어나면
        /// 무엇이 실리는지 확인해야 한다.
        public void 룸_상태_고정부는_11바이트다()
        {
            Assert.Equal(11, RoomStateHeader.WireSize);
        }

        [Fact]
        public void 제어는_3바이트다()
        {
            Assert.Equal(3, ControlMessage.WireSize);
        }

        /// 세션 수신·송신 버퍼가 이 값보다 커야 한다. 넘으면 접속이 끊긴다.
        ///
        /// **정원에서 유도한다.** 이름이 전부 상한까지 찬 최악의 경우이며, 정원이 바뀌면 이
        /// 값도 바뀌어야 한다 — 고정 숫자를 넣으면 정원을 내린 뒤에도 통과하면서 버퍼가
        /// 충분한지에 대해 아무것도 말하지 않는다.
        ///
        /// 123(8명, 항목 2B) → 139(8명, 항목 4B) → 91(5명). 항목이 넓어진 것은 준비 플래그와
        /// 캐릭터 번호이며, 그것이 대기방을 서버 권위로 만든 자리다(프로토콜 4).
        [Fact]
        public void 정원만큼의_룸_상태_최대는_91바이트다()
        {
            Assert.Equal(91, MessageCodec.RoomStateMaxWireSize(RealtimeConstants.Rooms.MaxPlayers));
        }

        /// 항목 고정부. 늘어나면 위의 상한도 함께 늘어난다.
        [Fact]
        public void 명단_항목_고정부는_4바이트다()
        {
            Assert.Equal(4, RoomPlayerEntry.FixedWireSize);
        }
    }
}
