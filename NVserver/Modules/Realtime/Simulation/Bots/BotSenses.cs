using System.Collections.Generic;
using System.Numerics;
using NV.Shared.Collision;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Simulation;

namespace NV.Realtime.Simulation.Bots
{
    /// 봇이 이번 틱에 볼 수 있는 것. 룸이 채워 넘긴다.
    ///
    /// **읽기 전용 묶음이고 살아 있는 룸 객체를 담지 않는다.** 두뇌가 몸이나 목표물을
    /// 쥘 수 있으면 언젠가 위치를 직접 옮기거나 열쇠를 지운다. 두뇌가 돌려줄 수 있는
    /// 것은 `InputFrame` 하나뿐이어야 한다 — 목표물은 `IReadOnlyList` 로, 다른 몸은
    /// 값 복사(`BotTarget`)로 들어온다.
    ///
    /// **여기 담긴 것은 룰셋이 봇에게 허용한 정보가 아니다.** 서버가 아는 좌표를 그대로
    /// 담으므로 목표 수행 봇은 문의 위치를 아는 치팅 봇이다(계획서 §3.7). 개발 검증용이며,
    /// 프로덕션 AI 로 올리려면 시야 제약이 먼저 필요하다.
    internal readonly struct BotSenses
    {
        public BotSenses(
            in PlayerState state,
            in InputFrame lastInput,
            WorldMap map,
            MatchRole role,
            MatchPhase matchPhase,
            int carriedKeys,
            bool objectivesPlaced,
            bool doorOpen,
            Vector3 doorPosition,
            IReadOnlyList<Vector3> keys,
            BotTarget[] targets,
            int targetCount)
        {
            State = state;
            LastInput = lastInput;
            Map = map;
            Role = role;
            MatchPhase = matchPhase;
            CarriedKeys = carriedKeys;
            ObjectivesPlaced = objectivesPlaced;
            DoorOpen = doorOpen;
            DoorPosition = doorPosition;
            Keys = keys;
            Targets = targets;
            TargetCount = targetCount;
        }

        public PlayerState State { get; }

        /// 직전에 만든 프레임. 시선을 이어 가는 기준이다.
        public InputFrame LastInput { get; }

        /// 이 룸의 지형. 격자가 없는 맵도 있으므로 `HasGrid` 를 먼저 본다.
        public WorldMap Map { get; }

        public MatchRole Role { get; }

        public MatchPhase MatchPhase { get; }

        /// 이 봇이 들고 있는 열쇠 수. 문으로 갈지 열쇠로 갈지가 이것으로 갈린다.
        public int CarriedKeys { get; }

        public bool ObjectivesPlaced { get; }

        public bool DoorOpen { get; }

        public Vector3 DoorPosition { get; }

        /// 아직 맵에 남은 열쇠. 주워지면 룸이 목록에서 지운다.
        public IReadOnlyList<Vector3> Keys { get; }

        /// 다른 몸들. **`TargetCount` 까지만 유효하다** — 배열은 룸이 재사용한다.
        public BotTarget[] Targets { get; }

        public int TargetCount { get; }
    }
}
