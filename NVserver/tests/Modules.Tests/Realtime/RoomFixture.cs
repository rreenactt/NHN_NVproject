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
        ///
        /// **기본으로 역할 공개 구간까지 지나간다.** 매치는 `RoleReveal` 로 시작하고 그
        /// 동안 이동이 잠기므로, 이동을 검사하는 테스트가 이 구간에 걸리면 "서버가 입력을
        /// 무시한다" 로 보인다. 리빌 자체를 검사할 때만 `skipReveal: false` 로 부른다.
        public static void FillAndStart(
            Room room,
            int count = RealtimeConstants.Rooms.MinPlayersToStart,
            bool skipReveal = true)
        {
            for (var index = 0; index < count; index++)
            {
                room.PostCommand(RoomCommand.Join(index + 1, (byte)index, string.Empty, index == 0));
            }

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            if (skipReveal)
            {
                SkipReveal(room);
            }
        }

        /// 역할 공개가 끝날 때까지 틱을 돌린다.
        ///
        /// 스냅샷을 보내지 않는다(`Broadcast` 를 부르지 않는다) — 이 구간의 프레임까지
        /// 세면 전송 수를 확인하는 테스트가 리빌 길이에 묶인다.
        public static void SkipReveal(Room room)
        {
            // 상한을 둔다. 단계가 넘어가지 않는 버그가 들어오면 테스트가 멈추는 대신
            // 실패해야 한다.
            for (var guard = 0; guard < 10_000 && room.MatchPhase == MatchPhase.RoleReveal; guard++)
            {
                room.Advance();
            }
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

        /// **종류까지 걸러야 한다.** 매치 상태 전문도 같은 `Event` opcode 로 오므로,
        /// opcode 만 보고 마지막 프레임을 집으면 종류가 다른 전문을 룸 상태로 파싱하고
        /// `ReadRoomState` 가 예외를 던진다. 클라이언트의 `DispatchEvent` 가 같은 이유로
        /// 종류를 먼저 본다.
        public bool TryLastRoomState(int sessionId, out RoomStateHeader header, out RoomPlayerEntry[] players)
        {
            header = default;
            players = Array.Empty<RoomPlayerEntry>();

            if (!TryLastEvent(sessionId, EventKind.RoomState, out var payload))
            {
                return false;
            }

            var buffer = new RoomPlayerEntry[RealtimeConstants.Rooms.MaxPlayers];
            var count = MessageCodec.ReadRoomState(payload, out header, buffer);

            players = new RoomPlayerEntry[count];
            Array.Copy(buffer, players, count);
            return true;
        }

        public bool TryLastMatchState(int sessionId, out MatchStateHeader header, out MatchParticipant[] participants)
        {
            header = default;
            participants = Array.Empty<MatchParticipant>();

            if (!TryLastEvent(sessionId, EventKind.MatchState, out var payload))
            {
                return false;
            }

            var buffer = new MatchParticipant[RealtimeConstants.Rooms.MaxPlayers];
            var count = MessageCodec.ReadMatchState(payload, out header, buffer);

            participants = new MatchParticipant[count];
            Array.Copy(buffer, participants, count);
            return true;
        }

        /// 이 세션에 마지막으로 간 그 종류의 전문.
        public bool TryLastEvent(int sessionId, EventKind kind, out byte[] payload)
        {
            payload = Array.Empty<byte>();

            if (!_sent.TryGetValue(sessionId, out var list))
            {
                return false;
            }

            for (var index = list.Count - 1; index >= 0; index--)
            {
                if (MessageCodec.ReadEventKind(list[index]) == kind)
                {
                    payload = list[index];
                    return true;
                }
            }

            return false;
        }

        public int CountOfEvent(int sessionId, EventKind kind)
        {
            if (!_sent.TryGetValue(sessionId, out var list))
            {
                return 0;
            }

            var count = 0;
            foreach (var payload in list)
            {
                if (MessageCodec.ReadEventKind(payload) == kind)
                {
                    count++;
                }
            }

            return count;
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
