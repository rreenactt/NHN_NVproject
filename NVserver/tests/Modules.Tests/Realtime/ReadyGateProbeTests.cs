using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 준비 게이트가 실제로 막는가. **고치기 전에 재현부터 한다.**
    public class ReadyGateProbeTests
    {
        [Fact]
        public void 게스트가_준비하지_않으면_시작되지_않는다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, string.Empty, true));
            room.PostCommand(RoomCommand.Join(2, 1, string.Empty, false));
            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            Assert.Equal(RoomPhase.Waiting, room.Phase);
        }

        [Fact]
        public void 셋_중_하나만_준비해도_시작되지_않는다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, string.Empty, true));
            room.PostCommand(RoomCommand.Join(2, 1, string.Empty, false));
            room.PostCommand(RoomCommand.Join(3, 2, string.Empty, false));
            room.PostCommand(RoomCommand.SetReady(2, true));
            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            Assert.Equal(RoomPhase.Waiting, room.Phase);
        }

        /// 방장은 준비를 누르지 않는다 — 시작 버튼이 그 사람의 준비다.
        [Fact]
        public void 방장을_뺀_전원이_준비하면_시작된다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, string.Empty, true));
            room.PostCommand(RoomCommand.Join(2, 1, string.Empty, false));
            room.PostCommand(RoomCommand.SetReady(2, true));
            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            Assert.Equal(RoomPhase.Playing, room.Phase);
        }
    }
}
