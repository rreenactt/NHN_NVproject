using System;

namespace NV.Realtime.Contracts
{
    /// 정적 룸 하나의 구성. 맵 id 와, 그 룸에만 적용되는 봇 오버라이드다.
    ///
    /// 봇 설정이 서버 전역 하나였을 때는 정적 룸이 둘이 되는 순간 같은 행동이 강제됐다 —
    /// 열린 방은 `Objective` 가 맞고 미로는 `Wander` 가 맞는데, 하나를 고르면 다른 쪽
    /// 검증이 죽는다. 그래서 룸이 자기 오버라이드를 갖고, 전역 `Realtime:Bots` 는
    /// 생략한 필드를 채우는 기본값이 된다.
    ///
    /// **`Enabled` 는 여기 없다.** 봇 기능의 방어선은 `GuardDevelopmentOnlyOptions` 가 보는
    /// 전역 스위치 하나이고, 프로필마다 스위치를 두면 "전역은 껐는데 프로필이 켠다" 가
    /// 표현 가능한 상태가 된다. 전역이 마스터 스위치, 프로필은 오버라이드다.
    public sealed class TestRoomProfile
    {
        public TestRoomProfile(
            string mapId,
            int? fillTo = null,
            BotBehavior? behavior = null,
            BotRolePreference? role = null,
            uint? seed = null)
        {
            if (string.IsNullOrEmpty(mapId))
            {
                throw new ArgumentException("맵 id 가 없다.", nameof(mapId));
            }

            MapId = mapId;
            FillTo = fillTo;
            Behavior = behavior;
            Role = role;
            Seed = seed;
        }

        public string MapId { get; }

        /// null 이면 전역 값을 따른다. 아래 셋도 같은 규칙이다.
        public int? FillTo { get; }

        public BotBehavior? Behavior { get; }

        public BotRolePreference? Role { get; }

        public uint? Seed { get; }

        /// 전역 설정 위에 이 프로필을 겹친 실효 봇 설정.
        ///
        /// 새 인스턴스를 만든다. 전역 객체를 고쳐 쓰면 한 룸의 오버라이드가
        /// 다른 룸의 기본값이 된다.
        public BotOptions ResolveBots(BotOptions global)
        {
            if (global == null)
            {
                throw new ArgumentNullException(nameof(global));
            }

            return new BotOptions
            {
                Enabled = global.Enabled,
                FillTo = FillTo ?? global.FillTo,
                Behavior = Behavior ?? global.Behavior,
                Role = Role ?? global.Role,
                Seed = Seed ?? global.Seed,
            };
        }
    }
}
