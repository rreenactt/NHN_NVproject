using System.Collections.Generic;
using System.Numerics;
using NV.Realtime.Contracts;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 목표를 수행하는 봇. Runner 는 열쇠를 모아 문을 열고 빠져나가며, Seeker 는 쫓고 쏜다.
    ///
    /// **열쇠와 문을 손으로 놓는다.** 배치는 `ObjectivePlacementTests` 가 덮으므로, 여기서
    /// 그것에 기대면 실패했을 때 "봇이 못 갔다" 와 "배치가 달랐다" 를 구별할 수 없다.
    ///
    /// 픽스처의 맵은 벽 하나뿐인 열린 방이다. 경로 탐색이 없는 봇이 목표에 도달할 수 있는
    /// 유일한 지형이며, 그것이 이 단계의 알려진 한계다(계획서 §5 Phase 3).
    public class BotObjectiveTests
    {
        /// 벽(x 5~6)의 왼쪽에 있는 셀 중심들. 격자는 6×6, 셀 4m, 원점 -12 이므로
        /// 중심은 -10·-6·-2·2·6·10 이고 x 6 열은 벽이 지나가 통행 불가다.
        private static readonly float[] SafeX = { -10f, -6f, -2f, 2f };

        private static readonly float[] SafeZ = { -10f, -6f, -2f, 2f, 6f, 10f };

        [Fact]
        public void Runner_봇이_열쇠로_걸어가_줍는다()
        {
            var room = Playing(BotRolePreference.Runner);
            var bot = TheBot(room);

            PlaceKeys(room, new[] { new Vector3(-10f, 0f, -10f) });
            room.Objectives.SetDoor(new Vector3(2f, 0f, 10f), 0f);

            AdvanceUntil(room, 900, () => bot.CarriedKeys > 0);

            Assert.Equal(1, bot.CarriedKeys);
            Assert.Empty(room.Objectives.Keys);
        }

        [Fact]
        public void Runner_봇이_문에_열쇠를_넣는다()
        {
            var room = Playing(BotRolePreference.Runner);

            PlaceKeys(room, new[] { new Vector3(-10f, 0f, -10f) });
            room.Objectives.SetDoor(new Vector3(2f, 0f, 10f), 0f);

            AdvanceUntil(room, 1800, () => room.Match.KeysInserted > 0);

            Assert.Equal(1, room.Match.KeysInserted);
        }

        [Fact]
        public void 문이_열려_있으면_Runner_봇이_빠져나간다()
        {
            var room = Playing(BotRolePreference.Runner);

            PlaceKeys(room, System.Array.Empty<Vector3>());
            room.Objectives.SetDoor(new Vector3(-10f, 0f, -10f), 0f);
            OpenDoor(room);

            AdvanceUntil(room, 1800, () => room.Match.Escapes > 0);

            Assert.Equal(1, room.Match.Escapes);
        }

        [Fact]
        public void Seeker_봇이_Runner_를_쏜다()
        {
            // 봇이 술래이고 사람이 도망치는 쪽이다. 사람은 입력을 보내지 않으므로
            // 스폰에 서 있는 표적이 된다.
            var room = Playing(BotRolePreference.Seeker);
            var human = TheHuman(room);

            PlaceKeys(room, System.Array.Empty<Vector3>());

            AdvanceUntil(room, 900, () => human.Hits > 0);

            Assert.True(human.Hits > 0, "술래 봇이 사람을 맞히지 못했다.");
        }

        [Fact]
        public void Seeker_봇은_벽_뒤의_Runner_를_쏘지_않는다()
        {
            // 시선이 막힌 채로 쏘면 탄창 3발이 벽에 사라진다. 룸은 그 발사를 정당한 것으로
            // 받아들이므로(사람도 벽을 쏠 수 있다) 걸러야 하는 쪽은 두뇌다.
            var room = Playing(BotRolePreference.Seeker);
            var bot = TheBot(room);
            var human = TheHuman(room);

            PlaceKeys(room, System.Array.Empty<Vector3>());

            // 벽(x 5~6)을 사이에 둔다. 봇은 오른쪽, 사람은 왼쪽이다.
            bot.State.Position = new Vector3(9f, 0f, 0f);
            human.State.Position = new Vector3(0f, 0f, 0f);

            // 봇이 벽을 돌아 나오기 전까지만 본다. 오래 돌리면 결국 시선이 열리고,
            // 그때 쏘는 것은 옳은 동작이다.
            for (var tick = 0; tick < 20; tick++)
            {
                room.Advance();
            }

            Assert.Equal(MatchConstants.SeekerMagazine, bot.Ammo);
            Assert.Equal(0, human.Hits);
        }

        [Fact]
        public void 술래_봇은_쓰러진_몸을_쫓지_않는다()
        {
            // 쓰러진 몸을 표적으로 세면 술래가 시체 앞에서 탄창을 비운다.
            var room = Playing(BotRolePreference.Seeker);
            var bot = TheBot(room);
            var human = TheHuman(room);

            PlaceKeys(room, System.Array.Empty<Vector3>());

            human.Downed = true;
            human.State.Position = new Vector3(0f, 0f, 0f);
            bot.State.Position = new Vector3(0f, 0f, 3f);

            for (var tick = 0; tick < 30; tick++)
            {
                room.Advance();
            }

            Assert.Equal(MatchConstants.SeekerMagazine, bot.Ammo);
        }

        /// 이 단계의 목적 전체. **봇 하나가 열쇠 10개를 넣고 문을 열고 빠져나간다.**
        ///
        /// 사람은 술래로 서 있기만 한다(입력을 보내지 않으므로 쏘지 않는다). 그래서 이
        /// 검사는 Runner 쪽 규칙 전부를 한 번에 지나간다 — 습득·삽입·개방·탈출.
        [Fact]
        public void 봇_하나가_문을_열고_빠져나간다()
        {
            var room = Playing(BotRolePreference.Runner);

            PlaceKeys(room, KeyRing(MatchConstants.KeysRequired));
            room.Objectives.SetDoor(new Vector3(-10f, 0f, -10f), 0f);

            AdvanceUntil(room, 12_000, () => room.Match.DoorOpen);
            Assert.Equal(MatchConstants.KeysRequired, room.Match.KeysInserted);

            AdvanceUntil(room, 1800, () => room.Match.Escapes > 0);
            Assert.Equal(1, room.Match.Escapes);
        }

        /// 사람 하나 + 목표 수행 봇 하나로 리빌까지 지나간 룸.
        private static Room Playing(BotRolePreference role)
        {
            var room = RoomFixture.WithBots(role: role, behavior: BotBehavior.Objective, seed: 20260804u);

            RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.SettleBots(room);

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();
            RoomFixture.SkipReveal(room);

            return room;
        }

        /// 맵의 열쇠를 이 목록으로 바꾼다. 배치가 놓은 것은 지운다.
        private static void PlaceKeys(Room room, IReadOnlyList<Vector3> keys)
        {
            // 뒤에서부터 지운다. `RemoveKeyAt` 이 목록을 당기므로 앞에서부터 지우면 건너뛴다.
            for (var index = room.Objectives.Keys.Count - 1; index >= 0; index--)
            {
                room.Objectives.RemoveKeyAt(index);
            }

            foreach (var key in keys)
            {
                room.Objectives.AddKey(key);
            }
        }

        /// 통행 가능한 셀 중심에서 열쇠 자리 `count` 개를 고른다.
        ///
        /// 문 자리(-10, -10)는 비운다. 열쇠가 문 위에 있으면 봇이 삽입하러 간 자리에서
        /// 그것을 주워, 한 번의 왕복이 두 개를 처리한다 — 검사가 그만큼 헐거워진다.
        private static List<Vector3> KeyRing(int count)
        {
            var keys = new List<Vector3>(count);

            foreach (var x in SafeX)
            {
                foreach (var z in SafeZ)
                {
                    if (keys.Count == count)
                    {
                        return keys;
                    }

                    if (x == -10f && z == -10f)
                    {
                        continue;
                    }

                    keys.Add(new Vector3(x, 0f, z));
                }
            }

            Assert.Equal(count, keys.Count);
            return keys;
        }

        /// 문을 열어 둔다. 열쇠를 실제로 넣는 경로는 다른 검사가 덮는다.
        private static void OpenDoor(Room room)
        {
            for (var index = 0; index < MatchConstants.KeysRequired; index++)
            {
                room.Match.InsertKey();
            }

            Assert.True(room.Match.DoorOpen);
        }

        /// 조건이 참이 될 때까지 돌린다. 상한을 두는 이유는 멈추는 대신 실패해야 하기 때문이다.
        private static void AdvanceUntil(Room room, int maxTicks, System.Func<bool> done)
        {
            for (var tick = 0; tick < maxTicks && !done(); tick++)
            {
                room.Advance();
            }
        }

        private static PlayerEntity TheBot(Room room)
        {
            return Single(room, bot: true);
        }

        private static PlayerEntity TheHuman(Room room)
        {
            return Single(room, bot: false);
        }

        private static PlayerEntity Single(Room room, bool bot)
        {
            PlayerEntity? found = null;

            foreach (var player in room.Players)
            {
                if (player.IsBot != bot)
                {
                    continue;
                }

                Assert.Null(found);
                found = player;
            }

            Assert.NotNull(found);
            return found!;
        }
    }
}
