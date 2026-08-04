using System;
using System.Numerics;
using NV.Realtime.Contracts;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Simulation;

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
                BotBehavior.Objective => Objective(mind, senses),
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
        /// 대한 발사체 스윕. 목표물을 찾아가는 것은 `Objective` 이고 그때 경로가 필요해진다.
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

            return WalkToward(feet, mind.Goal);
        }

        /// 자기 역할의 목표를 수행한다. 사람 없이 매치가 끝까지 가는 것이 이 단계의 목적이다.
        ///
        /// **경로 탐색이 없는 것이 여기서 처음 실질적 제약이 된다.** 열쇠와 문에 실제로
        /// 도달해야 하므로, 열린 방(`test-room`)에서는 동작하고 미로에서는 봇이 헤맨다.
        /// 격자 A\* 는 체인 견인과 같은 자리를 공유하며(계획서 §5·§7), 그 작업이 들어올 때
        /// 이 함수의 `WalkToward` 가 경로의 다음 지점을 향하도록 바뀐다.
        ///
        /// 진행 단계 밖에서는 서 있는다. 리빌 중에는 이동이 잠기고(룸이 비운다) 목표물
        /// 판정도 돌지 않으므로, 걷는 프레임을 만들 이유가 없다.
        private static InputFrame Objective(BotMind mind, in BotSenses senses)
        {
            if (senses.MatchPhase != MatchPhase.Playing || !senses.ObjectivesPlaced)
            {
                return Wander(mind, senses);
            }

            return senses.Role switch
            {
                MatchRole.Runner => RunnerObjective(mind, senses),
                MatchRole.Seeker => SeekerObjective(mind, senses),
                _ => Wander(mind, senses),
            };
        }

        /// 열쇠를 모아 문에 넣고 빠져나간다.
        ///
        /// 순서가 규칙을 하나 정하고 있다. **문이 열려 있으면 열쇠보다 탈출이 먼저다** —
        /// 반대로 두면 문이 열린 뒤에도 봇이 남은 열쇠를 주우러 다니고, 탈출 판정이 한 번도
        /// 돌지 않는다. 그것이 이 단계가 확인하려는 마지막 판정이다.
        ///
        /// 습득에는 입력이 필요 없다. 룰셋이 거리 폴링이므로 열쇠 위로 걸어가면 주워진다
        /// (`Room.PickUpKeys`). 삽입은 명시적인 `Interact` 다.
        private static InputFrame RunnerObjective(BotMind mind, in BotSenses senses)
        {
            var feet = senses.State.Position;

            if (senses.DoorOpen)
            {
                // 문간에 서 있으면 유지 시간이 쌓여 탈출한다. 도착한 뒤에는 멈춰야 하므로
                // 문 사용 반경 안에서는 이동을 비운다 — 계속 밀면 문을 지나쳐 벗어나고,
                // 그때 `EscapeHoldTicks` 가 0 으로 돌아간다.
                return WithinRange(feet, senses.DoorPosition, MatchConstants.DoorUseRadius * 0.5f)
                    ? Idle(senses)
                    : WalkToward(feet, senses.DoorPosition);
            }

            if (senses.CarriedKeys > 0)
            {
                var frame = WalkToward(feet, senses.DoorPosition);

                // 자격과 거리는 룸이 다시 본다(`Room.InsertKeys`). 여기서 반경을 좁게
                // 잡으면 봇이 문 앞에서 요청하지 않는 구간이 생기므로, 룸이 쓰는 값과
                // 같은 값을 쓴다 — 같은 질문에는 같은 답이다(AS-11).
                if (WithinRange(feet, senses.DoorPosition, MatchConstants.DoorUseRadius))
                {
                    frame = new InputFrame(
                        frame.Buttons | ButtonFlags.Interact,
                        frame.MoveX,
                        frame.MoveZ,
                        frame.Yaw,
                        frame.Pitch);
                }

                return frame;
            }

            if (TryNearestKey(senses, feet, out var key))
            {
                return WalkToward(feet, key);
            }

            // 맵에 열쇠가 없고 든 것도 없다. 문은 아직 닫혀 있으므로 할 일이 없다 —
            // 다른 Runner 가 들고 있는 상태이며, 그때는 돌아다니는 것이 맞다.
            return Wander(mind, senses);
        }

        /// 가장 가까운 Runner 를 쫓고 시선이 닿으면 쏜다.
        ///
        /// 발사 간격과 탄약은 룸이 센다(`Room.FireWeapons`). 두뇌는 방아쇠를 당기고 있을지만
        /// 정하며, 그것이 사람의 입력과 같은 모양이다 — `Fire` 는 엣지가 아니라 누르고 있는
        /// 상태다.
        private static InputFrame SeekerObjective(BotMind mind, in BotSenses senses)
        {
            if (!TryNearestRunner(senses, out var target))
            {
                return Wander(mind, senses);
            }

            var feet = senses.State.Position;
            var frame = AimAt(feet, target.Feet);

            if (HasLineOfSight(senses, target.Feet))
            {
                frame = new InputFrame(
                    frame.Buttons | ButtonFlags.Fire,
                    frame.MoveX,
                    frame.MoveZ,
                    frame.Yaw,
                    frame.Pitch);
            }

            return frame;
        }

        /// 목표를 향해 전진하는 프레임. 시선을 목표로 돌리고 앞으로 걷는다.
        ///
        /// 좌우(`MoveX`)를 쓰지 않는다. 함께 쓰면 시선과 진행 방향이 갈리는데, 봇에게는
        /// 그럴 이유가 없다 — 보는 쪽으로 걷는 것이 사람의 조작이다.
        private static InputFrame WalkToward(Vector3 feet, Vector3 goal)
        {
            return new InputFrame(
                ButtonFlags.None,
                0,
                RealtimeConstants.Bots.ForwardAxis,
                Quantization.ToFixedYaw(YawToward(feet, goal)),

                // 피치는 수평이다. 걷는 봇은 조준하지 않는다.
                0);
        }

        /// 목표의 몸 중심을 겨누고 그쪽으로 걷는 프레임.
        ///
        /// **눈높이에서 몸 중심으로 잰다.** 총알이 눈높이에서 나가고(`Room.TrySpawnProjectile`)
        /// 맞는 판정이 몸 박스이므로, 발밑을 겨누면 탄이 표적의 발 아래로 지나간다.
        private static InputFrame AimAt(Vector3 feet, Vector3 targetFeet)
        {
            var eye = feet.Y + (SimConstants.PlayerHeight * SimConstants.EyeHeightRatio);
            var center = targetFeet.Y + (SimConstants.PlayerHeight * 0.5f);

            var dx = targetFeet.X - feet.X;
            var dz = targetFeet.Z - feet.Z;
            var horizontal = MathF.Sqrt((dx * dx) + (dz * dz));

            // **피치는 음수가 위다**(클라이언트의 카메라 규약, `PlayerMovement.Forward`).
            // 부호를 뒤집어 적으면 술래가 표적의 반대쪽 높이를 겨누고, 증상은 "가까이서는
            // 맞는데 멀면 안 맞는다" 가 된다.
            var pitch = -MathF.Atan2(center - eye, horizontal);

            return new InputFrame(
                ButtonFlags.None,
                0,
                RealtimeConstants.Bots.ForwardAxis,
                Quantization.ToFixedYaw(MathF.Atan2(dx, dz)),
                Quantization.ToFixedPitch(pitch));
        }

        /// 요 규약은 `PlayerMovement.Forward` 가 소유한다 — 전방이 `(sin yaw, 0, cos yaw)`
        /// 이므로 목표 방향의 요는 `atan2(dx, dz)` 다. 인자 순서를 바꿔 적으면 봇이 목표와
        /// 90도 어긋난 방향으로 걷고, 증상은 "봇이 벽만 따라다닌다" 가 된다.
        private static float YawToward(Vector3 feet, Vector3 goal)
        {
            return MathF.Atan2(goal.X - feet.X, goal.Z - feet.Z);
        }

        /// 눈에서 표적의 몸 중심까지 지형에 막히지 않는가.
        ///
        /// 막힌 채로 쏘면 탄창 3발이 벽에 사라진다. 룸은 그 발사를 정당한 것으로 받아들이므로
        /// (사람도 벽을 쏠 수 있다) 걸러야 하는 쪽은 두뇌다.
        private static bool HasLineOfSight(in BotSenses senses, Vector3 targetFeet)
        {
            var eye = senses.State.Position
                + new Vector3(0f, SimConstants.PlayerHeight * SimConstants.EyeHeightRatio, 0f);

            var center = targetFeet + new Vector3(0f, SimConstants.PlayerHeight * 0.5f, 0f);

            var to = center - eye;
            var distance = to.Length();

            if (distance <= SimConstants.PlayerRadius)
            {
                // 몸이 겹칠 만큼 붙어 있다. 방향을 정규화할 수 없고, 이 거리에서 막히는
                // 지형도 없다.
                return true;
            }

            var direction = to / distance;

            return !senses.Map.Collision.Raycast(eye, direction, distance, out _);
        }

        /// 맵에 남은 열쇠 중 수평으로 가장 가까운 것.
        private static bool TryNearestKey(in BotSenses senses, Vector3 feet, out Vector3 key)
        {
            key = default;

            var best = float.MaxValue;
            var found = false;

            for (var index = 0; index < senses.Keys.Count; index++)
            {
                var candidate = senses.Keys[index];
                var distance = HorizontalSquared(feet, candidate);

                if (distance >= best)
                {
                    continue;
                }

                best = distance;
                key = candidate;
                found = true;
            }

            return found;
        }

        /// 아직 판정에 들어가는 Runner 중 가장 가까운 몸.
        private static bool TryNearestRunner(in BotSenses senses, out BotTarget target)
        {
            target = default;

            var feet = senses.State.Position;
            var best = float.MaxValue;
            var found = false;

            for (var index = 0; index < senses.TargetCount; index++)
            {
                var candidate = senses.Targets[index];

                if (!candidate.IsRunner || !candidate.IsActive)
                {
                    continue;
                }

                var distance = HorizontalSquared(feet, candidate.Feet);

                if (distance >= best)
                {
                    continue;
                }

                best = distance;
                target = candidate;
                found = true;
            }

            return found;
        }

        private static bool WithinRange(Vector3 feet, Vector3 point, float radius)
        {
            return HorizontalSquared(feet, point) <= radius * radius;
        }

        /// 수평 거리만 잰다. 층이 다른 목표는 계단으로 이어져야 도달하고, 그 판단은
        /// 경로 탐색이 들어올 때의 일이다.
        private static float HorizontalSquared(Vector3 a, Vector3 b)
        {
            var dx = a.X - b.X;
            var dz = a.Z - b.Z;

            return (dx * dx) + (dz * dz);
        }
    }
}
