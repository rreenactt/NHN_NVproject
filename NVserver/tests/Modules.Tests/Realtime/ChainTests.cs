using System.Numerics;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 체인 견인(기획서 §4.3).
    ///
    /// **빈 탄창이 체인을 부르고, 체인만이 탄창을 채운다.** 그 고리가 Seeker 의 유일한 대가이므로,
    /// 여기서 검사하는 것은 시간이 아니라 그 인과다 — 탄창이 저절로 차는 길이 하나라도 생기면
    /// 세 발의 무게가 사라진다.
    ///
    /// 예전에는 이 규칙이 **클라이언트에만** 있었다. 서버는 탄을 줄이기만 하고 채우지 않았으므로
    /// 세 발을 쏜 Seeker 는 그 매치 내내 빈 총을 들고 다녔고, 클라이언트가 스스로 끌고 가려 해도
    /// 다음 스냅샷이 그 위치를 되돌렸다. 증상은 "체인이 안 끌고 가고 재장전도 안 된다" 였다.
    public class ChainTests
    {
        /// 탄창을 비우면 그 몸에 `Frozen` 이 실린다. 클라이언트는 이 비트로 조작을 막고
        /// 체인을 그린다.
        [Fact]
        public void 탄창을_비우면_체인에_걸린다()
        {
            var world = ChainWorld.Create();

            world.EmptyMagazine();

            Assert.Equal(0, world.SeekerAmmo());
            Assert.True(world.SeekerIsFrozen(), "탄창이 비었는데 Frozen 이 실리지 않았다.");
        }

        /// 견인은 제단 옆자리로 데려간다. **직선으로 간다** — 서버는 걸어가는 경로를 모르고,
        /// 규칙은 "얼마나 오래 묶여 있는가" 와 "어디에 놓이는가" 다.
        [Fact]
        public void 견인이_끝나면_제단_옆에_서_있다()
        {
            var world = ChainWorld.Create();
            var altar = world.Room.Objectives.AltarDragPoint;

            world.EmptyMagazine();
            world.RunUntilReleased();

            var landed = world.SeekerPosition();

            Assert.True(
                Vector3.Distance(landed, altar) < 0.01f,
                $"제단 옆({altar}) 이 아니라 {landed} 에 있다.");
        }

        /// 묶여 있는 시간은 기획서가 정한 창 안에 있다.
        ///
        /// **정확한 틱을 박지 않는다.** 견인 길이는 제단까지의 거리에서 나오므로 맵과 스폰에
        /// 따라 달라진다 — 고정하면 맵을 바꿀 때마다 무너지는 테스트가 된다. 규칙이 정한 것은
        /// 하한(0.45초)·상한(3.5초)과 그 뒤의 대기 3초다.
        [Fact]
        public void 묶여_있는_시간이_기획서의_창_안이다()
        {
            var world = ChainWorld.Create();

            world.EmptyMagazine();
            var ticks = world.RunUntilReleased();

            var least = (MatchConstants.ChainDragTime + MatchConstants.ChainWait) * SimConstants.TickRate;
            var most = (MatchConstants.ChainDragMaxTime + MatchConstants.ChainWait) * SimConstants.TickRate;

            // 탄창을 비우는 마지막 발과 해제 사이에 몇 틱의 여유가 있다. 창이 넉넉하므로
            // 상한에 그만큼을 더해 둔다.
            Assert.InRange(ticks, (int)least - 2, (int)most + 4);
        }

        /// 견인 중에 아무리 밀어도 도착지가 바뀌지 않는다. 그것이 벌칙의 내용이다.
        ///
        /// "제자리에 있는가" 로는 검사할 수 없다 — 묶인 몸은 **끌려가는 중이라 움직인다.**
        /// 검사할 것은 그 움직임이 입력과 무관하다는 것이고, 그 증거는 전력으로 밀어도
        /// 제단에 내려놓인다는 사실이다.
        [Fact]
        public void 체인에_걸린_동안_이동_입력이_먹지_않는다()
        {
            var world = ChainWorld.Create();
            var altar = world.Room.Objectives.AltarDragPoint;

            world.EmptyMagazine();

            // 풀릴 때까지 앞으로 전력으로 민다.
            var pushed = 0;
            while (world.SeekerIsFrozen() && pushed < 400)
            {
                world.PushForward(1);
                pushed++;
            }

            Assert.True(
                Vector3.Distance(world.SeekerPosition(), altar) < 0.01f,
                $"밀린 채로 {world.SeekerPosition()} 에 내려놓였다. 제단은 {altar} 다.");
        }

        /// **묶여 있는 내내 탄창은 비어 있다.** 3초가 벌칙의 값이고, 그 사이에 한 틱이라도
        /// 차면 벌칙이 사라진다 — 그래서 끝 한 점이 아니라 구간 전체를 본다.
        [Fact]
        public void 풀리기_전까지_탄창이_계속_비어_있다()
        {
            var world = ChainWorld.Create();

            world.EmptyMagazine();

            var ticks = 0;
            while (world.SeekerIsFrozen() && ticks < 400)
            {
                Assert.Equal(0, world.SeekerAmmo());
                world.Advance(1);
                ticks++;
            }

            Assert.True(ticks > 0, "체인에 걸리지도 않았다.");
        }

        /// 놓아주는 틱에 탄창이 찬다. 서버에서 탄창이 차는 곳은 여기와 매치 시작 둘뿐이다.
        [Fact]
        public void 대기가_끝나면_탄창이_차고_풀린다()
        {
            var world = ChainWorld.Create();

            world.EmptyMagazine();
            world.RunUntilReleased();

            Assert.Equal(MatchConstants.SeekerMagazine, world.SeekerAmmo());
            Assert.False(world.SeekerIsFrozen(), "탄창이 찼는데 Frozen 이 남아 있다.");
        }

        /// 찬 탄창이 **전문에 실려 나간다.** 클라이언트는 룸을 볼 수 없고 이 값만 읽으므로
        /// (`WeaponController.AcceptAmmo`), 여기서 끊기면 화면의 탄창은 영원히 0 이다.
        [Fact]
        public void 찬_탄창이_전문에_실린다()
        {
            var world = ChainWorld.Create();

            world.EmptyMagazine();
            world.RunUntilReleased();

            // 전문은 2Hz 다. 다음 주기까지 돌린다.
            world.Advance(SimConstants.TickRate);

            Assert.Equal(MatchConstants.SeekerMagazine, world.SeekerAmmoOnWire());
        }

        /// 풀리고 나면 다시 걸을 수 있다. 잠금이 남으면 Seeker 가 제단에 붙박인다.
        [Fact]
        public void 풀리면_다시_움직인다()
        {
            var world = ChainWorld.Create();

            world.EmptyMagazine();
            world.RunUntilReleased();

            var before = world.SeekerPosition();
            world.PushForward(20);

            Assert.True(
                Vector3.Distance(world.SeekerPosition(), before) > 0.5f,
                "체인이 풀렸는데 움직이지 못한다.");
        }

        /// 다시 세 발을 쏘면 다시 걸린다. 한 매치에 한 번이 아니다.
        [Fact]
        public void 다시_비우면_다시_걸린다()
        {
            var world = ChainWorld.Create();

            world.EmptyMagazine();
            world.RunUntilReleased();
            Assert.Equal(MatchConstants.SeekerMagazine, world.SeekerAmmo());

            world.EmptyMagazine();

            Assert.Equal(0, world.SeekerAmmo());
            Assert.True(world.SeekerIsFrozen(), "두 번째 빈 탄창이 체인을 부르지 않았다.");
        }

        /// 묶여 있는 동안에는 쏠 수 없다. 탄이 0 이므로 `FireWeapons` 가 이미 거르지만,
        /// 그 두 조건이 갈라지면(대기 중에 탄이 차는 순서로 바뀌면) 벌칙이 사라진다.
        [Fact]
        public void 체인에_걸린_동안_총이_나가지_않는다()
        {
            var world = ChainWorld.Create();

            world.EmptyMagazine();

            var before = world.FireEventCount();

            // 풀릴 때까지 트리거를 당긴 채로 둔다.
            var ticks = 0;
            while (world.SeekerIsFrozen() && ticks < 400)
            {
                world.PullTrigger(1);
                ticks++;
            }

            Assert.True(ticks > 0, "체인에 걸리지도 않았다.");
            Assert.Equal(before, world.FireEventCount());
        }

        /// 견인은 부드럽게 출발해 부드럽게 도착한다(SmoothStep) — 오프라인 견인과 같은 곡선이다.
        ///
        /// 등속이면 매 틱의 이동량이 같아 출발이 홱 당겨지고 도착이 뚝 멈춘다. 이징이면
        /// 첫 틱과 마지막 틱의 이동량이 중간보다 뚜렷이 작다 — 그것을 검사한다. 정확한
        /// 곡선 값을 박지 않는 이유는 묶는 시간의 테스트가 정확한 틱을 박지 않는 이유와
        /// 같다: 견인 틱 수가 맵과 스폰에 따라 달라진다.
        [Fact]
        public void 견인이_부드럽게_출발하고_부드럽게_도착한다()
        {
            var world = ChainWorld.Create();

            world.EmptyMagazine();

            var steps = world.MeasureDragSteps();

            Assert.True(steps.Count >= 3, $"견인이 {steps.Count}틱뿐이라 곡선을 잴 수 없다.");

            var first = steps[0];
            var last = steps[steps.Count - 1];
            var peak = 0f;
            foreach (var step in steps)
            {
                if (step > peak)
                {
                    peak = step;
                }
            }

            Assert.True(peak > first * 1.5f, $"출발 틱({first:F3}m)이 최고 속도({peak:F3}m)와 다르지 않다 — 등속이다.");
            Assert.True(peak > last * 1.5f, $"도착 틱({last:F3}m)이 최고 속도({peak:F3}m)와 다르지 않다 — 등속이다.");
        }

        /// 이징을 넣어도 견인은 **뒤로 가지 않는다.** 경로 위의 진행이 단조 증가여야
        /// 클라이언트 보간이 앞뒤로 출렁이지 않는다.
        [Fact]
        public void 견인이_뒤로_가지_않는다()
        {
            var world = ChainWorld.Create();

            world.EmptyMagazine();

            var steps = world.MeasureDragSteps();

            foreach (var step in steps)
            {
                Assert.True(step >= -0.0001f, $"견인 중에 {-step:F3}m 뒤로 갔다.");
            }
        }

        /// 격자가 없는 맵에는 제단이 없다. 끌고 갈 곳이 없으면 벌칙을 걸지 않는다 —
        /// 제자리에 3초 묶어 두는 것은 기획서가 정한 벌칙이 아니다.
        [Fact]
        public void 제단이_없는_맵에서는_걸리지_않는다()
        {
            var world = ChainWorld.Create(withGrid: false);

            world.EmptyMagazine();

            Assert.False(world.SeekerIsFrozen(), "배치가 없는 맵에서 체인이 걸렸다.");
        }

        private sealed class ChainWorld
        {
            private uint _inputTick;

            private ChainWorld(Room room, RecordingTransport transport, byte seeker)
            {
                Room = room;
                Transport = transport;
                Seeker = seeker;
            }

            public Room Room { get; }

            private RecordingTransport Transport { get; }

            private byte Seeker { get; }

            private int SeekerSession => Seeker + 1;

            public static ChainWorld Create(bool withGrid = true)
            {
                var room = RoomFixture.Create(withGrid: withGrid);
                var transport = new RecordingTransport();

                RoomFixture.FillAndStart(room);
                room.Broadcast(transport);

                Assert.True(transport.TryLastMatchState(1, out _, out var participants));

                byte seeker = 0;
                foreach (var participant in participants)
                {
                    if (participant.Role == MatchRole.Seeker)
                    {
                        seeker = participant.PlayerId;
                    }
                }

                return new ChainWorld(room, transport, seeker);
            }

            /// 탄창이 빌 때까지 쏜다. 연사 간격이 있으므로 **사이만** 띄운다.
            ///
            /// 마지막 발 뒤에는 돌리지 않는다. 체인은 그 발과 함께 시작하므로, 여기서 더
            /// 돌리면 호출한 쪽이 재는 시간이 그만큼 짧아진다 — 실제로 그것 때문에 견인
            /// 시간이 104틱인데 98틱으로 측정됐다.
            public void EmptyMagazine()
            {
                var gap = (int)System.MathF.Ceiling(MatchConstants.FireInterval * SimConstants.TickRate) + 1;

                for (var round = 0; round < MatchConstants.SeekerMagazine; round++)
                {
                    if (round > 0)
                    {
                        Advance(gap);
                    }

                    Fire();
                }
            }

            public void Fire()
            {
                Post(new InputFrame(ButtonFlags.Fire, 0, 0, 0, 0));
            }

            /// 트리거를 누른 채로 여러 틱 돌린다.
            public void PullTrigger(int ticks)
            {
                for (var tick = 0; tick < ticks; tick++)
                {
                    Post(new InputFrame(ButtonFlags.Fire, 0, 0, 0, 0));
                }
            }

            /// 앞으로 미는 입력을 여러 틱 넣는다.
            public void PushForward(int ticks)
            {
                for (var tick = 0; tick < ticks; tick++)
                {
                    Post(new InputFrame(ButtonFlags.None, 0, Quantization.ToFixedMoveAxis(1f), 0, 0));
                }
            }

            public void Advance(int ticks)
            {
                for (var tick = 0; tick < ticks; tick++)
                {
                    Room.Advance();
                    Room.Broadcast(Transport);
                }
            }

            /// 체인이 풀릴 때까지 돌리고 걸린 틱 수를 돌려준다.
            ///
            /// **틱 수를 세어서 앞질러 가지 않는다.** 견인 길이는 제단까지의 거리에서 나오므로
            /// 맵과 스폰 자리에 따라 달라진다 — 상한(3.5초)을 가정하고 그만큼 돌리면 이미
            /// 풀린 몸을 재게 되고, 그러면 무엇을 검사해도 통과하거나 엉뚱하게 실패한다.
            /// 실제로 이 파일의 첫 판이 그 실수를 했다.
            public int RunUntilReleased(int cap = 400)
            {
                var ticks = 0;

                while (SeekerIsFrozen() && ticks < cap)
                {
                    Advance(1);
                    ticks++;
                }

                Assert.True(ticks > 0, "체인에 걸리지도 않았다.");
                Assert.True(ticks < cap, $"{cap}틱이 지나도 체인이 풀리지 않았다.");

                return ticks;
            }

            /// 견인 구간의 틱별 진행량(m)을 잰다. 도착(경로 끝)까지 한 틱씩 돌리며,
            /// 진행은 **경로 위 사영**으로 계산한다 — 경로가 꺾이면 직선거리로는 앞뒤를
            /// 구분할 수 없다.
            public System.Collections.Generic.List<float> MeasureDragSteps(int cap = 400)
            {
                var route = SeekerChainRoute();
                Assert.True(route.Count >= 2, "체인 경로가 없다 — 체인에 걸리지 않았다.");

                var target = route[route.Count - 1];
                var steps = new System.Collections.Generic.List<float>();
                var previous = ProgressAlongRoute(route, SeekerPosition());

                var ticks = 0;
                while (Vector3.Distance(SeekerPosition(), target) > 0.001f && ticks < cap)
                {
                    Advance(1);
                    var progress = ProgressAlongRoute(route, SeekerPosition());
                    steps.Add(progress - previous);
                    previous = progress;
                    ticks++;
                }

                Assert.True(ticks < cap, $"{cap}틱이 지나도 견인이 도착하지 않았다.");
                return steps;
            }

            /// 점을 경로에 사영해 시작점부터의 경로 거리로 바꾼다.
            private static float ProgressAlongRoute(System.Collections.Generic.List<Vector3> route, Vector3 point)
            {
                var best = 0f;
                var bestDistance = float.MaxValue;
                var arc = 0f;

                for (var index = 1; index < route.Count; index++)
                {
                    var from = route[index - 1];
                    var to = route[index];
                    var segment = to - from;
                    var lengthSquared = segment.LengthSquared();
                    var t = lengthSquared < 1e-8f
                        ? 0f
                        : System.Math.Clamp(Vector3.Dot(point - from, segment) / lengthSquared, 0f, 1f);

                    var closest = from + segment * t;
                    var distance = Vector3.DistanceSquared(point, closest);
                    var segmentLength = System.MathF.Sqrt(lengthSquared);

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = arc + segmentLength * t;
                    }

                    arc += segmentLength;
                }

                return best;
            }

            private System.Collections.Generic.List<Vector3> SeekerChainRoute()
            {
                foreach (var player in Room.Players)
                {
                    if (player.PlayerId == Seeker)
                    {
                        return player.ChainRoute;
                    }
                }

                Assert.Fail("룸에 Seeker 가 없다.");
                return null;
            }

            /// 지금 이 순간의 탄창. **전문이 아니라 룸에서 읽는다.**
            ///
            /// 전문은 2Hz 라 최대 15틱 낡을 수 있다. 해제는 특정 틱에 일어나므로, 전문으로
            /// 재면 "풀렸는데 탄창이 0" 이라는 존재하지 않는 순간이 보인다 — 실제로 그렇게
            /// 실패했다. 여기서 검사하는 것은 서버의 판정이고, 그것은 룸에 있다.
            public int SeekerAmmo()
            {
                foreach (var player in Room.Players)
                {
                    if (player.PlayerId == Seeker)
                    {
                        return player.Ammo;
                    }
                }

                Assert.Fail("룸에 Seeker 가 없다.");
                return -1;
            }

            /// 전문에 실려 나간 탄창. 클라이언트가 실제로 읽는 값이다.
            public int SeekerAmmoOnWire()
            {
                Assert.True(Transport.TryLastMatchState(SeekerSession, out _, out var participants));

                foreach (var participant in participants)
                {
                    if (participant.PlayerId == Seeker)
                    {
                        return participant.Ammo;
                    }
                }

                Assert.Fail("명단에 Seeker 가 없다.");
                return -1;
            }

            public bool SeekerIsFrozen()
            {
                foreach (var player in Room.Players)
                {
                    if (player.PlayerId == Seeker)
                    {
                        return (player.Wire.Flags & EntityFlags.Frozen) != 0;
                    }
                }

                Assert.Fail("룸에 Seeker 가 없다.");
                return false;
            }

            public Vector3 SeekerPosition()
            {
                foreach (var player in Room.Players)
                {
                    if (player.PlayerId == Seeker)
                    {
                        return player.State.Position;
                    }
                }

                Assert.Fail("룸에 Seeker 가 없다.");
                return default;
            }

            public int FireEventCount() => Transport.CountOfEvent(SeekerSession, EventKind.FireEvent);

            private void Post(in InputFrame frame)
            {
                _inputTick++;
                Room.PostInput(SeekerSession, _inputTick, frame);
                Room.Advance();
                Room.Broadcast(Transport);
            }
        }
    }
}
