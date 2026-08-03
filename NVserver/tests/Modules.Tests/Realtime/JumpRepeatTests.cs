using NV.Realtime;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 입력 반복이 점프를 두 번 만들지 않는가(IG-025).
    ///
    /// **이 태스크는 결함을 고치려고 열렸고, 결함이 없다는 것을 확인하며 닫혔다.** IG-012b2 에서
    /// `Jump` 도 엣지인데 `LastInput` 에 남아 반복된다는 것을 발견하고 "착지 순간에 재점프가
    /// 성립한다" 고 적었는데, 수치를 보면 성립할 수 없다 — 반복 구간이 체공 시간보다 훨씬 짧다.
    ///
    /// 그래서 여기서 하는 일은 **그 관계를 테스트로 고정하는 것**이다. 상호작용처럼 비트를
    /// 지우는 대신 그렇게 한 이유는 아래 `반복이_공중에서_끝난다` 에 적었다.
    public class JumpRepeatTests
    {
        /// 체공 시간(초). 위로 `JumpSpeed`, 중력 `Gravity` 인 포물선이므로 `2v/g` 다.
        private const float AirTimeSeconds = 2f * SimConstants.JumpSpeed / SimConstants.Gravity;

        /// **이것이 재점프가 불가능한 이유다.** 반복은 입력이 끊긴 뒤 최대
        /// `MaxInputRepeatTicks` 만 산다(그 뒤에는 `InputValidator.Neutral` 이 버튼을 비운다).
        /// 그 구간이 체공보다 짧으면 반복된 `Jump` 는 **전부 공중에서** 소비되고,
        /// `PlayerMovement.Step` 이 접지 검사로 걸러낸다.
        ///
        /// 이 관계가 깨지면(예: 지연 보상을 위해 반복 상한을 30틱으로 올리면) 착지 시점이
        /// 반복 구간 안으로 들어와 **한 번의 키 입력이 연속 점프가 된다.** 그때는
        /// `InputValidator.WithoutEdgeButtons` 에 `Jump` 를 더해야 하고, 이 테스트가 그 순간을
        /// 알려 준다.
        [Fact]
        public void 반복이_공중에서_끝난다()
        {
            var repeatSeconds = RealtimeConstants.Rooms.MaxInputRepeatTicks * SimConstants.TickDelta;

            Assert.True(
                repeatSeconds < AirTimeSeconds,
                $"입력 반복 {repeatSeconds}초가 체공 {AirTimeSeconds}초보다 길다. "
                + "반복된 Jump 가 착지 시점에 살아 있으므로 `WithoutEdgeButtons` 에 Jump 를 더해야 한다.");
        }

        /// 한 번의 `Jump` 입력이 정확히 한 번의 점프가 된다.
        ///
        /// 입력을 한 틱만 주고 그 뒤로는 아무것도 보내지 않는다 — 반복 갈래가 `Jump` 를 실어
        /// 나른다면 착지 후에 두 번째 상승이 나타난다.
        [Fact]
        public void 입력_한_번은_점프_한_번이다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            room.Broadcast(transport);

            var player = FirstPlayer(room);

            // 접지 상태에서 시작해야 점프가 성립한다.
            room.Advance();
            Assert.True((player.State.Flags & EntityFlags.OnGround) != 0);

            room.PostInput(player.SessionId, 1u, new InputFrame(ButtonFlags.Jump, 0, 0, 0, 0));

            // 체공의 두 배를 돌린다. 착지하고도 한참 남는다.
            var ticks = (int)(AirTimeSeconds * SimConstants.TickRate * 2f);
            var ascents = 0;
            var wasRising = false;

            for (var tick = 0; tick < ticks; tick++)
            {
                room.Advance();

                var rising = player.State.Velocity.Y > 0.5f;

                if (rising && !wasRising)
                {
                    ascents++;
                }

                wasRising = rising;
            }

            Assert.Equal(1, ascents);
        }

        private static PlayerEntity FirstPlayer(Room room)
        {
            foreach (var player in room.Players)
            {
                return player;
            }

            Assert.Fail("룸에 플레이어가 없다.");
            return null!;
        }
    }
}
