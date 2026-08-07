using System;

namespace NV.Shared.Contracts.Enums
{
    /// 놓인 장치 하나의 지금 상태. `ObjectiveDevice.State` 가 싣는다.
    ///
    /// **이 열거형이 생겼다는 것은 장치 사용이 서버로 넘어왔다는 뜻이다**(IG-013). 그전에는
    /// 바이트가 자리만 잡고 0 으로 나갔고, 사용·쿨다운은 클라이언트마다 따로 셌다 — 그래서
    /// 1회용 장치를 인원수만큼 쓸 수 있었고, 서버가 소유한 상태를 건드리는 효과(순간이동·지혈·
    /// 시간 추가)는 로컬로 적용됐다가 다음 전문에 되돌려졌다.
    ///
    /// 남은 쿨다운 **초를 싣지 않는다.** 목표물 전문은 변경 즉시 + 5초 주기로 나가므로 연속으로
    /// 줄어드는 값을 담을 자리가 아니다. 클라이언트는 이 비트가 켜진 것을 본 시각에서
    /// `MatchConstants` 의 상수로 카운트다운을 그린다 — 상수는 양쪽이 공유하므로 어긋나지
    /// 않고, 어긋나더라도 다음 전문이 바로잡는다.
    [Flags]
    public enum MatchDeviceState : byte
    {
        None = 0,

        /// 다 썼다. 1회용이 소진됐거나 파괴됐다. 되돌아오지 않는다.
        Spent = 1 << 0,

        /// 쿨다운 중. 다회용이 다시 켜지기를 기다린다.
        ///
        /// 순간이동은 **한 대를 쓰면 모든 순간이동 장치가** 이 비트를 받는다
        /// (`MatchConstants.TeleportSharedCooldown` — 기획서 §5.2 의 전역 락아웃).
        Cooling = 1 << 1,

        /// 효과가 **지금 돌고 있다.**
        ///
        /// 즉시 끝나는 효과에는 쓰지 않는다. 이 비트를 쓰는 것은 전체 정지+투시 하나뿐이며,
        /// 그것이 이 비트가 필요한 이유다 — 정지는 스냅샷의 `EntityFlags.Frozen` 으로 오지만
        /// 그 비트는 체인 견인과 한 자리를 나눠 쓰므로(`Room` 의 플래그 인코딩), 클라이언트가
        /// "왜 못 움직이는가" 를 가르는 근거가 따로 있어야 한다. 벽 투명화와 배너도 이 비트를
        /// 보고 켠다.
        Active = 1 << 2,

        /// Seeker 가 부쉈다(기획서 §5, IG-015). `Spent` 와 갈라 두는 이유는 화면이 다르기
        /// 때문이다 — 소진된 장치는 어둡게 서 있고 부서진 장치는 죽은 채로 서 있으며,
        /// 프롬프트도 "SPENT" 와 "DESTROYED" 로 다르다.
        Destroyed = 1 << 3,
    }

    /// 상태 바이트의 **위 4비트**에 실리는 피격 수를 넣고 뺀다.
    ///
    /// 한 바이트에 플래그와 수를 같이 싣는 것은 자리를 아끼려는 것이 아니라 **와이어를 늘리지
    /// 않기 위해서다.** 목표물 전문의 장치 항목은 크기가 정해져 있고(`ObjectiveDevice.WireSize`),
    /// 필드를 더하면 프로토콜 버전이 올라간다 — 반면 이 바이트는 처음부터 상태용으로 잡혀
    /// 있었고 아래 4비트만 쓰고 있었다.
    ///
    /// 세는 값이 `MatchConstants.DeviceDestroyHits`(4) 이므로 4비트(0~15)면 충분하다. 그 상수가
    /// 15를 넘게 되면 여기가 먼저 거짓말을 시작하므로, 넣을 때 잘라서 그 사실이 프롬프트의
    /// 숫자로 드러나게 한다.
    public static class MatchDeviceHits
    {
        private const int Shift = 4;

        public static MatchDeviceState With(MatchDeviceState state, int hits)
        {
            var clamped = hits < 0 ? 0 : (hits > 0xF ? 0xF : hits);

            return (MatchDeviceState)(((byte)state & 0x0F) | (clamped << Shift));
        }

        public static int Of(MatchDeviceState state)
        {
            return ((byte)state >> Shift) & 0x0F;
        }

        /// 수를 뺀 플래그만. 비교할 때 쓴다 — 피격 수가 섞여 있으면 같은 상태끼리 값이 달라진다.
        public static MatchDeviceState FlagsOf(MatchDeviceState state)
        {
            return (MatchDeviceState)((byte)state & 0x0F);
        }
    }
}
