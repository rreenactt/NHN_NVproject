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
        public void 룸_상태_고정부는_15바이트다()
        {
            Assert.Equal(15, RoomStateHeader.WireSize);
        }

        [Fact]
        public void 제어는_3바이트다()
        {
            Assert.Equal(3, ControlMessage.WireSize);
        }

        /// 세션 수신·송신 버퍼가 이 값보다 커야 한다. 넘으면 접속이 끊긴다.
        [Fact]
        public void 여덟명_룸_상태_최대는_127바이트다()
        {
            Assert.Equal(127, MessageCodec.RoomStateMaxWireSize(8));
        }
    }
}
