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
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 룸 테스트용 조립. 소켓 없이 룸을 돌린다.
    internal static class RoomFixture
    {
        /// 40x40 바닥과 X = 5 의 벽. 스폰은 원점 부근이다.
        ///
        /// `withGrid` 면 6×6 격자를 함께 싣는다. **`FreeFloor` 는 손으로 적지 않고
        /// `MapGridBuilder.MarkFreeFloor` 로 실제 충돌에서 계산한다** — 손으로 적으면 벽
        /// 안의 셀을 통행 가능으로 표시하는 실수를 테스트가 그대로 믿는다. 그래서 벽(x 5~6)
        /// 이 지나가는 열은 자동으로 빠진다.
        ///
        /// 격자가 없는 맵도 필요하다. 목표물 배치가 격자를 요구하므로, "격자가 없으면
        /// 배치하지 않는다" 를 검사하려면 그쪽도 만들 수 있어야 한다.
        public static WorldMap Map(bool withGrid = true)
        {
            var data = new MapData
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
            };

            if (!withGrid)
            {
                return new WorldMap(data);
            }

            const int size = 6;

            var grid = new MapGridData
            {
                Floors = 1,
                Width = size,
                Depth = size,
                CellSize = 4f,
                FloorHeight = 4f,
                OriginX = -12f,
                OriginZ = -12f,
                Cells = new byte[size * size],
            };

            // 전부 격자상 통행 가능으로 두고, 몸이 실제로 들어가는지는 충돌이 답한다.
            for (var index = 0; index < grid.Cells.Length; index++)
            {
                grid.Cells[index] = (byte)MapCellFlags.Standable;
            }

            data.Grid = grid;

            // 콜리전을 먼저 만들어 FreeFloor 를 채운다. `WorldMap` 이 생성 시점에 후보
            // 목록을 고정하므로 그 전에 끝나야 한다.
            MapGridBuilder.MarkFreeFloor(grid, data.ToCollisionWorld());

            return new WorldMap(data);
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
            string roomId = "test",
            bool withGrid = true,
            BotOptions? bots = null)
        {
            return new Room(
                roomId,
                Map(withGrid),
                network ?? NoConditions(),
                NullLogger.Instance,
                isStatic,
                bots: bots);
        }

        /// 봇을 쓰는 룸. **정적이 아닌 룸도 만들 수 있어야 한다** — "초대 코드 룸에는
        /// 봇이 생기지 않는다" 를 검사하려면 설정이 켜진 비정적 룸이 필요하다.
        public static Room WithBots(
            int fillTo = 0,
            BotRolePreference role = BotRolePreference.Runner,
            bool isStatic = true,
            bool enabled = true,
            BotBehavior behavior = BotBehavior.Idle,
            uint seed = 0u,
            bool withGrid = true)
        {
            return Create(
                isStatic: isStatic,
                withGrid: withGrid,
                bots: new BotOptions
                {
                    Enabled = enabled,
                    FillTo = fillTo,
                    Role = role,
                    Behavior = behavior,
                    Seed = seed,
                });
        }

        /// 사람 하나를 **슬롯을 예약하고** 넣는다. 받은 `PlayerId` 를 돌려준다.
        ///
        /// `FillAndStart` 는 슬롯 예약 없이 `PlayerId` 를 손으로 정한다. 사람만 있는 룸에서는
        /// 그래도 되지만 **봇이 있으면 안 된다** — 봇은 `TryReserveSlot` 으로 자리를 받으므로,
        /// 예약되지 않은 슬롯 배열을 보고 사람이 이미 쓰는 번호를 집는다. 증상은 한 룸에
        /// 같은 `PlayerId` 가 둘이 되는 것이고, 실제 경로(`/ws`)는 예약하므로 나타나지 않는다.
        public static byte JoinHuman(Room room, int sessionId, bool isHost = false)
        {
            Assert.True(room.TryReserveSlot(out var playerId), "슬롯이 없다.");

            room.PostCommand(RoomCommand.Join(sessionId, playerId, "human", isHost));

            return playerId;
        }

        /// 봇 커맨드가 적용될 때까지 틱을 돌린다.
        ///
        /// **두 틱이 필요하다.** 채우기는 `Advance` 의 마지막 단계에서 커맨드를 붙이고,
        /// 그 커맨드는 다음 `Advance` 의 첫 단계에서 적용된다. 한 틱만 돌리고 인원을
        /// 확인하면 아직 붙어 있는 커맨드를 보게 된다.
        public static void SettleBots(Room room, int ticks = 2)
        {
            for (var index = 0; index < ticks; index++)
            {
                room.Advance();
            }
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

            // **방장을 뺀 전원이 준비해야 시작된다.** 방장(세션 1)은 보내지 않는다 — 시작
            // 버튼을 누르는 것이 그 사람의 준비다(`Room.EveryoneElseIsReady`).
            //
            // 같은 드레인에 넣어도 된다. 큐가 FIFO 이므로 Join → SetReady → Start 순으로
            // 적용되고, 준비는 이미 명단에 있는 사람에게 적힌다.
            for (var index = 1; index < count; index++)
            {
                room.PostCommand(RoomCommand.SetReady(index + 1, true));
            }

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            if (skipReveal)
            {
                SkipReveal(room);
            }
        }

        /// 이 세션들이 준비를 누른다.
        ///
        /// **방장은 넣지 않는다.** 시작 버튼이 방장의 준비이고, 시작 조건은 요청자를 뺀
        /// 나머지만 본다(`Room.EveryoneElseIsReady`).
        ///
        /// 매치가 끝나 로비로 돌아오면 준비가 전원 내려가므로(`Room.ResetToWaiting`),
        /// 두 번째 매치를 시작하려면 다시 불러야 한다. 그것이 규칙이며 테스트가 그것을
        /// 그대로 밟는다.
        public static void Ready(Room room, params int[] sessionIds)
        {
            foreach (var sessionId in sessionIds)
            {
                room.PostCommand(RoomCommand.SetReady(sessionId, true));
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

        /// 목표물 전문. **문이 실려 있는지는 헤더의 `HasDoor` 로 본다** — Seeker 사본에서는
        /// 블록 자체가 없다.
        public bool TryLastObjectiveState(
            int sessionId,
            out ObjectiveStateHeader header,
            out ObjectivePoint door,
            out ObjectivePoint[] keys)
        {
            header = default;
            door = default;
            keys = Array.Empty<ObjectivePoint>();

            if (!TryLastEvent(sessionId, EventKind.ObjectiveState, out var payload))
            {
                return false;
            }

            var keyBuffer = new ObjectivePoint[64];
            var deviceBuffer = new ObjectiveDevice[16];

            var count = MessageCodec.ReadObjectiveState(
                payload,
                out header,
                out _,
                out _,
                out door,
                out _,
                out _,
                keyBuffer,
                deviceBuffer);

            keys = new ObjectivePoint[count];
            Array.Copy(keyBuffer, keys, count);
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
