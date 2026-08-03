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

        /// 이름이 전부 상한까지 찬 최악의 경우. 송신 버퍼는 이 크기로 잡는다.
        public static int RoomStateMaxWireSize(int maxPlayers)
        {
            return RoomStateHeader.WireSize
                + ((RoomPlayerEntry.FixedWireSize + ProtocolInfo.MaxDisplayNameBytes) * maxPlayers);
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

        public static int WriteRoomState(
            Span<byte> destination,
            RoomStateHeader header,
            ReadOnlySpan<RoomPlayerEntry> players)
        {
            if (players.Length != header.PlayerCount)
            {
                throw new ArgumentException("헤더의 PlayerCount 와 명단 길이가 다르다.", nameof(players));
            }

            var writer = new BitWriter(destination);
            writer.WriteByte((byte)MessageOpcode.Event);
            writer.WriteByte((byte)EventKind.RoomState);
            writer.WriteByte((byte)header.Phase);
            writer.WriteByte(header.HostPlayerId);
            writer.WriteByte(header.SeekerPlayerId);
            writer.WriteByte(header.Outcome);
            writer.WriteUInt32(header.StartTick);
            writer.WriteUInt32(unchecked((uint)header.PlacementSeed));
            writer.WriteByte(header.PlayerCount);

            for (var index = 0; index < players.Length; index++)
            {
                var player = players[index];
                var name = player.Name ?? string.Empty;

                if (name.Length > ProtocolInfo.MaxDisplayNameBytes)
                {
                    throw new ArgumentException(
                        $"표시 이름이 {ProtocolInfo.MaxDisplayNameBytes} 바이트를 넘는다: {name.Length}",
                        nameof(players));
                }

                writer.WriteByte(player.PlayerId);
                writer.WriteByte((byte)name.Length);

                for (var position = 0; position < name.Length; position++)
                {
                    var character = name[position];

                    // 이름은 서버가 ASCII 로 걸러 저장한다. 여기까지 온 비ASCII 는
                    // 그 필터가 빠진 경로가 있다는 뜻이므로 조용히 잘라 보내지 않는다.
                    if (character > 0x7F)
                    {
                        throw new ArgumentException("표시 이름에 ASCII 가 아닌 문자가 있다.", nameof(players));
                    }

                    writer.WriteByte((byte)character);
                }
            }

            return writer.BytesWritten;
        }

        /// 읽은 명단 길이를 반환한다.
        public static int ReadRoomState(
            ReadOnlySpan<byte> source,
            out RoomStateHeader header,
            Span<RoomPlayerEntry> players)
        {
            var reader = new BitReader(source);
            var opcode = (MessageOpcode)reader.ReadByte();
            if (opcode != MessageOpcode.Event)
            {
                throw new InvalidOperationException($"Event 가 아니다: 0x{(byte)opcode:X2}");
            }

            var kind = (EventKind)reader.ReadByte();
            if (kind != EventKind.RoomState)
            {
                throw new InvalidOperationException($"RoomState 가 아니다: {kind}");
            }

            var phase = (RoomPhase)reader.ReadByte();
            var hostPlayerId = reader.ReadByte();
            var seekerPlayerId = reader.ReadByte();
            var outcome = reader.ReadByte();
            var startTick = reader.ReadUInt32();
            var placementSeed = unchecked((int)reader.ReadUInt32());
            var playerCount = reader.ReadByte();

            if (players.Length < playerCount)
            {
                throw new ArgumentException($"명단 {playerCount} 줄을 담을 공간이 없다.", nameof(players));
            }

            Span<char> scratch = stackalloc char[ProtocolInfo.MaxDisplayNameBytes];

            for (var index = 0; index < playerCount; index++)
            {
                var playerId = reader.ReadByte();
                var nameLength = reader.ReadByte();

                if (nameLength > ProtocolInfo.MaxDisplayNameBytes)
                {
                    throw new InvalidOperationException($"표시 이름 길이가 범위를 벗어났다: {nameLength}");
                }

                for (var position = 0; position < nameLength; position++)
                {
                    scratch[position] = (char)reader.ReadByte();
                }

                var name = nameLength == 0
                    ? string.Empty
                    : new string(scratch.Slice(0, nameLength));

                players[index] = new RoomPlayerEntry(playerId, name);
            }

            header = new RoomStateHeader(
                phase,
                hostPlayerId,
                seekerPlayerId,
                outcome,
                startTick,
                placementSeed,
                playerCount);

            return playerCount;
        }

        public static int WriteControl(Span<byte> destination, ControlMessage message)
        {
            var writer = new BitWriter(destination);
            writer.WriteByte((byte)MessageOpcode.Control);
            writer.WriteByte((byte)message.Kind);
            writer.WriteByte(message.Value);
            return writer.BytesWritten;
        }

        /// 신뢰할 수 없는 입력이다. 정의되지 않은 종류는 여기서 거른다 —
        /// 룸이 알 수 없는 요청을 받아 분기 밖으로 떨어지는 경로를 만들지 않는다.
        public static ControlMessage ReadControl(ReadOnlySpan<byte> source)
        {
            var reader = new BitReader(source);
            var opcode = (MessageOpcode)reader.ReadByte();
            if (opcode != MessageOpcode.Control)
            {
                throw new InvalidOperationException($"Control 이 아니다: 0x{(byte)opcode:X2}");
            }

            var kind = (ControlKind)reader.ReadByte();
            var value = reader.ReadByte();

            if (kind != ControlKind.StartMatch
                && kind != ControlKind.EndMatch
                && kind != ControlKind.ReturnToLobby)
            {
                throw new InvalidOperationException($"정의되지 않은 제어 종류다: {(byte)kind}");
            }

            return new ControlMessage(kind, value);
        }
    }
}
