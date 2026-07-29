using System;
using NV.Realtime;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    public class RoomTests
    {
        private static InputFrame Forward()
        {
            return new InputFrame(ButtonFlags.None, 0, 127, 0, 0);
        }

        private static void Run(Room room, RecordingTransport transport, int ticks)
        {
            for (var tick = 0; tick < ticks; tick++)
            {
                room.Advance();
                room.Broadcast(transport);
            }
        }

        [Fact]
        public void 입장하면_스폰_위치에서_시작한다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            room.PostCommand(RoomCommand.Join(1, 0));
            Run(room, transport, 1);

            Assert.True(transport.TryLastSnapshot(1, out var header, out var entities));
            Assert.Single(entities);
            Assert.Equal(0, entities[0].Id);
            Assert.Equal(1u, header.Tick);

            // 스폰이 원점이고 바닥 위다.
            Assert.Equal(0, entities[0].X);
            Assert.Equal(0, entities[0].Z);
        }

        [Fact]
        public void 정원을_넘는_입장은_무시된다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            for (var sessionId = 1; sessionId <= RealtimeConstants.Rooms.MaxPlayers + 3; sessionId++)
            {
                room.PostCommand(RoomCommand.Join(sessionId, (byte)((sessionId - 1) % RealtimeConstants.Rooms.MaxPlayers)));
            }

            Run(room, transport, 1);

            Assert.True(transport.TryLastSnapshot(1, out _, out var entities));
            Assert.Equal(RealtimeConstants.Rooms.MaxPlayers, entities.Length);
        }

        [Fact]
        public void 퇴장하면_스냅샷에서_사라진다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            room.PostCommand(RoomCommand.Join(1, 0));
            room.PostCommand(RoomCommand.Join(2, 1));
            Run(room, transport, 1);

            Assert.True(transport.TryLastSnapshot(1, out _, out var before));
            Assert.Equal(2, before.Length);

            room.PostCommand(RoomCommand.Leave(2, 1));
            Run(room, transport, 1);

            Assert.True(transport.TryLastSnapshot(1, out _, out var after));
            Assert.Single(after);
        }

        [Fact]
        public void 아무도_없으면_스냅샷을_보내지_않는다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            Run(room, transport, 10);

            Assert.Equal(0, transport.TotalSent);
            Assert.Equal(10u, room.Tick);
        }

        [Fact]
        public void 전진_입력은_서버_판정으로_위치를_옮긴다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            room.PostCommand(RoomCommand.Join(1, 0));
            room.Advance();

            for (var tick = 1u; tick <= 30u; tick++)
            {
                room.PostInput(1, tick, Forward());
                room.Advance();
                room.Broadcast(transport);
            }

            Assert.True(transport.TryLastSnapshot(1, out var header, out var entities));

            var z = Quantization.ToMeters(entities[0].Z);
            Assert.True(z > 1f, $"Z = {z}");
            Assert.True(header.AckedInputTick > 0u, $"acked = {header.AckedInputTick}");
        }

        [Fact]
        public void 클라이언트는_위치를_보낼_수_없다()
        {
            // 입력 프레임에 위치 필드가 없다는 것을 구조로 확인한다.
            // 필드가 추가되면 이 테스트가 컴파일되지 않는다.
            var fields = typeof(InputFrame).GetProperties();

            foreach (var field in fields)
            {
                Assert.DoesNotContain("Position", field.Name, StringComparison.Ordinal);
                Assert.DoesNotContain("Velocity", field.Name, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void 한_틱에_적용하는_입력_수에_상한이_있다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            room.PostCommand(RoomCommand.Join(1, 0));
            room.Advance();

            // 한 번에 10틱치를 몰아 보낸다.
            for (var tick = 1u; tick <= 10u; tick++)
            {
                room.PostInput(1, tick, Forward());
            }

            room.Advance();
            room.Broadcast(transport);

            Assert.True(transport.TryLastSnapshot(1, out var header, out _));
            Assert.True(
                header.AckedInputTick <= RealtimeConstants.Rooms.MaxInputsPerTick,
                $"한 틱에 {header.AckedInputTick} 개를 적용했다.");
        }

        [Fact]
        public void 입력이_끊기면_반복_상한_뒤에_멈춘다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            room.PostCommand(RoomCommand.Join(1, 0));
            room.Advance();

            for (var tick = 1u; tick <= 40u; tick++)
            {
                room.PostInput(1, tick, Forward());
                room.Advance();
            }

            room.Broadcast(transport);
            Assert.True(transport.TryLastSnapshot(1, out _, out var moving));
            var movingZ = Quantization.ToMeters(moving[0].Z);

            // 입력을 끊는다. 반복 상한을 지나면 이동이 멈춰야 한다.
            for (var tick = 0; tick < 40; tick++)
            {
                room.Advance();
            }

            room.Broadcast(transport);
            Assert.True(transport.TryLastSnapshot(1, out _, out var stopped));
            var stoppedZ = Quantization.ToMeters(stopped[0].Z);

            var drift = stoppedZ - movingZ;

            // 반복 상한만큼만 더 가고 멈춘다. 40틱을 계속 달렸다면 8m 이상 갔을 것이다.
            Assert.True(drift < 2f, $"입력 없이 {drift}m 이동했다.");
        }

        [Fact]
        public void 벽을_넘어가지_못한다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            room.PostCommand(RoomCommand.Join(1, 0));
            room.Advance();

            // 요 90도로 벽(X = 5)을 향해 계속 전진한다.
            var toWall = new InputFrame(ButtonFlags.None, 0, 127, Quantization.ToFixedYaw(1.5707963f), 0);

            for (var tick = 1u; tick <= 120u; tick++)
            {
                room.PostInput(1, tick, toWall);
                room.Advance();
            }

            room.Broadcast(transport);
            Assert.True(transport.TryLastSnapshot(1, out _, out var entities));

            var x = Quantization.ToMeters(entities[0].X);
            Assert.True(x < 5f, $"벽을 통과했다. X = {x}");
            Assert.True(x > 3.5f, $"벽까지 도달하지 못했다. X = {x}");
        }

        [Fact]
        public void 세션마다_다른_ack_틱을_받는다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            room.PostCommand(RoomCommand.Join(1, 0));
            room.PostCommand(RoomCommand.Join(2, 1));
            room.Advance();

            // 1번만 입력을 보낸다.
            for (var tick = 1u; tick <= 5u; tick++)
            {
                room.PostInput(1, tick, Forward());
                room.Advance();
                room.Broadcast(transport);
            }

            Assert.True(transport.TryLastSnapshot(1, out var first, out _));
            Assert.True(transport.TryLastSnapshot(2, out var second, out _));

            Assert.True(first.AckedInputTick > 0u);
            Assert.Equal(0u, second.AckedInputTick);

            // 본문은 같아야 한다. 둘 다 두 엔티티를 본다.
            Assert.Equal(2, first.EntityCount);
            Assert.Equal(2, second.EntityCount);
        }
    }
}
