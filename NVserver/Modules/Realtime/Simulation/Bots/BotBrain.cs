using System;
using NV.Realtime.Contracts;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;

namespace NV.Realtime.Simulation.Bots
{
    /// 봇의 다음 입력을 만든다. **`InputFrame` 만 돌려준다.**
    ///
    /// 그것이 이 파일의 전부이고 규칙이다. 봇이 위치나 소지 열쇠를 직접 만지면 서버
    /// 판정을 우회하게 되고, 그러면 "봇으로 확인했다" 가 사람에게 아무것도 보증하지
    /// 않는다. 여기서 나온 프레임은 사람의 프레임과 같은 경로를 지난다 —
    /// `InputValidator.Sanitize` → 이동 잠금 → `PlayerMovement.Step` → 목표물·전투 판정.
    ///
    /// `Shared` 가 아니라 모듈에 있다. `structure.md` 8문 표의 1번 — 클라이언트는 봇의
    /// 다음 입력을 예측할 필요가 없고 스냅샷으로 결과만 받는다. 그래서 `MathF` 를 써도
    /// 된다. 결정성이 필요한 곳은 이동 계산이고 그쪽은 `Shared` 가 소유한다.
    internal static class BotBrain
    {
        public static InputFrame Think(BotBehavior behavior, BotMind mind, in BotSenses senses)
        {
            return behavior switch
            {
                BotBehavior.Wander => Wander(mind, senses),
                _ => Idle(senses),
            };
        }

        /// 서 있는다. 이동 성분만 비우고 시선은 유지한다.
        ///
        /// 시뮬레이션 자체는 계속 돌므로 중력을 받아 바닥에 내려앉고, 총에 맞고, 문간에
        /// 서 있으면 탈출까지 한다. 서 있는 몸 하나로 검증되는 것이 그만큼 넓다.
        private static InputFrame Idle(in BotSenses senses)
        {
            return InputValidator.Neutral(senses.LastInput);
        }

        /// 격자에서 뽑은 목표점을 향해 걷는다. **경로 탐색이 없다.**
        ///
        /// 직선으로 걷고 막히면 다른 목표를 뽑는다(`BotMind.NeedsNewGoal`). 열린 방에서는
        /// 그대로 돌아다니고 미로에서는 비효율적이지만, 이 단계가 검증하는 것은 이동 판정과
        /// 표현이다 — 속도 상한, 예측·보정, 발소리, 원격 몸의 애니메이션, 움직이는 표적에
        /// 대한 발사체 스윕. 목표물을 찾아가는 것은 다음 단계이고 그때 경로가 필요해진다.
        ///
        /// 격자가 없는 맵에서는 서 있는다. 아무 방향으로 걷게 하면 봇이 벽을 밀며 진동하고,
        /// 그것은 이동 판정을 검증하는 데 도움이 되지 않는다.
        private static InputFrame Wander(BotMind mind, in BotSenses senses)
        {
            var feet = senses.State.Position;

            if (mind.NeedsNewGoal(feet) && (!senses.Map.HasGrid || !mind.TryRetarget(senses.Map.Grid)))
            {
                return Idle(senses);
            }

            // 요 규약은 `PlayerMovement.Forward` 가 소유한다 — 전방이 `(sin yaw, 0, cos yaw)`
            // 이므로 목표 방향의 요는 `atan2(dx, dz)` 다. 인자 순서를 바꿔 적으면 봇이
            // 목표와 90도 어긋난 방향으로 걷고, 증상은 "봇이 벽만 따라다닌다" 가 된다.
            var yaw = MathF.Atan2(mind.Goal.X - feet.X, mind.Goal.Z - feet.Z);

            // 전진만 쓴다. 좌우(`MoveX`)를 함께 쓰면 시선과 진행 방향이 갈리는데, 봇에게는
            // 그럴 이유가 없다 — 시선을 목표로 돌리고 앞으로 걷는 것이 사람의 조작이다.
            return new InputFrame(
                ButtonFlags.None,
                0,
                RealtimeConstants.Bots.ForwardAxis,
                Quantization.ToFixedYaw(yaw),

                // 피치는 수평이다. 목표는 발밑 좌표이고 봇은 조준하지 않는다.
                0);
        }
    }
}
