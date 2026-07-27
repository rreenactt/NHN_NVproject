using System;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;

namespace NV.Shared.Serialization
{
    /// 게임 메시지의 와이어 변환. opcode 를 포함해 읽고 쓴다.
    /// 스냅샷은 델타 압축하지 않는다. TCP head-of-line blocking 때문에
    /// 이전 스냅샷 도착을 전제한 인코딩을 쓸 수 없다.
    public static class MessageCodec
    {
        public static int SnapshotWireSize(int entityCount)
        {
            return SnapshotHeader.WireSize + (EntityState.WireSize * entityCount);
        }

        public static int InputWireSize(int frameCount)
        {
            // opcode(1) + tick(4) + frameCount(1)
            return 6 + (InputFrame.WireSize * frameCount);
        }

        public static MessageOpcode ReadOpcode(ReadOnlySpan<byte> source)
        {
            if (source.Length < 1)
            {
                return MessageOpcode.None;
            }

            return (MessageOpcode)source[0];
        }

        public static int WriteWelcome(Span<byte> destination, WelcomeMessage message)
        {
            var writer = new BitWriter(destination);
            writer.WriteByte((byte)MessageOpcode.Welcome);
            writer.WriteUInt16(message.ProtocolVersion);
            writer.WriteByte(message.PlayerId);
            writer.WriteUInt32(message.ServerTick);
            writer.WriteUInt32(message.MapHash);
            writer.WriteByte(message.TickRate);
            return writer.BytesWritten;
        }

        public static WelcomeMessage ReadWelcome(ReadOnlySpan<byte> source)
        {
            var reader = new BitReader(source);
            var opcode = (MessageOpcode)reader.ReadByte();
            if (opcode != MessageOpcode.Welcome)
            {
                throw new InvalidOperationException($"Welcome 이 아니다: 0x{(byte)opcode:X2}");
            }

            return new WelcomeMessage(
                reader.ReadUInt16(),
                reader.ReadByte(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadByte());
        }

        public static int WriteSnapshot(
            Span<byte> destination,
            SnapshotHeader header,
            ReadOnlySpan<EntityState> entities)
        {
            if (entities.Length != header.EntityCount)
            {
                throw new ArgumentException("헤더의 EntityCount 와 엔티티 수가 다르다.", nameof(entities));
            }

            var writer = new BitWriter(destination);
            writer.WriteByte((byte)MessageOpcode.Snapshot);
            writer.WriteUInt32(header.Tick);
            writer.WriteUInt32(header.AckedInputTick);
            writer.WriteByte(header.EntityCount);

            for (var index = 0; index < entities.Length; index++)
            {
                var entity = entities[index];
                writer.WriteByte(entity.Id);
                writer.WriteInt16(entity.X);
                writer.WriteInt16(entity.Y);
                writer.WriteInt16(entity.Z);
                writer.WriteUInt16(entity.Yaw);
                writer.WriteInt16(entity.Pitch);
                writer.WriteByte((byte)entity.Flags);
                writer.WriteByte(entity.Health);
            }

            return writer.BytesWritten;
        }

        /// 읽은 엔티티 수를 반환한다. destination 이 짧으면 예외를 던진다.
        public static int ReadSnapshot(
            ReadOnlySpan<byte> source,
            out SnapshotHeader header,
            Span<EntityState> entities)
        {
            var reader = new BitReader(source);
            var opcode = (MessageOpcode)reader.ReadByte();
            if (opcode != MessageOpcode.Snapshot)
            {
                throw new InvalidOperationException($"Snapshot 이 아니다: 0x{(byte)opcode:X2}");
            }

            var tick = reader.ReadUInt32();
            var ackedInputTick = reader.ReadUInt32();
            var entityCount = reader.ReadByte();

            if (entities.Length < entityCount)
            {
                throw new ArgumentException($"엔티티 {entityCount} 개를 담을 공간이 없다.", nameof(entities));
            }

            for (var index = 0; index < entityCount; index++)
            {
                entities[index] = new EntityState(
                    reader.ReadByte(),
                    reader.ReadInt16(),
                    reader.ReadInt16(),
                    reader.ReadInt16(),
                    reader.ReadUInt16(),
                    reader.ReadInt16(),
                    (EntityFlags)reader.ReadByte(),
                    reader.ReadByte());
            }

            header = new SnapshotHeader(tick, ackedInputTick, entityCount);
            return entityCount;
        }

        /// tick 은 frames[0] 의 틱이고 이후 프레임은 하나씩 과거다.
        public static int WriteInput(Span<byte> destination, uint tick, ReadOnlySpan<InputFrame> frames)
        {
            if (frames.Length < 1 || frames.Length > ProtocolInfo.MaxInputFramesPerMessage)
            {
                throw new ArgumentException(
                    $"입력 프레임은 1..{ProtocolInfo.MaxInputFramesPerMessage} 개여야 한다.",
                    nameof(frames));
            }

            var writer = new BitWriter(destination);
            writer.WriteByte((byte)MessageOpcode.Input);
            writer.WriteUInt32(tick);
            writer.WriteByte((byte)frames.Length);

            for (var index = 0; index < frames.Length; index++)
            {
                var frame = frames[index];
                writer.WriteByte((byte)frame.Buttons);
                writer.WriteSByte(frame.MoveX);
                writer.WriteSByte(frame.MoveZ);
                writer.WriteUInt16(frame.Yaw);
                writer.WriteInt16(frame.Pitch);
            }

            return writer.BytesWritten;
        }

        /// 읽은 프레임 수를 반환한다.
        /// 신뢰할 수 없는 입력이므로 프레임 수 상한을 여기서 강제한다.
        public static int ReadInput(ReadOnlySpan<byte> source, out uint tick, Span<InputFrame> frames)
        {
            var reader = new BitReader(source);
            var opcode = (MessageOpcode)reader.ReadByte();
            if (opcode != MessageOpcode.Input)
            {
                throw new InvalidOperationException($"Input 이 아니다: 0x{(byte)opcode:X2}");
            }

            tick = reader.ReadUInt32();
            var frameCount = reader.ReadByte();

            if (frameCount < 1 || frameCount > ProtocolInfo.MaxInputFramesPerMessage)
            {
                throw new InvalidOperationException($"입력 프레임 수가 범위를 벗어났다: {frameCount}");
            }

            if (frames.Length < frameCount)
            {
                throw new ArgumentException($"프레임 {frameCount} 개를 담을 공간이 없다.", nameof(frames));
            }

            for (var index = 0; index < frameCount; index++)
            {
                frames[index] = new InputFrame(
                    (ButtonFlags)reader.ReadByte(),
                    reader.ReadSByte(),
                    reader.ReadSByte(),
                    reader.ReadUInt16(),
                    reader.ReadInt16());
            }

            return frameCount;
        }
    }
}
