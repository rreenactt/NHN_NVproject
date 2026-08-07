using NV.Shared.Contracts.Enums;

namespace NV.Shared.Contracts.Messages
{
    /// 스냅샷에 실리는 엔티티 하나. 13B.
    /// 위치는 Quantization 으로 양자화된 고정소수점이며 미터가 아니다.
    public readonly struct EntityState
    {
        public const int WireSize = 13;

        public EntityState(
            byte id,
            short x,
            short y,
            short z,
            ushort yaw,
            short pitch,
            EntityFlags flags,
            byte escapeProgress)
        {
            Id = id;
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
            Pitch = pitch;
            Flags = flags;
            EscapeProgress = escapeProgress;
        }

        public byte Id { get; }

        public short X { get; }

        public short Y { get; }

        public short Z { get; }

        public ushort Yaw { get; }

        public short Pitch { get; }

        public EntityFlags Flags { get; }

        /// 탈출 유지 진행도. 0 = 안 하고 있음, 255 = 문턱을 막 넘음.
        ///
        /// **이 자리에는 `Health` 가 있었고 한 번도 쓰이지 않았다.** 이 게임은 체력이 아니라
        /// 피격 수를 세므로(`RunnerHitsToDie`) 스폰의 100 이 그대로 실려 나가 아무도 읽지
        /// 않았다 — 탄창이 죽은 `Flags` 바이트를 물려받은 것과 같은 자리다. 크기를 늘리지
        /// 않으므로 프로토콜 버전도 그대로다.
        ///
        /// **매 틱 나가야 한다.** 유지 시간이 0.8초라 2Hz 전문으로는 한두 점밖에 찍히지 않고,
        /// 그 값으로는 끊을 타이밍을 볼 수 없다 — 룰셋이 그 진행도를 공개로 정한 이유가
        /// 끊을 수 있게 하는 것이므로, 채널이 늦으면 규칙 자체가 성립하지 않는다.
        public byte EscapeProgress { get; }
    }
}
