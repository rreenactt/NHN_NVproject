using System.Numerics;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;

namespace NV.Shared.Simulation
{
    /// 시뮬레이션 상태와 와이어 포맷 사이의 변환.
    ///
    /// 서버는 정방향(보낼 때), 클라이언트는 양방향을 쓴다.
    /// 원격 플레이어를 보간할 때 클라이언트가 EntityState 를 PlayerState 로 되돌린다.
    ///
    /// 양자화는 손실이 있다. 서버가 자기 PlayerState 를 그대로 들고 있고
    /// 클라이언트는 양자화된 값만 보므로, 예측 비교 시 이 오차를 감안해야 한다.
    /// 오차 상한은 위치 1/128m, 요 한 바퀴의 1/131072 이다.
    public static class StateProjection
    {
        public static EntityState ToEntityState(byte id, in PlayerState state)
        {
            return ToEntityState(id, state, EntityFlags.None);
        }

        /// 시뮬레이션 상태에 **매치 판정 비트를 얹어** 와이어로 만든다.
        ///
        /// 두 종류의 플래그가 한 바이트에 섞이는 자리가 여기다. `PlayerState.Flags` 는
        /// 이동이 소유하고(`Alive`·`OnGround`·`Crouching`) 클라이언트도 예측하지만,
        /// `matchFlags`(`Bleeding`·`Seeker`·`Escaped`·`Frozen`)는 서버만 안다.
        ///
        /// **매치 비트를 `PlayerState` 에 담지 않는 이유가 이 분리다.** 그 구조체는
        /// 결정적 시뮬레이션 상태이고 `StateHash` 에 들어가는데, 클라이언트가 예측할 수
        /// 없는 비트가 섞이면 리컨실리에이션의 해시 비교가 영구히 어긋난다. 합치는 것은
        /// 인코딩 순간뿐이고, 그러면 이동 계산은 매치를 모른 채로 남는다.
        public static EntityState ToEntityState(byte id, in PlayerState state, EntityFlags matchFlags)
        {
            return new EntityState(
                id,
                Quantization.ToFixedPosition(state.Position.X),
                Quantization.ToFixedPosition(state.Position.Y),
                Quantization.ToFixedPosition(state.Position.Z),
                Quantization.ToFixedYaw(state.Yaw),
                Quantization.ToFixedPitch(state.Pitch),
                state.Flags | matchFlags,
                state.Health);
        }

        /// 이동이 소유하는 비트만 남긴다.
        ///
        /// 클라이언트가 서버 스냅샷을 받아 예측 상태와 비교할 때 쓴다. 매치 비트를 빼지
        /// 않으면 "서버가 보낸 몸" 과 "내가 예측한 몸" 이 항상 달라 보인다.
        public static EntityFlags SimulationFlagsOf(EntityFlags flags)
        {
            return flags & (EntityFlags.Alive | EntityFlags.OnGround | EntityFlags.Crouching);
        }

        /// 매치 판정이 소유하는 비트만 남긴다.
        public static EntityFlags MatchFlagsOf(EntityFlags flags)
        {
            return flags & (EntityFlags.Bleeding | EntityFlags.Seeker | EntityFlags.Escaped | EntityFlags.Frozen);
        }

        public static PlayerState ToPlayerState(in EntityState entity)
        {
            var state = default(PlayerState);

            state.Position = new Vector3(
                Quantization.ToMeters(entity.X),
                Quantization.ToMeters(entity.Y),
                Quantization.ToMeters(entity.Z));

            // 속도는 와이어에 없다. 보간은 위치 차이로 계산한다.
            state.Velocity = new Vector3(0f, 0f, 0f);
            state.Yaw = Quantization.ToYawRadians(entity.Yaw);
            state.Pitch = Quantization.ToPitchRadians(entity.Pitch);
            state.Flags = entity.Flags;
            state.Health = entity.Health;

            return state;
        }

        /// 양자화를 한 번 통과시킨 값. 서버가 클라이언트의 예측값과 비교할 때 쓴다.
        public static PlayerState RoundTrip(byte id, in PlayerState state)
        {
            var wire = ToEntityState(id, state);
            var restored = ToPlayerState(wire);

            // 속도와 플래그는 양자화 대상이 아니므로 원본을 유지한다.
            restored.Velocity = state.Velocity;
            return restored;
        }

        public static bool IsAlive(EntityFlags flags)
        {
            return (flags & EntityFlags.Alive) != 0;
        }
    }
}
