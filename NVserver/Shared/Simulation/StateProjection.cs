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
            return new EntityState(
                id,
                Quantization.ToFixedPosition(state.Position.X),
                Quantization.ToFixedPosition(state.Position.Y),
                Quantization.ToFixedPosition(state.Position.Z),
                Quantization.ToFixedYaw(state.Yaw),
                Quantization.ToFixedPitch(state.Pitch),
                state.Flags,
                state.Health);
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
