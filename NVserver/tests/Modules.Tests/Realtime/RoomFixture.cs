using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using NV.Realtime.Contracts;
using NV.Realtime.Simulation;
using NV.Realtime.Transport;
using NV.Shared.Collision;
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

        public static Room Create(NetworkConditionSimulator? network = null)
        {
            return new Room("test", Map(), network ?? NoConditions(), NullLogger.Instance);
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

        public bool TryLastSnapshot(int sessionId, out SnapshotHeader header, out EntityState[] entities)
        {
            header = default;
            entities = Array.Empty<EntityState>();

            if (!_sent.TryGetValue(sessionId, out var list) || list.Count == 0)
            {
                return false;
            }

            var buffer = new EntityState[Room.MaxPlayers];
            var count = MessageCodec.ReadSnapshot(list[^1], out header, buffer);

            entities = new EntityState[count];
            Array.Copy(buffer, entities, count);
            return true;
        }
    }
}
