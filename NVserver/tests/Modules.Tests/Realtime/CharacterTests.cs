using NV.Realtime.Simulation;
using NV.Shared.Contracts.Messages;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 캐릭터는 방 안에서 유일하다.
    ///
    /// 서버가 아는 것은 **번호와 개수**뿐이다. 이름·색·에셋은 클라이언트의 표이며 서버가
    /// 알면 그 표가 두 곳에 생긴다. 그래서 여기 있는 판정은 둘이다 — 범위 안인가, 남이
    /// 입고 있지 않은가.
    public class CharacterTests
    {
        [Fact]
        public void 빈_캐릭터로_바꿀_수_있다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.Advance();

            var before = CharacterOf(room, 1);
            var target = (byte)(before + 1);

            room.PostCommand(RoomCommand.SetCharacter(1, target));
            room.Advance();

            Assert.Equal(target, CharacterOf(room, 1));
        }

        /// 두 클라이언트가 같은 틱에 같은 캐릭터를 고를 수 있고, 하나만 입을 수 있다.
        /// 먼저 처리된 쪽이 갖는다 — 슬롯 다툼과 같은 규칙이다.
        [Fact]
        public void 남이_입은_캐릭터는_거부된다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1));
            room.Advance();

            var mine = CharacterOf(room, 1);
            var theirs = CharacterOf(room, 2);

            Assert.NotEqual(mine, theirs);

            room.PostCommand(RoomCommand.SetCharacter(2, mine));
            room.Advance();

            // 요청한 쪽은 그대로 두고, 원래 주인도 그대로다.
            Assert.Equal(theirs, CharacterOf(room, 2));
            Assert.Equal(mine, CharacterOf(room, 1));
        }

        [Fact]
        public void 같은_틱의_같은_요청은_먼저_처리된_쪽이_갖는다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1));
            room.Advance();

            // 아무도 입지 않은 번호를 고른다. 정원 2 이므로 0·1 만 쓰이고 2 는 비어 있다.
            const byte free = 2;

            room.PostCommand(RoomCommand.SetCharacter(1, free));
            room.PostCommand(RoomCommand.SetCharacter(2, free));
            room.Advance();

            Assert.Equal(free, CharacterOf(room, 1));
            Assert.NotEqual(free, CharacterOf(room, 2));
        }

        [Fact]
        public void 범위를_벗어난_번호는_거부된다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.Advance();

            var before = CharacterOf(room, 1);

            room.PostCommand(RoomCommand.SetCharacter(1, ProtocolInfo.LobbyCharacterCount));
            room.PostCommand(RoomCommand.SetCharacter(1, RoomPlayerEntry.NoCharacter));
            room.Advance();

            Assert.Equal(before, CharacterOf(room, 1));
        }

        /// 매치 중에 외형이 바뀌면 원격 몸이 매치 중간에 갈아입는다.
        [Fact]
        public void 매치_중의_변경은_무시된다()
        {
            var room = RoomFixture.Create();

            RoomFixture.FillAndStart(room);

            var before = CharacterOf(room, 1);

            room.PostCommand(RoomCommand.SetCharacter(1, (byte)(before + 4)));
            room.Advance();

            Assert.Equal(before, CharacterOf(room, 1));
        }

        [Fact]
        public void 참가하지_않은_세션의_변경은_무시된다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.SetCharacter(99, 3));
            room.Advance();

            // 3 은 아무도 입지 않은 채로 남아야 한다.
            Assert.NotEqual(3, CharacterOf(room, 1));
        }

        /// 나간 사람의 캐릭터는 다시 고를 수 있어야 한다. 잠긴 채로 남으면 여덟 번의
        /// 입·퇴장으로 방에서 고를 수 있는 캐릭터가 사라진다.
        [Fact]
        public void 나간_사람의_캐릭터는_다시_비어_있다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1));
            room.Advance();

            var theirs = CharacterOf(room, 2);

            room.PostCommand(RoomCommand.Leave(2, 1));
            room.Advance();

            room.PostCommand(RoomCommand.SetCharacter(1, theirs));
            room.Advance();

            Assert.Equal(theirs, CharacterOf(room, 1));
        }

        /// 명단 전문에서 이 세션의 캐릭터를 읽는다.
        private static byte CharacterOf(Room room, int sessionId)
        {
            return RoomFixture.EntryOf(room, sessionId).CharacterId;
        }
    }
}
