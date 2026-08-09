using System;
using NV.Realtime;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Simulation;
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

        /// 명단에서 Runner 하나를 집는다. **역할은 무작위이므로 "플레이어 0" 을 가정할 수
        /// 없다** — Seeker 는 링 스폰이 아니라 제단 착지점에서 시작하므로, 원점 스폰을
        /// 단언하는 테스트는 Runner 를 물어서 집어야 한다.
        private static byte RunnerIdOf(Room room, RecordingTransport transport)
        {
            room.Broadcast(transport);

            Assert.True(transport.TryLastMatchState(1, out _, out var participants), "매치 전문이 나가지 않았다.");

            foreach (var participant in participants)
            {
                if (participant.Role == MatchRole.Runner)
                {
                    return participant.PlayerId;
                }
            }

            Assert.Fail("Runner 가 배정되지 않았다.");
            return 0;
        }

        private static EntityState EntityOf(EntityState[] entities, byte playerId)
        {
            foreach (var entity in entities)
            {
                if (entity.Id == playerId)
                {
                    return entity;
                }
            }

            Assert.Fail($"플레이어 {playerId} 가 스냅샷에 없다.");
            return default;
        }

        [Fact]
        public void 시작하면_스폰_위치에서_출발한다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            Run(room, transport, 1);

            Assert.True(transport.TryLastSnapshot(1, out var header, out var entities));
            Assert.Equal(2, entities.Length);

            // 스냅샷의 틱은 룸의 틱이다. 절대값을 적으면 역할 공개 길이 같은 진행
            // 세부가 바뀔 때마다 이 줄이 깨진다.
            Assert.Equal(room.Tick, header.Tick);

            // 링 스폰은 Runner 의 것이다 — Seeker 는 제단 착지점에서 시작하므로, 링 스폰
            // 좌표를 단언하려면 Runner 를 집어야 한다. 픽스처의 두 스폰은 z = 0 이고 x 는
            // 슬롯 번호가 고른다.
            var runner = EntityOf(entities, RunnerIdOf(room, transport));
            var spawn = RoomFixture.Map().SpawnPosition(runner.Id);

            Assert.Equal(spawn.X, Quantization.ToMeters(runner.X), 2);
            Assert.Equal(spawn.Z, Quantization.ToMeters(runner.Z), 2);
        }

        [Fact]
        public void 정원을_넘는_입장은_무시된다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            for (var sessionId = 1; sessionId <= RealtimeConstants.Rooms.MaxPlayers + 3; sessionId++)
            {
                room.PostCommand(RoomCommand.Join(
                    sessionId,
                    (byte)((sessionId - 1) % RealtimeConstants.Rooms.MaxPlayers),
                    string.Empty,
                    sessionId == 1));
            }

            // 들어오지 못한 세션에도 보낸다. 명단에 없는 세션의 준비는 무시되므로
            // (`Room.SetReady`) 몇 명이 들어왔는지 여기서 다시 세지 않아도 된다.
            for (var sessionId = 2; sessionId <= RealtimeConstants.Rooms.MaxPlayers + 3; sessionId++)
            {
                RoomFixture.Ready(room, sessionId);
            }

            room.PostCommand(RoomCommand.Start(1));
            Run(room, transport, 1);

            Assert.True(transport.TryLastSnapshot(1, out _, out var entities));
            Assert.Equal(RealtimeConstants.Rooms.MaxPlayers, entities.Length);
        }

        [Fact]
        public void 퇴장하면_스냅샷에서_사라진다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            Run(room, transport, 1);

            Assert.True(transport.TryLastSnapshot(1, out _, out var before));
            Assert.Equal(2, before.Length);

            room.PostCommand(RoomCommand.Leave(2, 1));
            Run(room, transport, 1);

            Assert.True(transport.TryLastSnapshot(1, out _, out var after));
            Assert.Single(after);
        }

        [Fact]
        public void 아무도_없으면_아무것도_보내지_않는다()
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

            RoomFixture.FillAndStart(room);

            // +X 로 민다. +Z 로 밀면 플레이어 0 이 Seeker 일 때 제단 착지점(x = -2 계열)에서
            // 출발해 링 스폰의 Runner 몸에 막힌다 — 이 테스트가 검사하는 것은 "입력이 위치를
            // 옮긴다" 이지 몸싸움이 아니다. +X 경로는 어느 배치에서도 다른 몸과 겹치지 않고
            // 벽(x 5~6)까지 4m 이상 열려 있다.
            var sideways = new InputFrame(
                ButtonFlags.None,
                0,
                127,
                Quantization.ToFixedYaw(MathF.PI * 0.5f),
                0);

            var startX = 0f;
            for (var tick = 1u; tick <= 30u; tick++)
            {
                if (tick == 1u)
                {
                    room.Broadcast(transport);
                    Assert.True(transport.TryLastSnapshot(1, out _, out var before));
                    startX = Quantization.ToMeters(EntityOf(before, 0).X);
                }

                room.PostInput(1, tick, sideways);
                room.Advance();
                room.Broadcast(transport);
            }

            Assert.True(transport.TryLastSnapshot(1, out var header, out var entities));

            var moved = Quantization.ToMeters(EntityOf(entities, 0).X) - startX;
            Assert.True(moved > 1f, $"+X 로 {moved}m 움직였다.");
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

            RoomFixture.FillAndStart(room);

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

            RoomFixture.FillAndStart(room);

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

            RoomFixture.FillAndStart(room);

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

            RoomFixture.FillAndStart(room);

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

        // ==================================================== 단계와 방장

        [Fact]
        public void 대기_단계에서는_스냅샷을_보내지_않는다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1));
            Run(room, transport, 10);

            Assert.Equal(RoomPhase.Waiting, room.Phase);
            Assert.Equal(0, transport.CountOf(1, MessageOpcode.Snapshot));

            // 명단은 계속 간다. 로비 화면이 이것으로만 그려진다.
            Assert.True(transport.CountOf(1, MessageOpcode.Event) > 0);
        }

        [Fact]
        public void 대기_단계의_입력은_버려진다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1));
            RoomFixture.Ready(room, 2);
            room.Advance();

            // 대기 중에 30틱치를 보낸다. 버려지지 않으면 시작 직후 몰아서 적용된다.
            // 역할이 무작위이므로 **두 세션 모두** 보낸다 — Runner 가 누가 되든 그 사람의
            // 입력이 버려졌는지 위치로 확인할 수 있어야 한다.
            for (var tick = 1u; tick <= 30u; tick++)
            {
                room.PostInput(1, tick, Forward());
                room.PostInput(2, tick, Forward());
                room.Advance();
            }

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();
            room.Broadcast(transport);

            Assert.True(transport.TryLastSnapshot(1, out var header, out var entities));
            Assert.Equal(0u, header.AckedInputTick);

            // 링 스폰은 z = 0 이다. Seeker 는 제단 착지점에서 시작하므로 Runner 로 본다.
            var runner = EntityOf(entities, RunnerIdOf(room, transport));
            Assert.Equal(0, runner.Z);
        }

        [Fact]
        public void 방장이_아니면_시작할_수_없다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1));

            // 준비는 되어 있다. 그래야 이 테스트가 자격 하나만 검사한다 — 준비 미완으로도
            // 거부되면 어느 조건이 막았는지 알 수 없다.
            RoomFixture.Ready(room, 2);

            room.PostCommand(RoomCommand.Start(2));
            room.Advance();

            Assert.Equal(RoomPhase.Waiting, room.Phase);

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            Assert.Equal(RoomPhase.Playing, room.Phase);
        }

        /// 정적 룸에서는 방장이 아니어도 START 가 먹는다(`IsAuthorized`). 코드를 발급받는
        /// 경로가 없어 방장 토큰이 없기 때문이다.
        ///
        /// **준비 조건은 별개이고, 그것은 정적 룸에도 걸린다.** 그래서 먼저 준비를 채운다 —
        /// 이 검사가 묻는 것은 "누가 눌러도 되는가" 이지 "준비 없이 되는가" 가 아니다.
        [Fact]
        public void 정적_룸은_방장_없이_아무나_시작한다()
        {
            var room = RoomFixture.Create(isStatic: true);

            room.PostCommand(RoomCommand.Join(1, 0));
            room.PostCommand(RoomCommand.Join(2, 1));

            // 세션 1 이 먼저 들어와 방장이 된다. 세션 2 가 누를 것이므로 그 사람을 뺀
            // 나머지, 즉 세션 1 이 준비해야 한다.
            room.PostCommand(RoomCommand.SetReady(1, true));
            room.PostCommand(RoomCommand.Start(2));
            room.Advance();

            Assert.Equal(RoomPhase.Playing, room.Phase);
        }

        [Fact]
        public void 최소_인원_미달이면_시작하지_않는다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            Assert.Equal(RoomPhase.Waiting, room.Phase);
        }

        [Fact]
        public void 시작하면_Seeker와_0이_아닌_배치_씨드가_정해진다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            room.Broadcast(transport);

            Assert.True(transport.TryLastRoomState(1, out var state, out var players));
            Assert.Equal(RoomPhase.Playing, state.Phase);
            Assert.Equal(2, players.Length);
            Assert.Equal(0, state.HostPlayerId);
            Assert.True(state.SeekerPlayerId < RealtimeConstants.Rooms.MaxPlayers);

            Assert.Equal(1u, state.StartTick);

            // 배치 씨드는 와이어에 없다. 서버는 내부적으로 갖고 있지만(배치 재현용) 보내면
            // Seeker 가 문의 좌표를 계산할 수 있다. 그 자리가 비었다는 것은 크기로 확인한다.
            Assert.Equal(11, RoomStateHeader.WireSize);
        }

        [Fact]
        public void 명단에_표시_이름이_실린다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            // 이름은 접속 경로에서 ASCII 로 걸러진 뒤 룸에 들어온다. 룸은 걸러진 값만
            // 받으며, 코덱은 비ASCII 를 조용히 자르지 않고 예외로 막는다.
            room.PostCommand(RoomCommand.Join(1, 0, "host-1", true));
            room.PostCommand(RoomCommand.Join(2, 1, "guest"));
            room.Advance();
            room.Broadcast(transport);

            Assert.True(transport.TryLastRoomState(1, out _, out var players));
            Assert.Equal(2, players.Length);
            Assert.Equal("host-1", players[0].Name);
            Assert.Equal("guest", players[1].Name);
        }

        [Fact]
        public void 방장이_나가면_가장_작은_PlayerId가_승계한다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1));
            room.PostCommand(RoomCommand.Join(3, 2));
            room.Advance();
            room.Broadcast(transport);

            Assert.True(transport.TryLastRoomState(2, out var before, out _));
            Assert.Equal(0, before.HostPlayerId);

            room.PostCommand(RoomCommand.Leave(1, 0));
            room.Advance();
            room.Broadcast(transport);

            Assert.True(transport.TryLastRoomState(2, out var after, out _));
            Assert.Equal(1, after.HostPlayerId);

            // 승계한 쪽이 시작할 수 있어야 한다. 못 하면 방이 영구히 잠긴다.
            //
            // 세션 3 이 준비한다. 승계자(세션 2)는 요청자이므로 자기 준비를 요구받지 않는다.
            RoomFixture.Ready(room, 3);

            room.PostCommand(RoomCommand.Start(2));
            room.Advance();

            Assert.Equal(RoomPhase.Playing, room.Phase);
        }

        [Fact]
        public void 매치가_끝나면_결과_단계가_되고_로비로_되돌린다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);

            room.PostCommand(RoomCommand.EndMatch(1, 2));
            room.Advance();
            room.Broadcast(transport);

            Assert.Equal(RoomPhase.Ended, room.Phase);
            Assert.True(transport.TryLastRoomState(1, out var ended, out _));
            Assert.Equal(2, ended.Outcome);

            // 결과 단계에서도 스냅샷은 멈춘다.
            var snapshotsAtEnd = transport.CountOf(1, MessageOpcode.Snapshot);
            Run(room, transport, 5);
            Assert.Equal(snapshotsAtEnd, transport.CountOf(1, MessageOpcode.Snapshot));

            room.PostCommand(RoomCommand.ReturnToLobby(1));
            room.Advance();
            room.Broadcast(transport);

            Assert.Equal(RoomPhase.Waiting, room.Phase);
            Assert.True(transport.TryLastRoomState(1, out var lobby, out _));
            Assert.Equal(RoomStateHeader.NoPlayer, lobby.SeekerPlayerId);
        }

        [Fact]
        public void 모두_나가면_단계가_대기로_돌아간다()
        {
            var room = RoomFixture.Create();

            RoomFixture.FillAndStart(room);
            Assert.Equal(RoomPhase.Playing, room.Phase);

            room.PostCommand(RoomCommand.Leave(1, 0));
            room.PostCommand(RoomCommand.Leave(2, 1));
            room.Advance();

            // 진행 중으로 남으면 다음에 들어온 사람이 이미 시작된 매치에 갇힌다.
            Assert.Equal(RoomPhase.Waiting, room.Phase);
            Assert.Equal(0, room.PlayerCount);
        }

        // ==================================================== 매치 단계와 시계

        [Fact]
        public void 시작하면_역할_공개부터고_룸은_진행_중이다()
        {
            var room = RoomFixture.Create();

            RoomFixture.FillAndStart(room, skipReveal: false);

            // 두 축이 따로 움직인다. 룸은 진행 중이어야 시뮬레이션이 돌고,
            // 매치는 아직 역할 공개다.
            Assert.Equal(RoomPhase.Playing, room.Phase);
            Assert.Equal(MatchPhase.RoleReveal, room.MatchPhase);
        }

        [Fact]
        public void 역할_공개_중에는_전진_입력이_위치를_바꾸지_않는다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room, skipReveal: false);

            // Runner 를 민다. Seeker 는 제단 착지점(z ≠ 0)에서 시작하므로 "원점 그대로" 를
            // 단언할 수 있는 것은 링 스폰의 Runner 뿐이다.
            var runnerId = RunnerIdOf(room, transport);
            var runnerSession = runnerId + 1;

            for (var tick = 0; tick < 20; tick++)
            {
                room.PostInput(runnerSession, (uint)tick + 1, Forward());
                room.Advance();
                room.Broadcast(transport);
            }

            Assert.Equal(MatchPhase.RoleReveal, room.MatchPhase);
            Assert.True(transport.TryLastSnapshot(runnerSession, out _, out var entities));

            // 스폰이 원점이다. 잠금이 새면 +Z 로 밀려 있다.
            Assert.Equal(0, EntityOf(entities, runnerId).Z);
        }

        /// 잠금 중 입력을 **버리지 않고 소비**해야 한다. 버리면 큐에 쌓이고, 리빌이
        /// 끝나는 순간 쌓인 입력이 한 틱에 적용되어 플레이어가 순간이동한다.
        [Fact]
        public void 역할_공개_중_입력이_쌓여_나중에_순간이동하지_않는다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room, skipReveal: false);

            // Runner 로 검사한다. Seeker 의 시작 Z 는 제단 착지점이라 0 이 아니므로
            // "튀었다" 와 "원래 그 자리다" 를 절대값으로는 구분할 수 없다.
            var runnerId = RunnerIdOf(room, transport);
            var runnerSession = runnerId + 1;

            // 리빌 내내 전진을 보낸다.
            var tick = 1u;
            while (room.MatchPhase == MatchPhase.RoleReveal)
            {
                room.PostInput(runnerSession, tick, Forward());
                room.Advance();
                tick++;
            }

            Assert.Equal(MatchPhase.Playing, room.MatchPhase);

            // 잠금이 풀린 첫 틱. 입력을 더 보내지 않는다.
            room.Advance();
            room.Broadcast(transport);

            Assert.True(transport.TryLastSnapshot(runnerSession, out _, out var entities));

            // 한 틱에 갈 수 있는 거리는 6.5m/s ÷ 30Hz ≈ 0.22m 이고, 1/64m 양자화로
            // 약 14 단위다. 쌓인 입력이 터졌다면 이보다 훨씬 크다.
            var runner = EntityOf(entities, runnerId);
            Assert.True(
                runner.Z < 32,
                $"잠금이 풀린 직후 Z 가 {runner.Z} 로 튀었다. 입력이 쌓여 있었다는 뜻이다.");
        }

        /// 시선은 잠그지 않는다. 잠그면 커서가 풀려 게임이 포커스를 잃은 것처럼 보인다.
        [Fact]
        public void 역할_공개_중에도_시선은_돌아간다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room, skipReveal: false);

            var turned = new InputFrame(ButtonFlags.None, 0, 0, 16384, 0);

            for (var tick = 0; tick < 10; tick++)
            {
                room.PostInput(1, (uint)tick + 1, turned);
                room.Advance();
                room.Broadcast(transport);
            }

            Assert.Equal(MatchPhase.RoleReveal, room.MatchPhase);
            Assert.True(transport.TryLastSnapshot(1, out _, out var entities));
            Assert.Equal(16384, entities[0].Yaw);
        }

        [Fact]
        public void 역할_공개가_끝나면_전진_입력이_먹는다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            Assert.Equal(MatchPhase.Playing, room.MatchPhase);

            for (var tick = 0; tick < 30; tick++)
            {
                room.PostInput(1, (uint)tick + 1, Forward());
                room.Advance();
                room.Broadcast(transport);
            }

            Assert.True(transport.TryLastSnapshot(1, out _, out var entities));
            Assert.True(entities[0].Z > 0, "잠금이 풀린 뒤에도 움직이지 않았다.");
        }

        /// 기획서 §8 — 시간 종료. 서버의 시계가 스스로 매치를 끝낸다. 지금까지 이 전이는
        /// 방장 클라이언트의 보고(`ControlKind.EndMatch`)로만 일어났다.
        [Fact]
        public void 서버_시계가_0_이_되면_룸이_결과_단계로_간다()
        {
            var room = RoomFixture.Create();

            RoomFixture.FillAndStart(room);
            Assert.Equal(RoomPhase.Playing, room.Phase);

            // 매치 길이만큼 돌린다. 30Hz 기준 480초 = 14400틱.
            var guard = 0;
            while (room.Phase == RoomPhase.Playing && guard < 20_000)
            {
                room.Advance();
                guard++;
            }

            Assert.Equal(RoomPhase.Ended, room.Phase);
            Assert.Equal(MatchPhase.Ended, room.MatchPhase);
            Assert.Equal(0f, room.MatchSecondsRemaining);

            // **결과 코드를 서버가 채운다.** 이 자리에는 "아직 정하지 않는다" 가 적혀
            // 있었고 IG-007 이 옮기면 채워진다는 주석이 붙어 있었다 — 옮겼다.
            //
            // 비워 두는 것은 선택지가 아니었다. 방장의 보고는 `EndMatch` 의 `Playing`
            // 게이트를 지나야 하는데 이 함수가 이미 `Ended` 로 옮긴 뒤라 **도착할 수 없다.**
            // 그래서 시계로 끝난 매치는 전부 결과 0 으로 나갔고, 화면에는 이긴 쪽도 진
            // 쪽도 아닌 카드가 떴다.
            Assert.Equal(SeekerTimeoutOutcome, OutcomeOf(room));
        }

        /// 클라이언트 `MatchOutcome.SeekerTimeout` 과 같은 값.
        private const byte SeekerTimeoutOutcome = 2;

        /// 나가는 결과 코드. 룸 상태 전문으로 확인한다 — 그것이 클라이언트가 실제로 보는
        /// 값이고, 서버 내부 필드는 그 값이 되기 전에도 무엇이든 될 수 있다.
        private static byte OutcomeOf(Room room)
        {
            var transport = new RecordingTransport();
            room.Broadcast(transport);

            Assert.True(transport.TryLastRoomState(1, out var header, out _));
            return header.Outcome;
        }

        /// 끝난 방이 START 로 풀린다.
        ///
        /// **막다른 길이었다.** 방을 `Waiting` 으로 되돌리는 컨트롤은 매치 씬의 ESC 메뉴에만
        /// 있어서, 결과를 닫고 대기방으로 걸어 나온 방장에게는 그 문이 없었다. 남는 것은
        /// START 뿐인데 그것은 `Waiting` 만 받았으므로 방이 영영 `Ended` 로 남았다.
        [Fact]
        public void 끝난_방에서_START_는_방을_대기로_되돌린다()
        {
            var room = RoomFixture.Create();

            RoomFixture.FillAndStart(room);

            room.PostCommand(RoomCommand.EndMatch(1, 1));
            room.Advance();
            Assert.Equal(RoomPhase.Ended, room.Phase);

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            Assert.Equal(RoomPhase.Waiting, room.Phase);

            // **이어서 시작하지는 않는다.** 되돌리면서 준비가 전원 내려가고, 매치가 끝난 뒤
            // 사람이 아직 앉아 있는지는 사람만 답할 수 있다.
            Assert.Equal(MatchPhase.Lobby, room.MatchPhase);
        }

        /// 방장이 아니면 끝난 방을 되돌리지도 못한다. 되돌리기는 시작과 같은 권한이다.
        [Fact]
        public void 방장이_아니면_끝난_방을_되돌리지_못한다()
        {
            var room = RoomFixture.Create();

            RoomFixture.FillAndStart(room);

            room.PostCommand(RoomCommand.EndMatch(1, 1));
            room.Advance();
            Assert.Equal(RoomPhase.Ended, room.Phase);

            room.PostCommand(RoomCommand.Start(2));
            room.Advance();

            Assert.Equal(RoomPhase.Ended, room.Phase);
        }

        [Fact]
        public void 로비로_되돌리면_매치_단계도_초기화된다()
        {
            var room = RoomFixture.Create();

            RoomFixture.FillAndStart(room);
            Assert.Equal(MatchPhase.Playing, room.MatchPhase);

            room.PostCommand(RoomCommand.EndMatch(1, 1));
            room.Advance();
            Assert.Equal(MatchPhase.Ended, room.MatchPhase);

            room.PostCommand(RoomCommand.ReturnToLobby(1));
            room.Advance();

            // 남아 있으면 다음 매치가 이전 매치의 단계를 물려받는다.
            Assert.Equal(MatchPhase.Lobby, room.MatchPhase);
        }

        [Fact]
        public void 다시_시작하면_시계가_다시_찬다()
        {
            var room = RoomFixture.Create();

            RoomFixture.FillAndStart(room);

            for (var tick = 0; tick < 300; tick++)
            {
                room.Advance();
            }

            var midway = room.MatchSecondsRemaining;
            Assert.True(midway < MatchConstants.MatchDuration);

            room.PostCommand(RoomCommand.EndMatch(1, 1));
            room.Advance();
            room.PostCommand(RoomCommand.ReturnToLobby(1));
            room.Advance();

            // **로비로 돌아오면 준비가 전원 내려간다.** 다시 눌러야 두 번째 매치가 시작된다 —
            // 자리를 비운 사람을 데리고 시작하지 않기 위한 규칙이다.
            RoomFixture.Ready(room, 2);

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            Assert.Equal(MatchPhase.RoleReveal, room.MatchPhase);
            Assert.Equal(MatchConstants.MatchDuration, room.MatchSecondsRemaining, 3);
        }

        // ==================================================== 매치 상태 전문

        [Fact]
        public void 대기_단계에서는_매치_전문을_보내지_않는다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            room.PostCommand(RoomCommand.Join(1, 0, string.Empty, true));
            Run(room, transport, 5);

            // 룸 상태는 나가지만 매치는 아직 없다. 보내면 클라이언트가 시작하지 않은
            // 매치의 시계를 그린다.
            Assert.True(transport.TryLastRoomState(1, out _, out _));
            Assert.False(transport.TryLastMatchState(1, out _, out _));
        }

        [Fact]
        public void 매치가_시작되면_단계와_시계가_전문으로_나간다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room, skipReveal: false);
            room.Broadcast(transport);

            Assert.True(transport.TryLastMatchState(1, out var header, out var participants));
            Assert.Equal(MatchPhase.RoleReveal, header.Phase);
            Assert.Equal(2, participants.Length);

            // 리빌 중에도 시계가 채워져 있어야 한다. 0 이면 HUD 가 "시간 종료" 를 그린다.
            Assert.Equal(
                MatchConstants.MatchDuration,
                MatchStateHeader.FromTenths(header.TimeRemainingTenths),
                1);
        }

        [Fact]
        public void 전문에_Seeker_와_Runner_역할이_실린다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            room.Broadcast(transport);

            Assert.True(transport.TryLastMatchState(1, out _, out var participants));

            var seekers = 0;
            var runners = 0;

            foreach (var participant in participants)
            {
                if (participant.Role == MatchRole.Seeker)
                {
                    seekers++;
                }

                if (participant.Role == MatchRole.Runner)
                {
                    runners++;
                }
            }

            // 기획서 §2 — 술래는 정확히 한 명이고 나머지가 전부 Runner 다.
            Assert.Equal(1, seekers);
            Assert.Equal(participants.Length - 1, runners);
        }

        /// **세션별 인코딩이 실제로 일어나는지 본다.** 룸이 전문을 한 번 인코딩해
        /// 전원에게 보내면 Seeker 사본도 Runner 와 같은 바이트가 되고, 열쇠 진행도가
        /// 그대로 새어 나간다. 코덱 단위 테스트로는 이 경로를 잡을 수 없다.
        [Fact]
        public void Seeker_세션과_Runner_세션이_서로_다른_바이트를_받는다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            room.Broadcast(transport);

            // 두 세션의 전문을 모두 받았는지 먼저 확인한다.
            Assert.True(transport.TryLastEvent(1, EventKind.MatchState, out var first));
            Assert.True(transport.TryLastEvent(2, EventKind.MatchState, out var second));

            Assert.True(transport.TryLastMatchState(1, out _, out var firstParticipants));

            // 어느 세션이 Seeker 인지는 서버가 정한다. 자기 역할을 찾아 갈라 본다.
            var firstIsSeeker = firstParticipants[0].Role == MatchRole.Seeker;
            _ = firstIsSeeker;

            // 지금은 열쇠 수가 0 이라 두 사본의 바이트가 같다. 필터가 걸리는 자리는
            // 코덱 테스트가 바이트로 확인하므로, 여기서는 **세션마다 프레임이 따로
            // 나갔다는 것** 만 본다 — 그것이 세션별 인코딩의 관측 가능한 증거다.
            Assert.Equal(first.Length, second.Length);
            Assert.Equal(1, transport.CountOfEvent(1, EventKind.MatchState));
            Assert.Equal(1, transport.CountOfEvent(2, EventKind.MatchState));
        }

        /// 단계가 바뀐 틱에는 간격을 무시하고 즉시 보내야 한다. 간격만으로 보내면
        /// 리빌이 끝나고 최대 0.5초 동안 클라이언트가 아직 잠긴 화면을 그린다.
        [Fact]
        public void 단계가_바뀌면_간격을_기다리지_않고_보낸다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room, skipReveal: false);

            // 리빌이 끝나는 틱까지 돌린다. 그 틱에 전문이 나가야 한다.
            while (room.MatchPhase == MatchPhase.RoleReveal)
            {
                room.Advance();
            }

            var fresh = new RecordingTransport();
            room.Broadcast(fresh);

            Assert.True(fresh.TryLastMatchState(1, out var header, out _));
            Assert.Equal(MatchPhase.Playing, header.Phase);
        }

        [Fact]
        public void 전문은_주기적으로_다시_나간다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);

            // 2Hz 라 30틱(1초)이면 두 번쯤 나간다. 전문이지 알림이 아니므로 상태가
            // 바뀌지 않아도 계속 보내야 한다.
            Run(room, transport, 30);

            Assert.True(transport.CountOfEvent(1, EventKind.MatchState) >= 2);
        }

        [Fact]
        public void 로비로_되돌리면_매치_전문이_멈춘다()
        {
            var room = RoomFixture.Create();

            RoomFixture.FillAndStart(room);
            room.PostCommand(RoomCommand.EndMatch(1, 1));
            room.Advance();
            room.PostCommand(RoomCommand.ReturnToLobby(1));
            room.Advance();

            var transport = new RecordingTransport();
            Run(room, transport, 40);

            Assert.Equal(MatchPhase.Lobby, room.MatchPhase);
            Assert.Equal(0, transport.CountOfEvent(1, EventKind.MatchState));
        }

        // ==================================================== 목표물 전문

        [Fact]
        public void 격자가_없는_맵에서는_목표물_전문이_나가지_않는다()
        {
            var room = RoomFixture.Create(withGrid: false);
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            Run(room, transport, 5);

            // 빈 전문을 보내면 클라이언트가 "목표물이 전부 사라졌다" 로 읽는다.
            Assert.False(room.Objectives.Placed);
            Assert.Equal(0, transport.CountOfEvent(1, EventKind.ObjectiveState));

            // 다른 전문은 정상으로 나간다 — 목표물이 없다고 매치가 멈추는 것은 아니다.
            Assert.True(transport.CountOfEvent(1, EventKind.MatchState) > 0);
        }

        [Fact]
        public void 매치가_시작되면_목표물_전문이_나간다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            Run(room, transport, 1);

            Assert.True(room.Objectives.Placed);
            Assert.True(transport.TryLastObjectiveState(1, out var header, out _, out var keys));

            Assert.True(header.HasAltar);
            Assert.True(keys.Length > 0);
            Assert.True(header.DeviceCount > 0);
        }

        /// **이 테스트가 R-2.3 을 닫았다는 증거다.** 두 세션이 같은 룸에서 같은 배치를 받지만
        /// 문 블록은 Runner 에게만 간다. 코덱 테스트는 필터 자체를 보고, 이것은 룸이 실제로
        /// 세션별로 인코딩하는지를 본다 — 한 번 인코딩해 전원에게 보내면 여기서 걸린다.
        [Fact]
        public void 문은_Runner_에게만_실린다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            Run(room, transport, 1);

            Assert.True(transport.TryLastObjectiveState(1, out var first, out var firstDoor, out _));
            Assert.True(transport.TryLastObjectiveState(2, out var second, out var secondDoor, out _));

            // 정확히 한쪽만 문을 받는다 — 이 룸의 Seeker 는 한 명이다.
            Assert.True(
                first.HasDoor != second.HasDoor,
                "두 세션이 같은 문 가시성을 받았다. 세션별 인코딩이 아니다.");

            // 문을 받은 쪽에는 좌표가 있고, 받지 않은 쪽은 0 이다.
            var runnerDoor = first.HasDoor ? firstDoor : secondDoor;
            var seekerDoor = first.HasDoor ? secondDoor : firstDoor;

            Assert.True(
                runnerDoor.X != 0 || runnerDoor.Z != 0,
                "Runner 사본의 문 좌표가 비어 있다.");

            Assert.Equal(0, seekerDoor.X);
            Assert.Equal(0, seekerDoor.Y);
            Assert.Equal(0, seekerDoor.Z);
        }

        [Fact]
        public void 열쇠와_장치는_양쪽_모두_받는다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            Run(room, transport, 1);

            Assert.True(transport.TryLastObjectiveState(1, out var first, out _, out var firstKeys));
            Assert.True(transport.TryLastObjectiveState(2, out var second, out _, out var secondKeys));

            // 룰셋 — 복도의 열쇠는 물리적 물건이고, Seeker 가 그것을 보는 것이 열쇠를
            // 지키는 전술을 만든다.
            Assert.Equal(firstKeys.Length, secondKeys.Length);
            Assert.Equal(first.DeviceCount, second.DeviceCount);
            Assert.True(first.HasAltar && second.HasAltar);
        }

        /// 5초 주기다. 2Hz 로 보내면 176B × 2 × 정원(5명) = 1.8KB/s 가 더 붙고 그만큼의 정보가 없다.
        [Fact]
        public void 목표물_전문은_매_틱_나가지_않는다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            Run(room, transport, 60);

            var count = transport.CountOfEvent(1, EventKind.ObjectiveState);

            // 60틱(2초)이면 시작 시 한 번뿐이어야 한다. 매 틱 보내면 60번이다.
            Assert.InRange(count, 1, 3);
        }

        [Fact]
        public void 로비로_되돌리면_목표물_전문이_멈춘다()
        {
            var room = RoomFixture.Create();

            RoomFixture.FillAndStart(room);
            room.PostCommand(RoomCommand.EndMatch(1, 1));
            room.Advance();
            room.PostCommand(RoomCommand.ReturnToLobby(1));
            room.Advance();

            var transport = new RecordingTransport();
            Run(room, transport, 200);

            Assert.False(room.Objectives.Placed);
            Assert.Equal(0, transport.CountOfEvent(1, EventKind.ObjectiveState));
        }

        // ==================================================== 스냅샷의 매치 플래그

        /// 기획서 §2 — 술래는 정확히 한 명이다. 스냅샷의 몸에 그것이 실려야 원격
        /// 클라이언트가 무기를 붙일지 판단할 수 있다.
        [Fact]
        public void 스냅샷에_Seeker_비트가_정확히_한_명에게_실린다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            Run(room, transport, 1);

            Assert.True(transport.TryLastSnapshot(1, out _, out var entities));
            Assert.Equal(2, entities.Length);

            var seekers = 0;
            foreach (var entity in entities)
            {
                if ((entity.Flags & EntityFlags.Seeker) != 0)
                {
                    seekers++;
                }
            }

            Assert.Equal(1, seekers);
        }

        /// 잠금을 클라이언트가 모르면 자기 입력으로 계속 예측하고 리컨실리에이션이 매 틱
        /// 되돌린다 — 증상은 잠긴 동안 화면이 떨리는 것이다.
        [Fact]
        public void 역할_공개_중에는_모든_몸에_Frozen_비트가_실린다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room, skipReveal: false);
            Run(room, transport, 1);

            Assert.Equal(MatchPhase.RoleReveal, room.MatchPhase);
            Assert.True(transport.TryLastSnapshot(1, out _, out var entities));

            foreach (var entity in entities)
            {
                Assert.True(
                    (entity.Flags & EntityFlags.Frozen) != 0,
                    $"플레이어 {entity.Id} 에 Frozen 이 없다.");
            }
        }

        [Fact]
        public void 역할_공개가_끝나면_Frozen_비트가_사라진다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            Run(room, transport, 1);

            Assert.Equal(MatchPhase.Playing, room.MatchPhase);
            Assert.True(transport.TryLastSnapshot(1, out _, out var entities));

            foreach (var entity in entities)
            {
                Assert.True((entity.Flags & EntityFlags.Frozen) == 0);
            }
        }

        /// 매치 비트가 이동 비트를 덮으면 원격 몸이 공중에 뜬 것으로 보인다.
        [Fact]
        public void 매치_비트가_이동_비트를_덮지_않는다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);

            // 바닥에 내려앉을 시간을 준다.
            Run(room, transport, 20);

            Assert.True(transport.TryLastSnapshot(1, out _, out var entities));

            foreach (var entity in entities)
            {
                Assert.True((entity.Flags & EntityFlags.Alive) != 0, "Alive 가 사라졌다.");
                Assert.True((entity.Flags & EntityFlags.OnGround) != 0, "OnGround 가 사라졌다.");
            }
        }

        /// 아직 서버가 세지 않는 비트는 나가지 않아야 한다. 0 이 아닌 값이 실리면
        /// 클라이언트가 있지도 않은 출혈을 그린다.
        [Fact]
        public void 아직_판정하지_않는_비트는_실리지_않는다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            Run(room, transport, 5);

            Assert.True(transport.TryLastSnapshot(1, out _, out var entities));

            foreach (var entity in entities)
            {
                Assert.True((entity.Flags & EntityFlags.Bleeding) == 0, "출혈은 IG-014 의 것이다.");
                Assert.True((entity.Flags & EntityFlags.Escaped) == 0, "탈출은 IG-012 의 것이다.");
            }
        }

        [Fact]
        public void 로비로_되돌리면_Seeker_비트도_사라진다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            Run(room, transport, 1);

            room.PostCommand(RoomCommand.EndMatch(1, 1));
            room.Advance();
            room.PostCommand(RoomCommand.ReturnToLobby(1));
            room.Advance();

            // 로비로 돌아오면 준비가 내려간다. 다시 누른다.
            RoomFixture.Ready(room, 2);

            room.PostCommand(RoomCommand.Start(1));
            RoomFixture.SkipReveal(room);

            var fresh = new RecordingTransport();
            Run(room, fresh, 1);

            // 두 번째 매치에서도 Seeker 는 정확히 한 명이다. 이전 매치의 비트가 남아
            // 있으면 두 명이 된다.
            Assert.True(fresh.TryLastSnapshot(1, out _, out var entities));

            var seekers = 0;
            foreach (var entity in entities)
            {
                if ((entity.Flags & EntityFlags.Seeker) != 0)
                {
                    seekers++;
                }
            }

            Assert.Equal(1, seekers);
        }
    }
}
