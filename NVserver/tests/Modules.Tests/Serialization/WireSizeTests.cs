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

        [Fact]
        public void 여덟명_스냅샷은_114바이트다()
        {
            Assert.Equal(114, MessageCodec.SnapshotWireSize(8));
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
        /// 123 이었다. 명단 항목이 2B 에서 4B 로 넓어졌다 — 준비 플래그와 캐릭터 번호가
        /// 붙었고, 그것이 대기방을 서버 권위로 만든 자리다(프로토콜 4).
        [Fact]
        public void 여덟명_룸_상태_최대는_139바이트다()
        {
            Assert.Equal(139, MessageCodec.RoomStateMaxWireSize(8));
        }

        /// 항목 고정부. 늘어나면 위의 상한도 함께 늘어난다.
        [Fact]
        public void 명단_항목_고정부는_4바이트다()
        {
            Assert.Equal(4, RoomPlayerEntry.FixedWireSize);
        }
    }
}
