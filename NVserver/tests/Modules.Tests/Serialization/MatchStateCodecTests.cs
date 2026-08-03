using System;
using NV.Realtime;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using Xunit;

namespace NV.Modules.Tests.Serialization
{
    /// 매치 상태 전문의 인코딩. **역할별 필터가 이 테스트의 핵심이다.**
    ///
    /// 룰셋은 Seeker 에게 열쇠 진행도를 알리지 않는다. 그것을 클라이언트에서 숨기는
    /// 방식으로는 지킬 수 없으므로(WebGL 빌드는 디컴파일된다) 와이어에서 지워야 하고,
    /// 지워졌는지는 **바이트를 직접 봐서** 확인해야 한다.
    public class MatchStateCodecTests
    {
        private static MatchParticipant[] Participants()
        {
            return new[]
            {
                new MatchParticipant(0, MatchRole.Seeker, 0, 0, 0),
                new MatchParticipant(1, MatchRole.Runner, 1, 1, 4),
                new MatchParticipant(2, MatchRole.Runner, 0, 0, 3),
            };
        }

        private static MatchStateHeader Header(byte keysInserted = 7, byte escapes = 1)
        {
            return new MatchStateHeader(
                MatchPhase.Playing,
                MatchStateHeader.ToTenths(123.4f),
                keysInserted,
                escapes,
                0,
                3);
        }

        private static byte[] Write(MatchRole forRole)
        {
            var buffer = new byte[MessageCodec.MatchStateMaxWireSize(RealtimeConstants.Rooms.MaxPlayers)];
            var participants = Participants();

            var length = MessageCodec.WriteMatchState(buffer, Header(), participants, forRole);

            var result = new byte[length];
            Array.Copy(buffer, result, length);
            return result;
        }

        [Fact]
        public void Runner_사본은_왕복해서_같은_값이_된다()
        {
            var payload = Write(MatchRole.Runner);
            var read = new MatchParticipant[RealtimeConstants.Rooms.MaxPlayers];

            var count = MessageCodec.ReadMatchState(payload, out var header, read);

            Assert.Equal(3, count);
            Assert.Equal(MatchPhase.Playing, header.Phase);
            Assert.Equal(7, header.KeysInserted);
            Assert.Equal(1, header.Escapes);
            Assert.Equal(0, header.Outcome);
            Assert.Equal(123.4f, MatchStateHeader.FromTenths(header.TimeRemainingTenths), 1);

            var expected = Participants();
            for (var index = 0; index < count; index++)
            {
                Assert.Equal(expected[index].PlayerId, read[index].PlayerId);
                Assert.Equal(expected[index].Role, read[index].Role);
                Assert.Equal(expected[index].Flags, read[index].Flags);
                Assert.Equal(expected[index].Hits, read[index].Hits);
                Assert.Equal(expected[index].CarriedKeys, read[index].CarriedKeys);
            }
        }

        /// **Seeker 사본에 열쇠 진행도가 실리지 않는다.** 읽어서 확인하는 것으로는
        /// 부족하지 않다 — 읽기가 쓰기의 역이므로 0 이 나오면 와이어에 0 이 있다.
        [Fact]
        public void Seeker_사본에는_삽입된_열쇠가_실리지_않는다()
        {
            var payload = Write(MatchRole.Seeker);
            var read = new MatchParticipant[RealtimeConstants.Rooms.MaxPlayers];

            MessageCodec.ReadMatchState(payload, out var header, read);

            Assert.Equal(0, header.KeysInserted);
        }

        [Fact]
        public void Seeker_사본에는_남의_소지_열쇠가_실리지_않는다()
        {
            var payload = Write(MatchRole.Seeker);
            var read = new MatchParticipant[RealtimeConstants.Rooms.MaxPlayers];

            var count = MessageCodec.ReadMatchState(payload, out _, read);

            for (var index = 0; index < count; index++)
            {
                Assert.Equal(0, read[index].CarriedKeys);
            }
        }

        /// 바이트를 직접 비교한다. 열쇠 수 7 과 소지 4·3 이 어디에도 남아 있지 않아야
        /// 한다 — 다른 필드에 우연히 같은 값이 있을 수 있으므로 위치로 확인한다.
        [Fact]
        public void 두_사본의_바이트가_열쇠_자리에서만_다르다()
        {
            var runner = Write(MatchRole.Runner);
            var seeker = Write(MatchRole.Seeker);

            Assert.Equal(runner.Length, seeker.Length);

            // 헤더의 keysInserted 는 opcode(1)+kind(1)+phase(1)+time(2) 다음이다.
            const int keysInsertedOffset = 5;
            Assert.Equal(7, runner[keysInsertedOffset]);
            Assert.Equal(0, seeker[keysInsertedOffset]);

            // 참가자마다 carriedKeys 는 항목의 마지막 바이트다.
            for (var index = 0; index < 3; index++)
            {
                var carriedOffset = MatchStateHeader.WireSize
                    + (index * MatchParticipant.WireSize)
                    + 4;

                Assert.Equal(0, seeker[carriedOffset]);
            }

            // 그 밖의 바이트는 전부 같아야 한다. 필터가 다른 값을 건드리면 여기서 걸린다.
            for (var offset = 0; offset < runner.Length; offset++)
            {
                var isKeysInserted = offset == keysInsertedOffset;
                var isCarried = offset >= MatchStateHeader.WireSize
                    && (offset - MatchStateHeader.WireSize) % MatchParticipant.WireSize == 4;

                if (isKeysInserted || isCarried)
                {
                    continue;
                }

                Assert.Equal(runner[offset], seeker[offset]);
            }
        }

