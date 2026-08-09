using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 열쇠 요구량이 매치 인원을 따라간다.
    ///
    /// 고정 10개는 인원이 적을수록 가혹했다 — Runner 한 명이 술래를 피하며 열 개를 모으는
    /// 시간과, 넷이 나눠 모으는 시간은 같은 숫자가 아니다.
    public class KeyRequirementTests
    {
        [Theory]
        [InlineData(2, 3)]
        [InlineData(3, 5)]
        [InlineData(4, 8)]
        [InlineData(5, 10)]
        public void 인원별_요구량(int players, int keys)
        {
            Assert.Equal(keys, MatchConstants.KeysRequiredWith(players));
        }

        /// 방 정원(5)을 넘는 값이 들어와도 답이 있어야 한다. 정원은 고정 파라미터지만
        /// 이 함수가 그것을 강제하는 자리는 아니다.
        [Fact]
        public void 정원을_넘어도_상한에서_멈춘다()
        {
            Assert.Equal(MatchConstants.KeysRequired, MatchConstants.KeysRequiredWith(9));
        }

        /// 1인 이하는 매치가 성립하지 않지만 0 을 돌려주면 문이 **처음부터 열려 있다.**
        [Fact]
        public void 인원이_모자라도_0_을_돌려주지_않는다()
        {
            Assert.Equal(3, MatchConstants.KeysRequiredWith(1));
            Assert.Equal(3, MatchConstants.KeysRequiredWith(0));
        }

        /// 룸이 시작 인원으로 요구량을 정하고, 매치 내내 고정한다.
        [Fact]
        public void 룸이_시작_인원으로_요구량을_정한다()
        {
            var room = RoomFixture.Create();

            RoomFixture.FillAndStart(room, 2);

            Assert.Equal(3, room.Match.KeysRequired);
        }

        /// **사람이 빠져도 요구량은 그대로다.** 지금 인원에서 다시 구하면 팀이 무너질수록
        /// 문이 쉬워지고, 마지막 한 명이 남을수록 이기기 쉬운 규칙이 된다.
        [Fact]
        public void 사람이_빠져도_요구량은_줄지_않는다()
        {
            var room = RoomFixture.Create();

            RoomFixture.FillAndStart(room, 3);
            Assert.Equal(5, room.Match.KeysRequired);

            // Runner 하나를 내보낸다. 3인 매치는 한 명이 빠져도 계속된다.
            var seeker = room.Match.KeysRequired;   // 값을 읽어 두고 비교한다
            room.PostCommand(RoomCommand.Leave(3, 2));
            room.Advance();

            Assert.Equal(seeker, room.Match.KeysRequired);
            Assert.Equal(5, room.Match.KeysRequired);
        }
    }
}
