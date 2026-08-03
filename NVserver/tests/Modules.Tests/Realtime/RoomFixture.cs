using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using NV.Realtime;
using NV.Realtime.Contracts;
using NV.Realtime.Simulation;
using NV.Realtime.Transport;
using NV.Shared.Collision;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Transport;

namespace NV.Modules.Tests.Realtime
{
    /// 룸 테스트용 조립. 소켓 없이 룸을 돌린다.
    internal static class RoomFixture
    {
        /// 40x40 바닥과 X = 5 의 벽. 스폰은 원점 부근이다.
        public static WorldMap Map()
        {
            return new WorldMap(new MapData
            {
                Name = "test",
                Boxes = new[]
                {
                    new MapBox { MinX = -20f, MinY = -1f, MinZ = -20f, MaxX = 20f, MaxY = 0f, MaxZ = 20f },
                    new MapBox { MinX = 5f, MinY = 0f, MinZ = -20f, MaxX = 6f, MaxY = 4f, MaxZ = 20f },
                },
                Spawns = new[]
                {
                    new MapSpawn { X = 0f, Y = 0f, Z = 0f, Yaw = 0f },
                    new MapSpawn { X = -2f, Y = 0f, Z = 0f, Yaw = 0f },
                },
            });
        }

        public static NetworkConditionSimulator NoConditions()
        {
            return new NetworkConditionSimulator(new RealtimeOptions());
        }

        public static NetworkConditionSimulator Conditions(int latencyMs, int jitterMs, double loss)
        {
            return new NetworkConditionSimulator(new RealtimeOptions
            {
                NetworkConditionsEnabled = true,
                LatencyMilliseconds = latencyMs,
                JitterMilliseconds = jitterMs,
                PacketLoss = loss,
            });
        }

        public static Room Create(
            NetworkConditionSimulator? network = null,
            bool isStatic = false,
            string roomId = "test")
        {
            return new Room(roomId, Map(), network ?? NoConditions(), NullLogger.Instance, isStatic);
        }

        /// 세션 1..count 를 playerId 0..count-1 로 넣고 방장(세션 1)이 매치를 시작한다.
        ///
        /// 룸은 대기 단계로 열리므로 이 절차 없이는 스냅샷이 나오지 않는다.
        /// Join 과 Start 가 같은 드레인에 들어가며, 큐가 FIFO 라 Start 는 이미 모인
        /// 명단을 본다.
        public static void FillAndStart(Room room, int count = RealtimeConstants.Rooms.MinPlayersToStart)
        {
            for (var index = 0; index < count; index++)
            {
                room.PostCommand(RoomCommand.Join(index + 1, (byte)index, string.Empty, index == 0));
            }

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();
        }
    }

    /// 스냅샷을 모아 두는 전송 대역. 세션별 마지막 스냅샷을 들고 있다.
    internal sealed class RecordingTransport : IServerTransport
    {
        private readonly Dictionary<int, List<byte[]>> _sent = new();

        public List<string> Disconnected { get; } = new();

        public int TotalSent { get; private set; }

        public bool TrySend(int sessionId, ReadOnlySpan<byte> payload, Reliability reliability)
        {
            if (!_sent.TryGetValue(sessionId, out var list))
            {
                list = new List<byte[]>();
                _sent[sessionId] = list;
            }

            list.Add(payload.ToArray());
            TotalSent++;
            return true;
        }

        public void Disconnect(int sessionId, string reason)
        {
            Disconnected.Add(reason);
        }

        public int CountFor(int sessionId)
        {
            return _sent.TryGetValue(sessionId, out var list) ? list.Count : 0;
        }

        public int CountOf(int sessionId, MessageOpcode opcode)
        {
            if (!_sent.TryGetValue(sessionId, out var list))
            {
                return 0;
            }

            var count = 0;
            foreach (var payload in list)
            {
                if (MessageCodec.ReadOpcode(payload) == opcode)
                {
                    count++;
                }
            }

            return count;
        }

        /// 마지막 스냅샷. opcode 로 걸러야 한다 — 룸 상태 전문이 같은 대역으로 오므로
        /// 마지막 메시지를 그대로 스냅샷으로 읽으면 종류가 다른 프레임을 파싱한다.
        public bool TryLastSnapshot(int sessionId, out SnapshotHeader header, out EntityState[] entities)
        {
            header = default;
            entities = Array.Empty<EntityState>();

            if (!TryLastOf(sessionId, MessageOpcode.Snapshot, out var payload))
            {
                return false;
            }

            var buffer = new EntityState[RealtimeConstants.Rooms.MaxPlayers];
            var count = MessageCodec.ReadSnapshot(payload, out header, buffer);

            entities = new EntityState[count];
            Array.Copy(buffer, entities, count);
            return true;
        }

        public bool TryLastRoomState(int sessionId, out RoomStateHeader header, out RoomPlayerEntry[] players)
        {
            header = default;
            players = Array.Empty<RoomPlayerEntry>();

            if (!TryLastOf(sessionId, MessageOpcode.Event, out var payload))
            {
                return false;
            }

            var buffer = new RoomPlayerEntry[RealtimeConstants.Rooms.MaxPlayers];
            var count = MessageCodec.ReadRoomState(payload, out header, buffer);

            players = new RoomPlayerEntry[count];
            Array.Copy(buffer, players, count);
            return true;
        }

        private bool TryLastOf(int sessionId, MessageOpcode opcode, out byte[] payload)
        {
            payload = Array.Empty<byte>();

            if (!_sent.TryGetValue(sessionId, out var list))
            {
                return false;
            }

            for (var index = list.Count - 1; index >= 0; index--)
            {
                if (MessageCodec.ReadOpcode(list[index]) == opcode)
                {
                    payload = list[index];
                    return true;
                }
            }

            return false;
        }
    }
}
