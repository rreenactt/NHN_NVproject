using System;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using Xunit;

namespace NV.Modules.Tests.Serialization
{
    public class CodecRoundTripTests
    {
        [Fact]
        public void Welcome는_라운드트립한다()
        {
            var original = new WelcomeMessage(ProtocolInfo.Version, 7, 123456u, 0xDEADBEEF, 30);
            var buffer = new byte[WelcomeMessage.WireSize];

            var written = MessageCodec.WriteWelcome(buffer, original);
            var decoded = MessageCodec.ReadWelcome(buffer);

            Assert.Equal(WelcomeMessage.WireSize, written);
            Assert.Equal(original.ProtocolVersion, decoded.ProtocolVersion);
            Assert.Equal(original.PlayerId, decoded.PlayerId);
            Assert.Equal(original.ServerTick, decoded.ServerTick);
            Assert.Equal(original.MapHash, decoded.MapHash);
            Assert.Equal(original.TickRate, decoded.TickRate);
        }

        [Fact]
        public void 스냅샷은_라운드트립한다()
        {
            var entities = new EntityState[8];
            for (var index = 0; index < entities.Length; index++)
            {
                entities[index] = new EntityState(
                    (byte)index,
                    (short)(index * -1000),
                    (short)(index * 37),
                    short.MaxValue,
                    (ushort)(index * 8000),
                    short.MinValue,
                    EntityFlags.Alive | EntityFlags.OnGround,
                    (byte)(100 - index));
            }

            var header = new SnapshotHeader(4_000_000_000u, 3_999_999_990u, (byte)entities.Length);
            var buffer = new byte[MessageCodec.SnapshotWireSize(entities.Length)];

            var written = MessageCodec.WriteSnapshot(buffer, header, entities);

            var decodedEntities = new EntityState[8];
            var count = MessageCodec.ReadSnapshot(buffer, out var decodedHeader, decodedEntities);

            Assert.Equal(114, written);
            Assert.Equal(entities.Length, count);
            Assert.Equal(header.Tick, decodedHeader.Tick);
            Assert.Equal(header.AckedInputTick, decodedHeader.AckedInputTick);
            Assert.Equal(header.EntityCount, decodedHeader.EntityCount);

            for (var index = 0; index < entities.Length; index++)
            {
                Assert.Equal(entities[index].Id, decodedEntities[index].Id);
                Assert.Equal(entities[index].X, decodedEntities[index].X);
                Assert.Equal(entities[index].Y, decodedEntities[index].Y);
                Assert.Equal(entities[index].Z, decodedEntities[index].Z);
                Assert.Equal(entities[index].Yaw, decodedEntities[index].Yaw);
                Assert.Equal(entities[index].Pitch, decodedEntities[index].Pitch);
                Assert.Equal(entities[index].Flags, decodedEntities[index].Flags);
                Assert.Equal(entities[index].Health, decodedEntities[index].Health);
            }
        }

        [Fact]
        public void 입력은_라운드트립한다()
        {
            var frames = new[]
            {
                new InputFrame(ButtonFlags.Jump | ButtonFlags.Fire, 127, -127, 65535, short.MinValue),
                new InputFrame(ButtonFlags.None, 0, 0, 0, 0),
                new InputFrame(ButtonFlags.Crouch, -1, 1, 32768, short.MaxValue),
            };

            var buffer = new byte[MessageCodec.InputWireSize(frames.Length)];
            var written = MessageCodec.WriteInput(buffer, 999u, frames);

            var decodedFrames = new InputFrame[ProtocolInfo.MaxInputFramesPerMessage];
            var count = MessageCodec.ReadInput(buffer, out var tick, decodedFrames);

            Assert.Equal(27, written);
            Assert.Equal(999u, tick);
            Assert.Equal(frames.Length, count);

            for (var index = 0; index < frames.Length; index++)
            {
                Assert.Equal(frames[index].Buttons, decodedFrames[index].Buttons);
                Assert.Equal(frames[index].MoveX, decodedFrames[index].MoveX);
                Assert.Equal(frames[index].MoveZ, decodedFrames[index].MoveZ);
                Assert.Equal(frames[index].Yaw, decodedFrames[index].Yaw);
                Assert.Equal(frames[index].Pitch, decodedFrames[index].Pitch);
            }
        }

        [Fact]
        public void opcode가_다르면_읽기를_거부한다()
        {
            var buffer = new byte[MessageCodec.SnapshotWireSize(0)];
            MessageCodec.WriteSnapshot(buffer, new SnapshotHeader(1u, 0u, 0), Array.Empty<EntityState>());

            Assert.Throws<InvalidOperationException>(() =>
            {
                var frames = new InputFrame[ProtocolInfo.MaxInputFramesPerMessage];
                MessageCodec.ReadInput(buffer, out _, frames);
            });
        }

        [Fact]
        public void 프레임_수_상한을_넘는_입력은_거부한다()
        {
            // 손상되거나 조작된 프레임 수를 그대로 신뢰하면 버퍼를 넘겨 읽는다.
            var buffer = new byte[MessageCodec.InputWireSize(ProtocolInfo.MaxInputFramesPerMessage)];
            MessageCodec.WriteInput(
                buffer,
                1u,
                new[] { new InputFrame(ButtonFlags.None, 0, 0, 0, 0) });

            buffer[5] = 200;

            Assert.Throws<InvalidOperationException>(() =>
            {
                var frames = new InputFrame[ProtocolInfo.MaxInputFramesPerMessage];
                MessageCodec.ReadInput(buffer, out _, frames);
            });
        }

