using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Simulation
{
    /// 매치 규칙의 수치를 기획서에 못질한다.
    ///
    /// 상수에 로직이 없으니 테스트할 것도 없어 보이지만, 여기서 잡는 것은 계산이 아니라
    /// **문서와의 어긋남**이다. 밸런스를 만지다 보면 값이 조용히 흘러가고, 그때 기획서는
    /// 갱신되지 않는다. 이 테스트가 깨지면 둘 중 하나를 고르라는 신호다 — 기획서를 먼저
    /// 고치고 여기를 맞추거나, 값을 되돌리거나.
    ///
    /// 근거는 `docs/asymmetric_tag_shooter_game_design.md` 와 룰셋
    /// (`NVproject/.claude/skills/game-rules/references/ruleset.md`) 이다.
    public class MatchConstantsTests
    {
        [Fact]
        public void 기획서_3장의_목표_수치와_같다()
        {
            // "열쇠 10개 수집", "2명 이상 탈출 시 승리"
            Assert.Equal(10, MatchConstants.KeysRequired);
            Assert.Equal(2, MatchConstants.EscapesToWin);
        }

        [Fact]
        public void 기획서_4장의_전투_수치와_같다()
        {
            // §4.1 "2회 피격: 사망", §4.3 "탄창: 3발", "3초 행동 불가"
            Assert.Equal(2, MatchConstants.RunnerHitsToDie);
            Assert.Equal(3, MatchConstants.SeekerMagazine);
            Assert.Equal(3f, MatchConstants.ChainWait);
        }

        [Fact]
        public void 기획서_5장의_장치_수치와_같다()
        {
            // §5 "맵에는 8~9개의 장치 존재", §5.2 "쿨타임 12초"
            Assert.InRange(MatchConstants.DeviceCount, 8, 9);
            Assert.Equal(12f, MatchConstants.TeleportSharedCooldown);
        }

        /// 룰셋의 기본값. 기획서 §8 은 "시간 종료" 만 정하고 값을 주지 않는다(AS-2).
        [Fact]
        public void 매치_길이는_룰셋의_8분이다()
        {
            Assert.Equal(480f, MatchConstants.MatchDuration);
        }

        // ==================================================== 내부 정합성

        /// 뿌린 열쇠가 필요한 수보다 적으면 Runner 는 이길 방법이 없다. 증상은 버그가
        /// 아니라 "이 매치는 원래 못 이기는 것" 처럼 보이는 밸런스 문제로 나타난다.
        [Fact]
        public void 뿌리는_열쇠가_필요한_열쇠보다_적지_않다()
        {
            Assert.True(
                MatchConstants.KeysPlaced >= MatchConstants.KeysRequired,
                $"열쇠 {MatchConstants.KeysPlaced}개를 뿌리는데 {MatchConstants.KeysRequired}개가 필요하다.");
        }

        /// 하한이 상한보다 크면 견인 시간이 어느 쪽으로도 클램프되지 않는다.
        [Fact]
        public void 체인_견인_시간의_하한이_상한을_넘지_않는다()
        {
            Assert.True(MatchConstants.ChainDragTime <= MatchConstants.ChainDragMaxTime);
        }

        [Fact]
        public void 시간과_거리_상수가_모두_양수다()
        {
            Assert.True(MatchConstants.MatchDuration > 0f);
            Assert.True(MatchConstants.RoleRevealDuration > 0f);
            Assert.True(MatchConstants.KeyPickupRadius > 0f);
            Assert.True(MatchConstants.KeyInsertInterval > 0f);
            Assert.True(MatchConstants.DoorUseRadius > 0f);
            Assert.True(MatchConstants.EscapeHoldTime > 0f);
            Assert.True(MatchConstants.DeviceUseRadius > 0f);
            Assert.True(MatchConstants.ChainDragSpeed > 0f);
            Assert.True(MatchConstants.ChainReloadTime > 0f);
            Assert.True(MatchConstants.TeleportSharedCooldown > 0f);
            Assert.True(MatchConstants.RepeatableDeviceCooldown > 0f);
            Assert.True(MatchConstants.DeviceTimeBonus > 0f);
            Assert.True(MatchConstants.MapViewDuration > 0f);
            Assert.True(MatchConstants.SeekerCamDuration > 0f);
            Assert.True(MatchConstants.FreezeDuration > 0f);
        }

        [Fact]
        public void 세는_값이_모두_1_이상이다()
        {
            Assert.True(MatchConstants.KeysRequired >= 1);
            Assert.True(MatchConstants.EscapesToWin >= 1);
            Assert.True(MatchConstants.RunnerHitsToDie >= 1);
            Assert.True(MatchConstants.SeekerMagazine >= 1);
            Assert.True(MatchConstants.DeviceCount >= 1);
        }

        /// 소지 상한은 0 이하가 "무제한" 을 뜻하는 자리다. 음수도 같은 뜻이지만, 값이
        /// 음수가 되는 경로가 있다면 그것은 실수다.
        [Fact]
        public void 소지_상한은_음수가_아니다()
        {
            Assert.True(MatchConstants.CarryLimit >= 0);
        }

        /// 삽입 간격 × 필요한 열쇠 수가 매치 길이를 넘으면, 열쇠를 다 모아도 넣을
        /// 시간이 없다. 지금은 6초 대 480초로 여유가 크지만 두 값이 함께 조정될 때
        /// 놓치기 쉬운 관계다.
        [Fact]
        public void 열쇠를_전부_넣는_시간이_매치_길이를_넘지_않는다()
        {
            var insertTime = MatchConstants.KeyInsertInterval * MatchConstants.KeysRequired;

            Assert.True(
                insertTime < MatchConstants.MatchDuration,
                $"삽입에만 {insertTime}초가 필요한데 매치는 {MatchConstants.MatchDuration}초다.");
        }

        /// 역할 공개 동안은 이동이 잠긴다. 그 시간이 매치 길이에 가까우면 플레이할
        /// 시간이 남지 않는다.
        [Fact]
        public void 역할_공개가_매치_길이의_일부에_그친다()
        {
            Assert.True(MatchConstants.RoleRevealDuration < MatchConstants.MatchDuration * 0.1f);
        }

        /// **도달할 수 없는 승리 조건을 만들지 않는다.** Runner 가 한 명뿐인 방에서 2 를
        /// 요구하면 그 Runner 는 잘해도 이길 수 없고, 그 방은 아무도 남지 않은 채 시계만
        /// 끝까지 돈다 — 2인 매치가 정확히 그 경우다.
        [Fact]
        public void 탈출_목표는_Runner_수를_넘지_않는다()
        {
            Assert.Equal(1, MatchConstants.EscapesToWinWith(1));
            Assert.Equal(2, MatchConstants.EscapesToWinWith(2));
            Assert.Equal(2, MatchConstants.EscapesToWinWith(4));
        }

        /// 아직 아무도 없는 순간에는 기본값으로 답한다. 0 을 돌려주면 "이미 이겼다" 가 된다.
        [Fact]
        public void 인원을_모르면_기본_목표를_쓴다()
        {
            Assert.Equal(MatchConstants.EscapesToWin, MatchConstants.EscapesToWinWith(0));
        }
    }
}
