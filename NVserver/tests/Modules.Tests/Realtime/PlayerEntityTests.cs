using System.Numerics;
using NV.Realtime.Simulation;
using NV.Realtime.Transport;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    public class PlayerEntityTests
    {
        private static PlayerEntity Player()
        {
            return new PlayerEntity(1, 0, new Vector3(0f, 0f, 0f), 0f);
        }

        private static InboundInput Input(uint tick)
        {
            return new InboundInput(1, tick, tick, new InputFrame(ButtonFlags.None, 0, 0, 0, 0));
        }

        [Fact]
        public void 입력은_틱_순서대로_나온다()
        {
            var player = Player();

            // 순서가 뒤섞여 도착해도 적용은 오름차순이어야 한다.
            Assert.True(player.TryBuffer(Input(3)));
            Assert.True(player.TryBuffer(Input(1)));
            Assert.True(player.TryBuffer(Input(2)));

            Assert.True(player.TryTakeNext(out var first));
            Assert.True(player.TryTakeNext(out var second));
            Assert.True(player.TryTakeNext(out var third));

            Assert.Equal(1u, first.Tick);
            Assert.Equal(2u, second.Tick);
            Assert.Equal(3u, third.Tick);
            Assert.False(player.TryTakeNext(out _));
        }

        [Fact]
        public void 중복_전송된_같은_틱은_한_번만_담긴다()
        {
            var player = Player();

            Assert.True(player.TryBuffer(Input(5)));
            Assert.False(player.TryBuffer(Input(5)));

            Assert.Equal(1, player.BufferedInputCount);
        }

        [Fact]
        public void 이미_적용한_틱은_다시_담기지_않는다()
        {
            var player = Player();

            player.TryBuffer(Input(10));
            player.TryTakeNext(out _);

            Assert.False(player.TryBuffer(Input(10)));
            Assert.False(player.TryBuffer(Input(9)));
            Assert.True(player.TryBuffer(Input(11)));
        }

        [Fact]
        public void 틱_카운터_도약은_거부된다()
        {
            // 이걸 받아들이면 LastProcessedInputTick 이 튀어 이후 입력이 전부 막힌다.
            var player = Player();

            Assert.True(player.TryBuffer(Input(1)));
            Assert.False(player.TryBuffer(Input(uint.MaxValue)));
            Assert.False(player.TryBuffer(Input(1 + PlayerEntity.MaxInputLead + 1)));
            Assert.True(player.TryBuffer(Input(1 + PlayerEntity.MaxInputLead)));
        }

        [Fact]
        public void 첫_입력은_틱_번호가_커도_받는다()
        {
            // 클라이언트 틱 카운터가 0 에서 시작한다는 보장이 없다.
            var player = Player();

            Assert.True(player.TryBuffer(Input(1_000_000u)));
            Assert.Equal(1, player.BufferedInputCount);
        }

        [Fact]
        public void 버퍼가_가득_차면_오래된_입력을_버린다()
        {
            var player = Player();

            for (var tick = 1u; tick <= 30u; tick++)
            {
                player.TryBuffer(Input(tick));
            }

            Assert.True(player.BufferedInputCount <= 16);

            // 가장 최근 입력은 살아 있어야 한다.
            player.TryTakeNext(out var oldest);
            Assert.True(oldest.Tick > 1u, $"oldest = {oldest.Tick}");
        }

        [Fact]
        public void 스폰_위치와_요가_초기_상태에_반영된다()
        {
            var player = new PlayerEntity(7, 3, new Vector3(5f, 0f, -5f), 1.5f);

            Assert.Equal(5f, player.State.Position.X);
            Assert.Equal(-5f, player.State.Position.Z);
            Assert.Equal(1.5f, player.State.Yaw);
            Assert.Equal(3, player.Wire.Id);
            Assert.Equal(PlayerEntity.MaxHealth, player.State.Health);
        }
    }
}
