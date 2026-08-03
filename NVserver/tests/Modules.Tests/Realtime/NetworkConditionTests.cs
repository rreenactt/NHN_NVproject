using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 조건 주입기가 꺼진 경로에 영향을 주지 않는 것과,
    /// 켰을 때 실제로 지연·손실이 생기는 것을 확인한다.
    public class NetworkConditionTests
    {
        private static InputFrame Forward()
        {
            return new InputFrame(ButtonFlags.None, 0, 127, 0, 0);
        }

        [Fact]
        public void 꺼져_있으면_지연도_손실도_없다()
        {
            var simulator = RoomFixture.NoConditions();

            Assert.False(simulator.Enabled);
            Assert.Equal(0u, simulator.DelayTicks());
            Assert.False(simulator.ShouldDrop());
        }

        [Fact]
        public void 지연은_틱_단위로_반올림된다()
        {
            // 30Hz 에서 한 틱은 33.3ms 다. 120ms 는 3.6틱이므로 4틱이다.
            var simulator = RoomFixture.Conditions(120, 0, 0.0);

            Assert.True(simulator.Enabled);
            Assert.Equal(4, simulator.BaseDelayTicks);
            Assert.Equal(4u, simulator.DelayTicks());
        }

        [Fact]
        public void 반틱_미만의_지연은_0틱이_된다()
        {
            var simulator = RoomFixture.Conditions(10, 0, 0.0);

            Assert.Equal(0, simulator.BaseDelayTicks);
        }

        [Fact]
        public void 마일스톤_기준인_30ms_지터가_표현된다()
        {
            // 절삭하면 0틱이 되어 지터 설정이 조용히 무효가 된다.
            var simulator = RoomFixture.Conditions(120, 30, 0.0);

            Assert.Equal(1, simulator.JitterTicks);
        }

        [Fact]
        public void 지터는_지연을_흔든다()
        {
            var simulator = RoomFixture.Conditions(120, 60, 0.0);

            var seen = new System.Collections.Generic.HashSet<uint>();
            for (var sample = 0; sample < 200; sample++)
            {
                seen.Add(simulator.DelayTicks());
            }

            Assert.True(seen.Count > 1, "지터가 적용되지 않았다.");

            foreach (var delay in seen)
            {
                Assert.True(delay <= (uint)(simulator.BaseDelayTicks + simulator.JitterTicks), $"delay = {delay}");
            }
        }

        [Fact]
        public void 손실_100퍼센트면_전부_버린다()
        {
            var simulator = RoomFixture.Conditions(0, 0, 1.0);

            for (var sample = 0; sample < 100; sample++)
            {
                Assert.True(simulator.ShouldDrop());
            }
        }

        [Fact]
        public void 손실_0퍼센트면_하나도_버리지_않는다()
        {
            var simulator = RoomFixture.Conditions(120, 30, 0.0);

            for (var sample = 0; sample < 100; sample++)
            {
                Assert.False(simulator.ShouldDrop());
            }
        }

        [Fact]
        public void 시드가_같으면_같은_순서를_낸다()
        {
            var first = RoomFixture.Conditions(120, 30, 0.1);
            var second = RoomFixture.Conditions(120, 30, 0.1);

            for (var sample = 0; sample < 50; sample++)
            {
                Assert.Equal(first.DelayTicks(), second.DelayTicks());
            }
        }

        [Fact]
        public void 인바운드_지연은_입력_적용을_늦춘다()
        {
            var delayed = RoomFixture.Create(RoomFixture.Conditions(120, 0, 0.0), roomId: "delayed");
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(delayed);

            // 지연 4틱. 보낸 직후 두 틱 안에는 적용되지 않아야 한다.
            delayed.PostInput(1, 1u, Forward());

            delayed.Advance();
            delayed.Broadcast(transport);
            Assert.True(transport.TryLastSnapshot(1, out var early, out _));
            Assert.Equal(0u, early.AckedInputTick);

            for (var tick = 0; tick < 5; tick++)
            {
                delayed.Advance();
            }

            delayed.Broadcast(transport);
            Assert.True(transport.TryLastSnapshot(1, out var later, out _));
            Assert.Equal(1u, later.AckedInputTick);
        }

        [Fact]
        public void 인바운드_손실은_입력을_사라지게_한다()
        {
            var lossy = RoomFixture.Create(RoomFixture.Conditions(0, 0, 1.0), roomId: "lossy");
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(lossy);

            for (var tick = 1u; tick <= 30u; tick++)
            {
                lossy.PostInput(1, tick, Forward());
                lossy.Advance();
            }

            lossy.Broadcast(transport);
            Assert.True(transport.TryLastSnapshot(1, out var header, out _));

            Assert.Equal(0u, header.AckedInputTick);
        }

        [Fact]
        public void 지연_틱_상한은_시뮬레이션_상수와_맞물린다()
        {
            // 랙 보상 상한(200ms)보다 큰 지연을 넣으면 보상 범위를 벗어난다.
            // 그 경계를 넘겼는지 확인할 수 있어야 한다.
            var simulator = RoomFixture.Conditions(200, 0, 0.0);

            Assert.Equal(6, simulator.BaseDelayTicks);
            Assert.Equal(6, 200 * SimConstants.TickRate / 1000);
        }
    }
}
