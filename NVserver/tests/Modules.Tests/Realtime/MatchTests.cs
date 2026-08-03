using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 매치의 단계와 시계. 이 계산이 클라이언트에서 서버로 옮겨 온 첫 판정이다.
    public class MatchTests
    {
        private static int TicksFor(float seconds)
        {
            return (int)(seconds * SimConstants.TickRate);
        }

        [Fact]
        public void 시작하지_않은_매치는_로비다()
        {
            var match = new Match();

            Assert.Equal(MatchPhase.Lobby, match.Phase);
            Assert.False(match.MovementLocked);
        }

        [Fact]
        public void 시작하면_역할_공개부터다()
        {
            var match = new Match();
            match.Begin();

            Assert.Equal(MatchPhase.RoleReveal, match.Phase);
            Assert.True(match.MovementLocked);
        }

        /// 리빌 동안 남은 시간이 0 으로 보이면 그 값이 전문에 실려 클라이언트 HUD 가
        /// "시간 종료" 를 그린다. 시계는 시작 시점에 채워져 있어야 한다.
        [Fact]
        public void 역할_공개_중에도_매치_시계가_이미_채워져_있다()
        {
            var match = new Match();
            match.Begin();

            Assert.Equal(MatchConstants.MatchDuration, match.MatchSecondsRemaining, 3);
            Assert.Equal(MatchConstants.RoleRevealDuration, match.RevealSecondsRemaining, 3);
        }

        [Fact]
        public void 역할_공개가_끝나면_진행으로_간다()
        {
            var match = new Match();
            match.Begin();

            var revealTicks = TicksFor(MatchConstants.RoleRevealDuration);

            for (var tick = 0; tick < revealTicks - 1; tick++)
            {
                Assert.False(match.Advance());
                Assert.Equal(MatchPhase.RoleReveal, match.Phase);
            }

            // 마지막 리빌 틱이 단계를 넘긴다. 매치가 끝난 것은 아니므로 false 다.
            Assert.False(match.Advance());
            Assert.Equal(MatchPhase.Playing, match.Phase);
            Assert.False(match.MovementLocked);
            Assert.Equal(0f, match.RevealSecondsRemaining);
        }

        /// 리빌 동안에는 시계가 줄지 않아야 한다. 줄면 매치가 4초 짧아진다.
        [Fact]
        public void 역할_공개_중에는_매치_시계가_줄지_않는다()
        {
            var match = new Match();
            match.Begin();

            var before = match.MatchTicksRemaining;

            for (var tick = 0; tick < TicksFor(MatchConstants.RoleRevealDuration) - 1; tick++)
            {
                match.Advance();
            }

            Assert.Equal(before, match.MatchTicksRemaining);
        }

        [Fact]
        public void 진행_중에는_매치_시계가_틱마다_줄어든다()
        {
            var match = AtPlaying();
            var before = match.MatchTicksRemaining;

            match.Advance();

            Assert.Equal(before - 1, match.MatchTicksRemaining);
        }

        /// 기획서 §8 — 시간 종료. 단계는 넘어가지만 결과는 여기서 정하지 않는다(IG-007).
        [Fact]
        public void 시간이_다_되면_끝났다고_알린다()
        {
            var match = AtPlaying();

            var ticks = match.MatchTicksRemaining;
            for (var tick = 0; tick < ticks - 1; tick++)
            {
                Assert.False(match.Advance());
            }

            Assert.True(match.Advance());
            Assert.Equal(MatchPhase.Ended, match.Phase);
            Assert.Equal(0f, match.MatchSecondsRemaining);
        }

        /// 끝났다는 신호는 **한 번만** 나와야 한다. 매 틱 나오면 룸이 종료 처리를
        /// 반복하고, 로그와 전문이 매 틱 갱신된다.
        [Fact]
        public void 끝났다는_신호는_한_번만_나온다()
        {
            var match = AtPlaying();

            var ticks = match.MatchTicksRemaining;
            for (var tick = 0; tick < ticks - 1; tick++)
            {
                match.Advance();
            }

            Assert.True(match.Advance());

            for (var tick = 0; tick < 10; tick++)
            {
                Assert.False(match.Advance());
            }
        }

        [Fact]
        public void 끝난_매치는_이동을_잠근다()
        {
            var match = AtPlaying();
            match.ForceEnd();

            Assert.Equal(MatchPhase.Ended, match.Phase);
            Assert.True(match.MovementLocked);
            Assert.Equal(0f, match.MatchSecondsRemaining);
        }

        [Fact]
        public void 되돌리면_로비로_간다()
        {
            var match = AtPlaying();
            match.Reset();

            Assert.Equal(MatchPhase.Lobby, match.Phase);
            Assert.False(match.MovementLocked);
        }

        /// 다시 시작하면 시계도 다시 찬다. 재시작이 이전 매치의 남은 시간을 물려받으면
        /// 두 번째 매치가 짧아진다.
        [Fact]
        public void 다시_시작하면_시계가_다시_찬다()
        {
            var match = AtPlaying();

            for (var tick = 0; tick < 300; tick++)
            {
                match.Advance();
            }

            Assert.True(match.MatchTicksRemaining < TicksFor(MatchConstants.MatchDuration));

            match.Reset();
            match.Begin();

            Assert.Equal(TicksFor(MatchConstants.MatchDuration), match.MatchTicksRemaining);
            Assert.Equal(MatchPhase.RoleReveal, match.Phase);
        }

        /// 틱으로 세므로 프레임레이트와 무관하다. 30Hz 기준 480초는 정확히 14400틱이고
        /// 나머지가 없다.
        [Fact]
        public void 시계가_고정_틱으로_환산된다()
        {
            var match = new Match();
            match.Begin();

            Assert.Equal(14400, match.MatchTicksRemaining);
            Assert.Equal(120, match.RevealTicksRemaining);
        }

        private static Match AtPlaying()
        {
            var match = new Match();
            match.Begin();

            for (var tick = 0; tick < TicksFor(MatchConstants.RoleRevealDuration); tick++)
            {
                match.Advance();
            }

            Assert.Equal(MatchPhase.Playing, match.Phase);
            return match;
        }
    }
}
