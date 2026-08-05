using System;
using NV.Realtime;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Serialization
{
    /// 목표물 전문의 인코딩. **문 블록이 Seeker 사본에서 사라지는지가 핵심이다.**
    ///
    /// 이 필터가 이 이관 작업의 원래 목적이다. 씨드를 공유해 양쪽이 같은 배치를 계산하는
    /// 방식으로는 문 좌표가 Seeker 의 프로세스에 도달하는 것을 막을 수 없었고, 컬링 레이어는
    /// 화면에서 가릴 뿐이다. 값을 0 으로 채우는 것도 부족하다 — 그것도 "문이 있다" 는 사실과
    /// 블록 크기를 알려 준다. **블록 자체가 없어야 한다.**
    public class ObjectiveStateCodecTests
    {
        private const int MaxKeys = 16;
        private const int MaxDevices = 9;

        private static readonly ObjectivePoint Altar = new(100, 0, 200);
        private static readonly ObjectivePoint AltarDrag = new(150, 0, 200);
        private static readonly ObjectivePoint Door = new(-3000, 640, 2500);
        private const ushort DoorYaw = 12345;

        private static ObjectivePoint[] Keys()
        {
            return new[]
            {
                new ObjectivePoint(10, 0, 20),
                new ObjectivePoint(30, 0, 40),
                new ObjectivePoint(-50, 640, 60),
            };
        }

        private static ObjectiveDevice[] Devices()
        {
            return new[]
            {
                new ObjectiveDevice(MatchDeviceType.AddTime, 1, 0, 2, 100, 0),
                new ObjectiveDevice(MatchDeviceType.Teleport, 3, 640, 4, 200, 0),
            };
        }

        private static byte[] Write(MatchRole forRole, bool doorOpen = false)
        {
            var buffer = new byte[MessageCodec.ObjectiveStateMaxWireSize(MaxKeys, MaxDevices)];
            var keys = Keys();
            var devices = Devices();

            var header = new ObjectiveStateHeader(
                ObjectiveFlags.HasAltar | ObjectiveFlags.HasDoor,
                (byte)keys.Length,
                (byte)devices.Length);

            var length = MessageCodec.WriteObjectiveState(
                buffer,
                header,
                Altar,
                AltarDrag,
                Door,
                DoorYaw,
                doorOpen,
                keys,
                devices,
                forRole);

            var result = new byte[length];
            Array.Copy(buffer, result, length);
            return result;
        }

        private static ObjectiveStateHeader Read(
            byte[] payload,
            out ObjectivePoint door,
            out ushort doorYaw,
            out bool doorOpen,
            out ObjectivePoint[] keys,
            out ObjectiveDevice[] devices)
        {
            var keyBuffer = new ObjectivePoint[MaxKeys];
            var deviceBuffer = new ObjectiveDevice[MaxDevices];

            var count = MessageCodec.ReadObjectiveState(
                payload,
                out var header,
                out _,
                out _,
                out door,
                out doorYaw,
                out doorOpen,
                keyBuffer,
                deviceBuffer);

            keys = new ObjectivePoint[count];
            Array.Copy(keyBuffer, keys, count);

            devices = new ObjectiveDevice[header.DeviceCount];
            Array.Copy(deviceBuffer, devices, header.DeviceCount);

            return header;
        }

        // ==================================================== Runner 사본

        [Fact]
        public void Runner_사본은_왕복해서_같은_값이_된다()
        {
            var payload = Write(MatchRole.Runner, doorOpen: true);
            var header = Read(payload, out var door, out var doorYaw, out var doorOpen, out var keys, out var devices);

            Assert.True(header.HasAltar);
            Assert.True(header.HasDoor);

            Assert.Equal(Door.X, door.X);
            Assert.Equal(Door.Y, door.Y);
            Assert.Equal(Door.Z, door.Z);
            Assert.Equal(DoorYaw, doorYaw);
            Assert.True(doorOpen);

            Assert.Equal(3, keys.Length);
            Assert.Equal(Keys()[2].Z, keys[2].Z);

            Assert.Equal(2, devices.Length);
            Assert.Equal(MatchDeviceType.Teleport, devices[1].Type);
            Assert.Equal(640, devices[1].Y);
        }

        [Fact]
        public void 제단은_양쪽_사본에_실린다()
        {
            foreach (var role in new[] { MatchRole.Runner, MatchRole.Seeker })
            {
                var keyBuffer = new ObjectivePoint[MaxKeys];
                var deviceBuffer = new ObjectiveDevice[MaxDevices];

                MessageCodec.ReadObjectiveState(
                    Write(role),
                    out var header,
                    out var altar,
                    out var drag,
                    out _,
                    out _,
                    out _,
                    keyBuffer,
                    deviceBuffer);

                Assert.True(header.HasAltar, $"{role} 사본에 제단이 없다.");
                Assert.Equal(Altar.X, altar.X);
                Assert.Equal(AltarDrag.X, drag.X);
            }
        }

        // ==================================================== Seeker 사본

        /// **문 블록이 사라진다.** 헤더의 비트도 함께 내려가야 수신 측이 블록을 찾지 않는다.
        [Fact]
        public void Seeker_사본에는_문_블록이_없다()
        {
            var payload = Write(MatchRole.Seeker);
            var header = Read(payload, out var door, out var doorYaw, out var doorOpen, out _, out _);

            Assert.False(header.HasDoor);
            Assert.Equal(0, door.X);
            Assert.Equal(0, door.Y);
            Assert.Equal(0, door.Z);
            Assert.Equal(0, doorYaw);
            Assert.False(doorOpen);
        }

        /// **바이트가 실제로 짧다.** 0 으로 채우는 방식이면 길이가 같고, 그러면 "문이 있다" 는
        /// 사실이 여전히 새어 나간다.
        [Fact]
        public void Seeker_사본이_문_블록만큼_짧다()
        {
            var runner = Write(MatchRole.Runner);
            var seeker = Write(MatchRole.Seeker);

            // 문 블록은 위치(6) + yaw(2) + 개방(1) = 9바이트다.
            Assert.Equal(runner.Length - 9, seeker.Length);
        }

        /// 문 좌표가 바이트 어디에도 남아 있지 않아야 한다.
        [Fact]
        public void Seeker_사본_바이트에_문_좌표가_없다()
        {
            var seeker = Write(MatchRole.Seeker);

            // 문의 x = -3000 은 리틀엔디언으로 0x48 0xF4 다. 그 연속이 없어야 한다.
            var low = unchecked((byte)(-3000 & 0xFF));
            var high = unchecked((byte)((-3000 >> 8) & 0xFF));

            for (var index = 0; index + 1 < seeker.Length; index++)
            {
                Assert.False(
                    seeker[index] == low && seeker[index + 1] == high,
                    $"오프셋 {index} 에 문의 x 좌표가 남아 있다.");
            }
        }

        [Fact]
        public void Seeker_도_열쇠와_장치는_받는다()
        {
            var payload = Write(MatchRole.Seeker);
            Read(payload, out _, out _, out _, out var keys, out var devices);

            // 룰셋 — 복도의 열쇠는 물리적 물건이고, Seeker 가 그것을 보는 것이 열쇠를 지키는
            // 전술을 만든다. 장치는 §5.3 의 파괴 대상이다.
            Assert.Equal(3, keys.Length);
            Assert.Equal(2, devices.Length);
            Assert.Equal(MatchDeviceType.AddTime, devices[0].Type);
        }

        /// 문을 뺀 것 외에는 두 사본이 같아야 한다. 필터가 다른 값을 건드리면 여기서 걸린다.
        [Fact]
        public void 문_블록_앞부분은_두_사본이_같다()
        {
            var runner = Write(MatchRole.Runner);
            var seeker = Write(MatchRole.Seeker);

            // 헤더(5) + 제단(12) 까지는 같다. flags 바이트만 다르다.
            const int flagsOffset = 2;
            const int altarEnd = ObjectiveStateHeader.WireSize + (ObjectivePoint.WireSize * 2);

            for (var index = 0; index < altarEnd; index++)
            {
                if (index == flagsOffset)
                {
                    continue;
                }

                Assert.Equal(runner[index], seeker[index]);
            }

            // flags 는 HasDoor 비트만 달라야 한다.
            var runnerFlags = (ObjectiveFlags)runner[flagsOffset];
            var seekerFlags = (ObjectiveFlags)seeker[flagsOffset];

            Assert.Equal(runnerFlags & ~ObjectiveFlags.HasDoor, seekerFlags);
        }

        // ==================================================== 계약 위반

        [Fact]
        public void 열쇠_수가_헤더와_다르면_거절한다()
        {
            var buffer = new byte[256];
            var header = new ObjectiveStateHeader(ObjectiveFlags.None, 3, 0);

            Assert.Throws<ArgumentException>(() => MessageCodec.WriteObjectiveState(
                buffer,
                header,
                default,
                default,
                default,
                0,
                false,
                new ObjectivePoint[2],
                ReadOnlySpan<ObjectiveDevice>.Empty,
                MatchRole.Runner));
        }

        [Fact]
        public void 장치_수가_헤더와_다르면_거절한다()
        {
            var buffer = new byte[256];
            var header = new ObjectiveStateHeader(ObjectiveFlags.None, 0, 2);

            Assert.Throws<ArgumentException>(() => MessageCodec.WriteObjectiveState(
                buffer,
                header,
                default,
                default,
                default,
                0,
                false,
                ReadOnlySpan<ObjectivePoint>.Empty,
                new ObjectiveDevice[1],
                MatchRole.Runner));
        }

        [Fact]
        public void 매치_전문을_목표물_전문으로_읽지_않는다()
        {
            var buffer = new byte[64];
            var length = MessageCodec.WriteMatchState(
                buffer,
                new MatchStateHeader(MatchPhase.Playing, 0, 0, 0, 0, 0),
                ReadOnlySpan<MatchParticipant>.Empty,
                MatchRole.Runner);

            var payload = new ReadOnlySpan<byte>(buffer, 0, length).ToArray();

            Assert.Throws<InvalidOperationException>(() => MessageCodec.ReadObjectiveState(
                payload,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                new ObjectivePoint[MaxKeys],
                new ObjectiveDevice[MaxDevices]));
        }

        [Fact]
        public void 전문의_종류를_본문_파싱_없이_알_수_있다()
        {
            Assert.Equal(EventKind.ObjectiveState, MessageCodec.ReadEventKind(Write(MatchRole.Runner)));
        }

        // ==================================================== 크기

        /// 클라이언트 수신 버퍼가 512B 다(`NetworkClient.ReceiveBytes`). 열쇠·장치 수를 늘리는
        /// 변경에서 가장 먼저 넘칠 자리이므로 여유를 못질해 둔다.
        [Fact]
        public void 최악의_경우가_수신_버퍼_안에_들어간다()
        {
            // 배치 10개 + 정원 전원이 각자 들고 죽어 흘린 열쇠, 장치 9개.
            //
            // **정원에서 유도한다.** 숫자를 적어 두면 정원이 바뀐 뒤에도 통과하면서 버퍼가
            // 충분한지에 대해 아무것도 말하지 않는다 — `Room` 의 버퍼도 같은 식으로 잡는다.
            var worst = MessageCodec.ObjectiveStateMaxWireSize(
                MatchConstants.KeysPlaced + RealtimeConstants.Rooms.MaxPlayers,
                MatchConstants.DeviceMix.Length);

            Assert.True(worst < 512, $"최악의 경우 {worst}B 로 수신 버퍼 512B 를 넘는다.");
        }

        [Fact]
        public void 빈_배치도_인코딩된다()
        {
            var buffer = new byte[64];

            var length = MessageCodec.WriteObjectiveState(
                buffer,
                new ObjectiveStateHeader(ObjectiveFlags.None, 0, 0),
                default,
                default,
                default,
                0,
                false,
                ReadOnlySpan<ObjectivePoint>.Empty,
                ReadOnlySpan<ObjectiveDevice>.Empty,
                MatchRole.Runner);

            Assert.Equal(ObjectiveStateHeader.WireSize, length);

            var header = Read(
                new ReadOnlySpan<byte>(buffer, 0, length).ToArray(),
                out _,
                out _,
                out _,
                out var keys,
                out var devices);

            Assert.False(header.HasAltar);
            Assert.False(header.HasDoor);
            Assert.Empty(keys);
            Assert.Empty(devices);
        }
    }
}
