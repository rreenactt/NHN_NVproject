using System;
using NV.Shared.Contracts.Enums;

namespace NV.Shared.Contracts.Messages
{
    /// 목표물 전문의 고정부. 제단·문·열쇠·장치 블록이 뒤따른다.
    ///
    /// **이 전문이 이 이관 작업의 원래 목적이다.** 지금까지 목표물 위치는
    /// `RoomStateHeader.PlacementSeed` 를 받아 **모든 클라이언트가 같은 씨드로 계산**했고,
    /// 그래서 문의 좌표가 Seeker 의 프로세스 메모리에도 있었다. 룰셋은 문이 Runner 에게만
    /// 보여야 한다고 정하고 클라이언트는 컬링 레이어로 그것을 지키지만, WebGL 빌드가
    /// 디컴파일되는 전제에서 카메라 마스크로 막을 수 있는 종류의 정보가 아니다.
    ///
    /// **그래서 Seeker 사본에서는 문 블록을 아예 뺀다.** 좌표를 0 으로 채우는 것으로는
    /// 부족하다 — 그것도 "문이 있다" 는 사실과 블록 크기를 알려 준다. 없는 블록은 복원할
    /// 수 없다.
    ///
    /// 열쇠는 전원에게 보낸다. 룰셋이 그렇게 정한다 — 복도에 놓인 열쇠는 물리적 물건이고,
    /// Seeker 가 그것을 보는 것이 열쇠를 지키는 전술을 만든다. 제단과 장치도 공통이다
    /// (제단은 고정물이고 Seeker 가 알아야 하는 벌칙 지점, 장치는 §5.3 의 파괴 대상).
    ///
    /// 크기는 8인 룸 최악의 경우 대략 5 + 12 + 9 + 10×6 + 9×10 = 176B 다. 걸릴 수 있는
    /// 상한은 클라이언트의 수신 버퍼 512B(`NetworkClient.ReceiveBytes`)이고 여유가 3배쯤
    /// 있으므로, **열쇠나 장치 수를 크게 늘리는 변경에서 가장 먼저 넘칠 자리다.**
    public readonly struct ObjectiveStateHeader
    {
        /// opcode(1) + kind(1) + flags(1) + keyCount(1) + deviceCount(1)
        public const int WireSize = 5;

        public ObjectiveStateHeader(
            ObjectiveFlags flags,
            byte keyCount,
            byte deviceCount)
        {
            Flags = flags;
            KeyCount = keyCount;
            DeviceCount = deviceCount;
        }

        public ObjectiveFlags Flags { get; }

        public byte KeyCount { get; }

        public byte DeviceCount { get; }

        public bool HasAltar => (Flags & ObjectiveFlags.HasAltar) != 0;

        /// 문 블록이 실려 있는가. **Seeker 사본에서는 false 다.**
        public bool HasDoor => (Flags & ObjectiveFlags.HasDoor) != 0;
    }

    /// 목표물 전문의 고정부 플래그.
    [Flags]
    public enum ObjectiveFlags : byte
    {
        None = 0,

        /// 제단 블록이 뒤따른다. 격자가 없는 맵에서는 서지 않는다.
        HasAltar = 1 << 0,

        /// 문 블록이 뒤따른다. **역할별로 갈리는 유일한 비트다.**
        HasDoor = 1 << 1,
    }

    /// 놓인 장치 하나의 와이어 표현.
    public readonly struct ObjectiveDevice
    {
        /// x·y·z(2×3) + yaw(2) + type(1) + state(1)
        public const int WireSize = 10;

        public ObjectiveDevice(
            MatchDeviceType type,
            short x,
            short y,
            short z,
            ushort yaw,
            MatchDeviceState state)
        {
            Type = type;
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
            State = state;
        }

        public MatchDeviceType Type { get; }

        public short X { get; }

        public short Y { get; }

        public short Z { get; }

        public ushort Yaw { get; }

        /// 소진·쿨다운·진행 중 상태(IG-013).
        ///
        /// 자리를 미리 잡아 둔 바이트였고, 이제 채워진다. 파괴(IG-015)는 아직 클라이언트가
        /// 세므로 `Spent` 로 올라오지 않는다.
        public MatchDeviceState State { get; }
    }

    /// 양자화된 위치 하나. 열쇠 목록과 제단이 쓴다.
    public readonly struct ObjectivePoint
    {
        /// x·y·z(2×3)
        public const int WireSize = 6;

        public ObjectivePoint(short x, short y, short z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public short X { get; }

        public short Y { get; }

        public short Z { get; }
    }
}