        [Fact]
        public void 룸_상태는_라운드트립한다()
        {
            // 이름 없음·짧은 이름·상한까지 찬 이름을 한 전문에 섞는다.
            var players = new[]
            {
                new RoomPlayerEntry(0, "host"),
                new RoomPlayerEntry(3, string.Empty),
                new RoomPlayerEntry(7, new string('W', ProtocolInfo.MaxDisplayNameBytes)),
            };

            var header = new RoomStateHeader(
                RoomPhase.Playing,
                hostPlayerId: 0,
                seekerPlayerId: 7,
                outcome: 0,
                startTick: 4_000_000_000u,
                playerCount: (byte)players.Length);

            var buffer = new byte[MessageCodec.RoomStateMaxWireSize(8)];
            var written = MessageCodec.WriteRoomState(buffer, header, players);

            var decodedPlayers = new RoomPlayerEntry[8];
            var count = MessageCodec.ReadRoomState(buffer, out var decodedHeader, decodedPlayers);

            // 고정부 + (2+4) + (2+0) + (2+12) = 37B
            Assert.Equal(RoomStateHeader.WireSize + 22, written);
            Assert.Equal(players.Length, count);
            Assert.Equal(header.Phase, decodedHeader.Phase);
            Assert.Equal(header.HostPlayerId, decodedHeader.HostPlayerId);
            Assert.Equal(header.SeekerPlayerId, decodedHeader.SeekerPlayerId);
            Assert.Equal(header.StartTick, decodedHeader.StartTick);

            // **배치 씨드가 실리지 않는다.** 고정부 11B 는 opcode·kind·phase·host·seeker·
            // outcome·startTick(4)·count 이고, 씨드 4바이트가 들어갈 자리가 없다. 그 필드가
            // 있었을 때는 Seeker 가 문의 좌표를 계산할 수 있었다.
            Assert.Equal(11, RoomStateHeader.WireSize);

            for (var index = 0; index < players.Length; index++)
            {
                Assert.Equal(players[index].PlayerId, decodedPlayers[index].PlayerId);
                Assert.Equal(players[index].Name, decodedPlayers[index].Name);
            }
        }

        [Fact]
        public void 빈_룸_상태도_라운드트립한다()
        {
            var header = new RoomStateHeader(
                RoomPhase.Waiting,
                RoomStateHeader.NoPlayer,
                RoomStateHeader.NoPlayer,
                outcome: 0,
                startTick: 0u,
                playerCount: 0);

            var buffer = new byte[MessageCodec.RoomStateMaxWireSize(8)];
            var written = MessageCodec.WriteRoomState(buffer, header, Array.Empty<RoomPlayerEntry>());
            var count = MessageCodec.ReadRoomState(buffer, out var decoded, new RoomPlayerEntry[8]);

            Assert.Equal(RoomStateHeader.WireSize, written);
            Assert.Equal(0, count);
            Assert.Equal(RoomPhase.Waiting, decoded.Phase);
            Assert.Equal(RoomStateHeader.NoPlayer, decoded.HostPlayerId);
        }

        [Fact]
        public void 이름_길이_상한을_넘는_룸상태는_거부한다()
        {
            // 손상된 길이를 그대로 신뢰하면 버퍼를 넘겨 읽는다.
            var buffer = new byte[MessageCodec.RoomStateMaxWireSize(8)];
            MessageCodec.WriteRoomState(
                buffer,
                new RoomStateHeader(RoomPhase.Waiting, 0, RoomStateHeader.NoPlayer, 0, 0u, 1),
                new[] { new RoomPlayerEntry(0, "ab") });

            // 고정부 다음이 playerId, 그 다음이 nameLength 다.
            buffer[RoomStateHeader.WireSize + 1] = 200;

            Assert.Throws<InvalidOperationException>(
                () => MessageCodec.ReadRoomState(buffer, out _, new RoomPlayerEntry[8]));
        }

        [Fact]
        public void 제어는_라운드트립한다()
        {
            var buffer = new byte[ControlMessage.WireSize];
            var written = MessageCodec.WriteControl(buffer, new ControlMessage(ControlKind.EndMatch, 3));

            var decoded = MessageCodec.ReadControl(buffer);

            Assert.Equal(ControlMessage.WireSize, written);
            Assert.Equal(ControlKind.EndMatch, decoded.Kind);
            Assert.Equal(3, decoded.Value);
        }

        [Fact]
        public void 정의되지_않은_제어는_거부한다()
        {
            var buffer = new byte[ControlMessage.WireSize];
            MessageCodec.WriteControl(buffer, new ControlMessage(ControlKind.StartMatch, 0));
            buffer[1] = 99;

            Assert.Throws<InvalidOperationException>(() => MessageCodec.ReadControl(buffer));
        }

        [Fact]
        public void 바이트정렬된_16비트_기록은_리틀엔디언이다()
        {
            var buffer = new byte[2];
            var writer = new BitWriter(buffer);
            writer.WriteUInt16(0x1234);

            Assert.Equal(0x34, buffer[0]);
            Assert.Equal(0x12, buffer[1]);
        }

        [Fact]
        public void 비트_단위_기록과_판독이_대칭이다()
        {
            var buffer = new byte[8];
            var writer = new BitWriter(buffer);
            writer.WriteBits(0b101u, 3);
            writer.WriteBits(0u, 1);
            writer.WriteBits(0xFFFFu, 16);
            writer.WriteBool(true);

            Assert.Equal(21, writer.BitPosition);
            Assert.Equal(3, writer.BytesWritten);

            var reader = new BitReader(buffer);
            Assert.Equal(0b101u, reader.ReadBits(3));
            Assert.Equal(0u, reader.ReadBits(1));
            Assert.Equal(0xFFFFu, reader.ReadBits(16));
            Assert.True(reader.ReadBool());
        }
    }
}
