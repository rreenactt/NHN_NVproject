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
    }
}
