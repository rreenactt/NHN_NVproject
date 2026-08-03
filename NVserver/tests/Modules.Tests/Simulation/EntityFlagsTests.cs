using System.Numerics;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Simulation
{
    /// 한 바이트에 두 종류의 플래그가 섞인다. 이 테스트가 지키는 것은 **그 둘이 서로를
    /// 밟지 않는다는 것** 이다 — 이동이 소유하는 비트와 매치 판정이 소유하는 비트.
    public class EntityFlagsTests
    {
        private const EntityFlags SimulationBits =
            EntityFlags.Alive | EntityFlags.OnGround | EntityFlags.Crouching;

        private const EntityFlags MatchBits =
            EntityFlags.Bleeding | EntityFlags.Seeker | EntityFlags.Escaped | EntityFlags.Frozen;

        /// `EntityState.Flags` 는 1바이트다. 비트가 8개를 넘으면 조용히 잘린다.
        [Fact]
        public void 플래그가_8비트를_넘지_않는다()
        {
            Assert.Equal(0, (byte)SimulationBits & 0xF8 & ~0x07);
            Assert.True((int)(SimulationBits | MatchBits) <= 0xFF);
        }

        /// 두 묶음이 겹치면 이동이 매치 비트를 덮거나 그 반대가 된다.
        [Fact]
        public void 이동_비트와_매치_비트가_겹치지_않는다()
        {
            Assert.Equal(EntityFlags.None, SimulationBits & MatchBits);
        }

        [Fact]
        public void 비트_값이_고정되어_있다()
        {
            // 와이어에 나가는 값이다. 바뀌면 구버전 클라이언트가 다른 뜻으로 읽는다.
            Assert.Equal(1, (byte)EntityFlags.Alive);
            Assert.Equal(2, (byte)EntityFlags.OnGround);
            Assert.Equal(4, (byte)EntityFlags.Crouching);
            Assert.Equal(8, (byte)EntityFlags.Bleeding);
            Assert.Equal(16, (byte)EntityFlags.Seeker);
            Assert.Equal(32, (byte)EntityFlags.Escaped);
            Assert.Equal(64, (byte)EntityFlags.Frozen);
        }

        /// `EntityState` 가 커지면 8인 스냅샷이 114B 를 넘고 대역폭 계산이 어긋난다.
        [Fact]
        public void 플래그를_늘려도_엔티티_크기가_그대로다()
        {
            Assert.Equal(13, EntityState.WireSize);
        }

        // ==================================================== 투영

        private static PlayerState Standing()
        {
            var state = PlayerState.Spawn(new Vector3(1f, 0f, 2f), 0.5f, 100);
            state.Flags |= EntityFlags.OnGround;
            return state;
        }

        [Fact]
        public void 매치_비트를_주지_않으면_이동_비트만_실린다()
        {
            var wire = StateProjection.ToEntityState(3, Standing());

            Assert.Equal(EntityFlags.Alive | EntityFlags.OnGround, wire.Flags);
            Assert.Equal(EntityFlags.None, StateProjection.MatchFlagsOf(wire.Flags));
        }

        [Fact]
        public void 매치_비트가_이동_비트_위에_얹힌다()
        {
            var wire = StateProjection.ToEntityState(
                3,
                Standing(),
                EntityFlags.Seeker | EntityFlags.Frozen);

            // 둘 다 살아 있어야 한다. 얹는 쪽이 덮어쓰면 OnGround 가 사라져 원격 몸이
            // 공중에 뜬 것으로 보인다.
            Assert.True((wire.Flags & EntityFlags.Alive) != 0);
            Assert.True((wire.Flags & EntityFlags.OnGround) != 0);
            Assert.True((wire.Flags & EntityFlags.Seeker) != 0);
            Assert.True((wire.Flags & EntityFlags.Frozen) != 0);
        }

        [Fact]
        public void 두_묶음을_다시_갈라낼_수_있다()
        {
            var wire = StateProjection.ToEntityState(
                0,
                Standing(),
                EntityFlags.Bleeding | EntityFlags.Seeker);

            Assert.Equal(
                EntityFlags.Alive | EntityFlags.OnGround,
                StateProjection.SimulationFlagsOf(wire.Flags));

            Assert.Equal(
                EntityFlags.Bleeding | EntityFlags.Seeker,
                StateProjection.MatchFlagsOf(wire.Flags));
        }

        /// **클라이언트가 예측 상태와 비교할 때 매치 비트를 빼야 한다.** 빼지 않으면
        /// 서버가 보낸 몸과 자기 예측이 항상 달라 보이고, 리컨실리에이션이 매 틱 보정한다.
        [Fact]
        public void 예측_비교에서_매치_비트를_걸러내면_같아진다()
        {
            var predicted = Standing();

            var fromServer = StateProjection.ToEntityState(
                0,
                predicted,
                EntityFlags.Seeker | EntityFlags.Frozen);

            Assert.NotEqual(predicted.Flags, fromServer.Flags);
            Assert.Equal(predicted.Flags, StateProjection.SimulationFlagsOf(fromServer.Flags));
        }

        /// 매치 비트를 `PlayerState` 에 담지 않는 이유를 고정한다. 담으면 `StateHash` 가
        /// 달라지고, 클라이언트는 그 비트를 예측할 수 없으므로 해시 비교가 영구히 어긋난다.
        [Fact]
        public void 매치_비트는_시뮬레이션_상태의_해시를_바꾸지_않는다()
        {
            var state = Standing();
            var before = StateHash.Of(state);

            // 서버가 하는 것 — 상태는 그대로 두고 인코딩할 때만 얹는다.
            var wire = StateProjection.ToEntityState(0, state, EntityFlags.Seeker);
            _ = wire;

            Assert.Equal(before, StateHash.Of(state));
        }

        [Fact]
        public void 와이어에서_되돌린_상태도_매치_비트를_들고_있다()
        {
            var wire = StateProjection.ToEntityState(0, Standing(), EntityFlags.Bleeding);
            var restored = StateProjection.ToPlayerState(wire);

            // 원격 몸의 표현(피 흔적)이 이 비트를 읽는다.
            Assert.True((restored.Flags & EntityFlags.Bleeding) != 0);

            // 이동 판정에 쓰는 프로퍼티는 매치 비트에 흔들리지 않아야 한다.
            Assert.True(restored.IsGrounded);
            Assert.False(restored.IsCrouching);
        }
    }
}
