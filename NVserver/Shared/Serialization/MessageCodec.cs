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
                playerCount);

            return playerCount;
        }

        public static int MatchStateMaxWireSize(int maxPlayers)
        {
            return MatchStateHeader.WireSize + (maxPlayers * MatchParticipant.WireSize);
        }

        /// `Event` 프레임의 종류. 본문을 파싱하기 전에 어느 전문인지 가른다.
        ///
        /// 이것 없이 `Event` 를 무조건 한 종류로 파싱하면, 새 전문이 추가된 순간
        /// 기존 클라이언트가 매 프레임 파싱 예외를 던진다.
        public static EventKind ReadEventKind(ReadOnlySpan<byte> source)
        {
            if (source.Length < 2 || (MessageOpcode)source[0] != MessageOpcode.Event)
            {
                return EventKind.None;
            }

            return (EventKind)source[1];
        }

        /// 매치 상태 전문을 **수신자의 역할에 맞게** 쓴다.
        ///
        /// `forRole` 이 이 함수의 요점이다. 룰셋은 Seeker 에게 열쇠 진행도를 알리지
        /// 않으므로, 그 사본에서는 삽입된 열쇠 수와 모든 참가자의 소지 열쇠를 0 으로
        /// 채운다. **필터를 인코딩 지점에 두는 이유는 우회할 자리를 없애기 위해서다** —
        /// 호출부가 필터를 잊는 경로가 있으면 그 경로로 좌표가 새고, 클라이언트에서
        /// 숨기는 방식은 디컴파일로 되살아난다.
        ///
        /// 탈출 수는 걸러내지 않는다. Seeker 가 막아야 하는 수이므로 알아야 한다.
        public static int WriteMatchState(
            Span<byte> destination,
            MatchStateHeader header,
            ReadOnlySpan<MatchParticipant> participants,
            MatchRole forRole)
        {
            if (participants.Length != header.ParticipantCount)
            {
                throw new ArgumentException(
                    "헤더의 ParticipantCount 와 참가자 수가 다르다.",
                    nameof(participants));
            }

            var hideKeys = forRole == MatchRole.Seeker;

            var writer = new BitWriter(destination);
            writer.WriteByte((byte)MessageOpcode.Event);
            writer.WriteByte((byte)EventKind.MatchState);
            writer.WriteByte((byte)header.Phase);
            writer.WriteUInt16(header.TimeRemainingTenths);
            writer.WriteByte(hideKeys ? (byte)0 : header.KeysInserted);
            writer.WriteByte(header.Escapes);
            writer.WriteByte(header.Outcome);
            writer.WriteByte(header.ParticipantCount);

            for (var index = 0; index < participants.Length; index++)
            {
                var participant = participants[index];

                writer.WriteByte(participant.PlayerId);
                writer.WriteByte((byte)participant.Role);
                writer.WriteByte(participant.Flags);
                writer.WriteByte(participant.Hits);
                writer.WriteByte(hideKeys ? (byte)0 : participant.CarriedKeys);
            }

            return writer.BytesWritten;
        }

        /// 읽은 참가자 수를 반환한다.
        public static int ReadMatchState(
            ReadOnlySpan<byte> source,
            out MatchStateHeader header,
            Span<MatchParticipant> participants)
        {
            var reader = new BitReader(source);
            var opcode = (MessageOpcode)reader.ReadByte();
            if (opcode != MessageOpcode.Event)
            {
                throw new InvalidOperationException($"Event 가 아니다: 0x{(byte)opcode:X2}");
            }

            var kind = (EventKind)reader.ReadByte();
            if (kind != EventKind.MatchState)
            {
                throw new InvalidOperationException($"MatchState 가 아니다: {kind}");
            }

            var phase = (MatchPhase)reader.ReadByte();
            var timeRemaining = reader.ReadUInt16();
            var keysInserted = reader.ReadByte();
            var escapes = reader.ReadByte();
            var outcome = reader.ReadByte();
            var participantCount = reader.ReadByte();

            if (participants.Length < participantCount)
            {
                throw new ArgumentException(
                    $"참가자 {participantCount} 명을 담을 공간이 없다.",
                    nameof(participants));
            }

            for (var index = 0; index < participantCount; index++)
            {
                participants[index] = new MatchParticipant(
                    reader.ReadByte(),
                    (MatchRole)reader.ReadByte(),
                    reader.ReadByte(),
                    reader.ReadByte(),
                    reader.ReadByte());
            }

            header = new MatchStateHeader(
                phase,
                timeRemaining,
                keysInserted,
                escapes,
                outcome,
                participantCount);

            return participantCount;
        }

        /// 목표물 전문의 최대 크기. 문·제단이 다 실리고 열쇠·장치가 상한까지 있는 경우다.
        public static int ObjectiveStateMaxWireSize(int maxKeys, int maxDevices)
        {
            return ObjectiveStateHeader.WireSize
                + (ObjectivePoint.WireSize * 2)          // 제단 위치 + 착지점
                + ObjectivePoint.WireSize + 2 + 1        // 문 위치 + yaw + 개방 여부
                + (maxKeys * ObjectivePoint.WireSize)
                + (maxDevices * ObjectiveDevice.WireSize);
        }

        /// 목표물 전문을 **수신자의 역할에 맞게** 쓴다.
        ///
        /// `forRole` 이 Seeker 면 **문 블록을 아예 쓰지 않고** `HasDoor` 를 내린다. 좌표를
        /// 0 으로 채우는 것과 다르다 — 그것도 "문이 있다" 는 사실과 블록 크기를 알려 준다.
        /// 없는 블록은 복원할 방법이 없다.
        ///
        /// 필터를 인코딩 지점에 두는 이유는 `WriteMatchState` 와 같다. 호출부가 필터를 잊는
        /// 경로가 있으면 그 경로로 좌표가 새고, 클라이언트에서 숨기는 방식은 디컴파일로
        /// 되살아난다.
        ///
        /// 열쇠·제단·장치는 걸러내지 않는다. 룰셋상 Seeker 도 봐야 하는 것들이다 — 열쇠를
        /// 지키는 전술, 벌칙 지점, §5.3 의 파괴 대상.
        public static int WriteObjectiveState(
            Span<byte> destination,
            ObjectiveStateHeader header,
            ObjectivePoint altarPosition,
            ObjectivePoint altarDragPoint,
            ObjectivePoint doorPosition,
            ushort doorYaw,
            bool doorOpen,
            ReadOnlySpan<ObjectivePoint> keys,
            ReadOnlySpan<ObjectiveDevice> devices,
            MatchRole forRole)
        {
            if (keys.Length != header.KeyCount)
            {
                throw new ArgumentException("헤더의 KeyCount 와 열쇠 수가 다르다.", nameof(keys));
            }

            if (devices.Length != header.DeviceCount)
            {
                throw new ArgumentException("헤더의 DeviceCount 와 장치 수가 다르다.", nameof(devices));
            }

            // Seeker 에게는 문이 없다. 헤더의 비트도 함께 내려야 수신 측이 블록을 찾지 않는다.
            var flags = header.Flags;
            if (forRole == MatchRole.Seeker)
            {
                flags &= ~ObjectiveFlags.HasDoor;
            }

            var writer = new BitWriter(destination);
            writer.WriteByte((byte)MessageOpcode.Event);
            writer.WriteByte((byte)EventKind.ObjectiveState);
            writer.WriteByte((byte)flags);
            writer.WriteByte(header.KeyCount);
            writer.WriteByte(header.DeviceCount);

            if ((flags & ObjectiveFlags.HasAltar) != 0)
            {
                WritePoint(ref writer, altarPosition);
                WritePoint(ref writer, altarDragPoint);
            }

            if ((flags & ObjectiveFlags.HasDoor) != 0)
            {
                WritePoint(ref writer, doorPosition);
                writer.WriteUInt16(doorYaw);
                writer.WriteByte(doorOpen ? (byte)1 : (byte)0);
            }

            for (var index = 0; index < keys.Length; index++)
            {
                WritePoint(ref writer, keys[index]);
            }

            for (var index = 0; index < devices.Length; index++)
            {
                var device = devices[index];

                writer.WriteInt16(device.X);
                writer.WriteInt16(device.Y);
                writer.WriteInt16(device.Z);
                writer.WriteUInt16(device.Yaw);
                writer.WriteByte((byte)device.Type);
                writer.WriteByte(device.State);
            }

            return writer.BytesWritten;
        }

        /// 읽은 열쇠 수를 반환한다. 장치 수는 헤더에 있다.
        public static int ReadObjectiveState(
            ReadOnlySpan<byte> source,
            out ObjectiveStateHeader header,
            out ObjectivePoint altarPosition,
            out ObjectivePoint altarDragPoint,
            out ObjectivePoint doorPosition,
            out ushort doorYaw,
            out bool doorOpen,
            Span<ObjectivePoint> keys,
            Span<ObjectiveDevice> devices)
        {
            altarPosition = default;
            altarDragPoint = default;
            doorPosition = default;
            doorYaw = 0;
            doorOpen = false;

            var reader = new BitReader(source);
            var opcode = (MessageOpcode)reader.ReadByte();
            if (opcode != MessageOpcode.Event)
            {
                throw new InvalidOperationException($"Event 가 아니다: 0x{(byte)opcode:X2}");
            }

            var kind = (EventKind)reader.ReadByte();
            if (kind != EventKind.ObjectiveState)
            {
                throw new InvalidOperationException($"ObjectiveState 가 아니다: {kind}");
            }

            var flags = (ObjectiveFlags)reader.ReadByte();
            var keyCount = reader.ReadByte();
            var deviceCount = reader.ReadByte();

            if (keys.Length < keyCount)
            {
                throw new ArgumentException($"열쇠 {keyCount} 개를 담을 공간이 없다.", nameof(keys));
            }

            if (devices.Length < deviceCount)
            {
                throw new ArgumentException($"장치 {deviceCount} 개를 담을 공간이 없다.", nameof(devices));
            }

            if ((flags & ObjectiveFlags.HasAltar) != 0)
            {
                altarPosition = ReadPoint(ref reader);
                altarDragPoint = ReadPoint(ref reader);
            }

            if ((flags & ObjectiveFlags.HasDoor) != 0)
            {
                doorPosition = ReadPoint(ref reader);
                doorYaw = reader.ReadUInt16();
                doorOpen = reader.ReadByte() != 0;
            }

            for (var index = 0; index < keyCount; index++)
            {
                keys[index] = ReadPoint(ref reader);
            }

            for (var index = 0; index < deviceCount; index++)
            {
                // 와이어 순서대로 읽어 한 번에 만든다. 인자 위치로 읽으면 C# 이 평가 순서를
                // 보장하더라도 읽는 순서가 코드에서 보이지 않아, 필드를 추가할 때 어긋난다.
                var x = reader.ReadInt16();
                var y = reader.ReadInt16();
                var z = reader.ReadInt16();
                var yaw = reader.ReadUInt16();
                var type = (MatchDeviceType)reader.ReadByte();
                var state = reader.ReadByte();

                devices[index] = new ObjectiveDevice(type, x, y, z, yaw, state);
            }

            header = new ObjectiveStateHeader(flags, keyCount, deviceCount);
            return keyCount;
        }

        private static void WritePoint(ref BitWriter writer, ObjectivePoint point)
        {
            writer.WriteInt16(point.X);
            writer.WriteInt16(point.Y);
            writer.WriteInt16(point.Z);
        }

        private static ObjectivePoint ReadPoint(ref BitReader reader)
        {
            return new ObjectivePoint(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
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
