using System;
using System.Numerics;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 술래는 게이지가 있는 동안만 달린다.
    ///
    /// 쫓는 쪽이 늘 빠르면 도망이 성립하지 않고, 한 번도 빠르지 않으면 잡히는 순간이 오지
    /// 않는다. 게이지는 그 사이를 술래의 선택으로 만든다.
    public class SeekerSprintTests
    {
        /// 상수가 요청받은 초와 맞는가. 틱당 정수로 나뉘지 않으면 게이지가 조금씩 어긋난다.
        [Fact]
        public void 게이지가_4초에_비고_10초에_찬다()
        {
            var drainTicks = MatchConstants.SprintChargeFull / MatchConstants.SprintChargeDrain;
            var gainTicks = MatchConstants.SprintChargeFull / MatchConstants.SprintChargeGain;

            Assert.Equal(4f, drainTicks / (float)SimConstants.TickRate);
            Assert.Equal(10f, gainTicks / (float)SimConstants.TickRate);

            // 정수로 나뉘어야 한다 — 나머지가 남으면 끝에서 한 틱이 짧거나 길어진다.
            Assert.Equal(0, MatchConstants.SprintChargeFull % MatchConstants.SprintChargeDrain);
            Assert.Equal(0, MatchConstants.SprintChargeFull % MatchConstants.SprintChargeGain);
        }

        /// 사람이 달리는 속도의 1.6배.
        [Fact]
        public void 술래의_달리기는_사람의_1_6배다()
        {
            var runner = SimConstants.MoveSpeed * SimConstants.SprintMultiplier;
            var seeker = SimConstants.MoveSpeed * SimConstants.SeekerSprintMultiplier;

            Assert.Equal(1.6f, seeker / runner, 4);
        }

        [Fact]
        public void 달리면_게이지가_줄고_멈추면_찬다()
        {
            var world = Sprinting();

            world.Run(ticks: 30);
            var afterRunning = world.Charge();

            Assert.True(afterRunning < MatchConstants.SprintChargeFull, "달렸는데 줄지 않았다.");

            world.Idle(ticks: 30);

            Assert.True(world.Charge() > afterRunning, "멈췄는데 차지 않았다.");
        }

        /// 서 있는 채로 누르고만 있으면 닳지 않는다. 배수는 이동 입력이 있을 때만 의미가 있다.
        [Fact]
        public void 제자리에서_누르고_있어도_닳지_않는다()
        {
            var world = Sprinting();

            world.PressSprintStandingStill(ticks: 30);

            Assert.Equal(MatchConstants.SprintChargeFull, world.Charge());
        }

        /// 게이지가 비면 그냥 못 달린다 — 속도가 사람의 것으로 돌아온다.
        ///
        /// **속도를 재지 거리를 재지 않는다.** 픽스처 맵은 좁아서 계속 달리면 가장자리 밖으로
        /// 떨어지는데, 공중에서는 감속이 느려 달리기를 끊어도 속도가 한동안 유지된다 —
        /// 처음 쓴 버전이 그 낙하를 "게이지가 안 걸린다" 로 읽었다.
        [Fact]
        public void 게이지가_비면_사람의_속도를_넘지_않는다()
        {
            var world = Sprinting();

            world.Empty();
            world.Run(ticks: 10);

            var runnerSprint = SimConstants.MoveSpeed * SimConstants.SprintMultiplier;

            Assert.True(
                world.HorizontalSpeed() <= runnerSprint * 1.02f,
                $"게이지가 비었는데 {world.HorizontalSpeed()}m/s 다. 사람의 달리기는 {runnerSprint}m/s.");
        }

        /// 게이지가 있으면 사람보다 빠르다. 위 검사의 반대쪽 — 둘이 같이 있어야 "게이지가
        /// 걸린다" 와 "애초에 안 빠르다" 를 구별한다.
        [Fact]
        public void 게이지가_있으면_사람보다_빠르다()
        {
            var world = Sprinting();

            world.Run(ticks: 10);

            var runnerSprint = SimConstants.MoveSpeed * SimConstants.SprintMultiplier;

            Assert.True(
                world.HorizontalSpeed() > runnerSprint * 1.2f,
                $"게이지가 가득인데 {world.HorizontalSpeed()}m/s 밖에 안 된다.");
        }

        /// 다 차기를 기다릴 필요가 없다. 조금이라도 있으면 달린다.
        [Fact]
        public void 충전_중에도_달릴_수_있다()
        {
            var world = Sprinting();

            world.Empty();

            // 한 틱만 쉬어도 게이지가 생긴다.
            world.Idle(ticks: 1);
            Assert.True(world.Charge() > 0);

            var before = world.Charge();
            world.Run(ticks: 1);

            Assert.True(world.Charge() < before, "충전 중에는 달릴 수 없었다.");
        }

        /// Runner 사본에서는 게이지가 지워진다. 탄약과 같은 종류의 정보다.
        [Fact]
        public void 게이지는_Runner_사본에서_지워진다()
        {
            var world = Sprinting();

            world.Run(ticks: 30);
            world.Room.Broadcast(world.Transport);

            Assert.True(world.Transport.TryLastMatchState(world.SeekerSession, out _, out var mine));
            Assert.True(world.Transport.TryLastMatchState(world.RunnerSession, out _, out var theirs));

            Assert.True(Find(mine, world.Seeker).SprintCharge > 0, "술래 사본에 게이지가 없다.");
            Assert.Equal(0, Find(theirs, world.Seeker).SprintCharge);
        }

        private static MatchParticipant Find(MatchParticipant[] participants, byte playerId)
        {
            foreach (var participant in participants)
            {
                if (participant.PlayerId == playerId)
                {
                    return participant;
                }
            }

            Assert.Fail($"플레이어 {playerId} 가 전문에 없다.");
            return default;
        }

        private static SprintWorld Sprinting()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room, 2, skipReveal: true);
            room.Broadcast(transport);

            Assert.True(transport.TryLastMatchState(1, out _, out var participants));

            byte seeker = 0;
            byte runner = 0;
            foreach (var participant in participants)
            {
                if (participant.Role == MatchRole.Seeker)
                {
                    seeker = participant.PlayerId;
                }
                else
                {
                    runner = participant.PlayerId;
                }
            }

            return new SprintWorld(room, transport, seeker, runner);
        }

        private sealed class SprintWorld
        {
            private uint _inputTick;

            public SprintWorld(Room room, RecordingTransport transport, byte seeker, byte runner)
            {
                Room = room;
                Transport = transport;
                Seeker = seeker;
                Runner = runner;
            }

            public Room Room { get; }

            public RecordingTransport Transport { get; }

            public byte Seeker { get; }

            public byte Runner { get; }

            public int SeekerSession => Seeker + 1;

            public int RunnerSession => Runner + 1;

            /// 앞으로 달린다. **요를 돌려 벽을 피할 필요가 없다** — 게이지는 실제로 나아간
            /// 거리와 무관하게 입력으로만 닳는다.
            public void Run(int ticks) => Push(ticks, ButtonFlags.Sprint, forward: 127);

            public void Idle(int ticks) => Push(ticks, ButtonFlags.None, forward: 0);

            public void PressSprintStandingStill(int ticks) => Push(ticks, ButtonFlags.Sprint, forward: 0);

            private void Push(int ticks, ButtonFlags buttons, sbyte forward)
            {
                for (var tick = 0; tick < ticks; tick++)
                {
                    _inputTick++;
                    Room.PostInput(SeekerSession, _inputTick, new InputFrame(buttons, 0, forward, 0, 0));
                    Room.Advance();
                }
            }

            /// 게이지를 직접 비운다. 200틱을 달려 비우면 그 사이에 맵 밖으로 나간다.
            public void Empty()
            {
                foreach (var player in Room.Players)
                {
                    if (player.PlayerId == Seeker)
                    {
                        player.SprintCharge = 0;
                        return;
                    }
                }

                Assert.Fail("술래가 명단에 없다.");
            }

            public float HorizontalSpeed()
            {
                foreach (var player in Room.Players)
                {
                    if (player.PlayerId == Seeker)
                    {
                        var v = player.State.Velocity;
                        return MathF.Sqrt((v.X * v.X) + (v.Z * v.Z));
                    }
                }

                Assert.Fail("술래가 명단에 없다.");
                return 0f;
            }

            public int Charge()
            {
                foreach (var player in Room.Players)
                {
                    if (player.PlayerId == Seeker)
                    {
                        return player.SprintCharge;
                    }
                }

                Assert.Fail("술래가 명단에 없다.");
                return 0;
            }

            public Vector3 Position()
            {
                foreach (var player in Room.Players)
                {
                    if (player.PlayerId == Seeker)
                    {
                        return player.State.Position;
                    }
                }

                Assert.Fail("술래가 명단에 없다.");
                return default;
            }
        }
    }
}