        /// 탈출 수는 걸러내지 않는다. Seeker 가 막아야 하는 수이므로 알아야 한다.
        [Fact]
        public void Seeker_도_탈출_수는_받는다()
        {
            var payload = Write(MatchRole.Seeker);
            var read = new MatchParticipant[RealtimeConstants.Rooms.MaxPlayers];

            MessageCodec.ReadMatchState(payload, out var header, read);

            Assert.Equal(1, header.Escapes);
        }

        /// 역할은 Seeker 에게도 그대로 간다. 자기가 술래인 것을 알아야 한다.
        [Fact]
        public void 역할은_양쪽_사본에_그대로_실린다()
        {
            var payload = Write(MatchRole.Seeker);
            var read = new MatchParticipant[RealtimeConstants.Rooms.MaxPlayers];

            MessageCodec.ReadMatchState(payload, out _, read);

            Assert.Equal(MatchRole.Seeker, read[0].Role);
            Assert.Equal(MatchRole.Runner, read[1].Role);
        }

        [Fact]
        public void 참가자_수가_헤더와_다르면_거절한다()
        {
            var buffer = new byte[64];
            var header = new MatchStateHeader(MatchPhase.Playing, 0, 0, 0, 0, 3);

            Assert.Throws<ArgumentException>(() => MessageCodec.WriteMatchState(
                buffer,
                header,
                new ReadOnlySpan<MatchParticipant>(new MatchParticipant[2]),
                MatchRole.Runner));
        }

        [Fact]
        public void 룸_상태_전문을_매치_전문으로_읽지_않는다()
        {
            var buffer = new byte[MessageCodec.RoomStateMaxWireSize(RealtimeConstants.Rooms.MaxPlayers)];
            var roomHeader = new RoomStateHeader(RoomPhase.Playing, 0, 0, 0, 1u, 42, 0);

            var length = MessageCodec.WriteRoomState(
                buffer,
                roomHeader,
                ReadOnlySpan<RoomPlayerEntry>.Empty);

            var payload = new ReadOnlySpan<byte>(buffer, 0, length).ToArray();

            Assert.Throws<InvalidOperationException>(() => MessageCodec.ReadMatchState(
                payload,
                out _,
                new MatchParticipant[8]));
        }

        /// 종류를 먼저 볼 수 있어야 수신 측이 본문을 파싱하기 전에 가를 수 있다.
        [Fact]
        public void 전문의_종류를_본문_파싱_없이_알_수_있다()
        {
            Assert.Equal(EventKind.MatchState, MessageCodec.ReadEventKind(Write(MatchRole.Runner)));

            var buffer = new byte[MessageCodec.RoomStateMaxWireSize(RealtimeConstants.Rooms.MaxPlayers)];
            var length = MessageCodec.WriteRoomState(
                buffer,
                new RoomStateHeader(RoomPhase.Waiting, 0, 0, 0, 0u, 1, 0),
                ReadOnlySpan<RoomPlayerEntry>.Empty);

            Assert.Equal(
                EventKind.RoomState,
                MessageCodec.ReadEventKind(new ReadOnlySpan<byte>(buffer, 0, length)));
        }

        [Fact]
        public void Event_가_아닌_프레임은_종류가_None_이다()
        {
            var buffer = new byte[WelcomeMessage.WireSize];
            var length = MessageCodec.WriteWelcome(
                buffer,
                new WelcomeMessage(ProtocolInfo.Version, 0, 1u, 0u, 30));

            Assert.Equal(
                EventKind.None,
                MessageCodec.ReadEventKind(new ReadOnlySpan<byte>(buffer, 0, length)));

            Assert.Equal(EventKind.None, MessageCodec.ReadEventKind(ReadOnlySpan<byte>.Empty));
        }

        // ==================================================== 시간 환산

        [Theory]
        [InlineData(0f, 0)]
        [InlineData(0.05f, 0)]
        [InlineData(1f, 10)]
        [InlineData(123.4f, 1234)]
        [InlineData(480f, 4800)]
        public void 초가_0_1초_단위로_환산된다(float seconds, int expected)
        {
            Assert.Equal((ushort)expected, MatchStateHeader.ToTenths(seconds));
        }

        [Fact]
        public void 음수_시간은_0_이_된다()
        {
            Assert.Equal(0, MatchStateHeader.ToTenths(-5f));
        }

        /// 감싸 올리면 6553초가 0 이 되어 "시간 종료" 로 보인다. 자르는 편이 낫다.
        [Fact]
        public void 상한을_넘는_시간은_감싸지_않고_잘린다()
        {
            Assert.Equal(ushort.MaxValue, MatchStateHeader.ToTenths(100_000f));
        }

        /// 매치 길이 480초가 u16 안에 넉넉히 들어간다. 장치가 시간을 더해도 남는다.
        [Fact]
        public void 매치_길이가_u16_안에_들어간다()
        {
            Assert.True(MatchStateHeader.ToTenths(NV.Shared.Simulation.MatchConstants.MatchDuration) < ushort.MaxValue);
        }
    }
}
