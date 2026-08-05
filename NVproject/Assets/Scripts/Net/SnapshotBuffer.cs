using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using UnityEngine;

namespace NV.Client.Net
{
    /// 한 엔티티의 렌더링용 상태. 와이어의 고정소수점을 이미 미터·도로 풀어 놓은 값이다.
    public struct EntitySample
    {
        public byte Id;
        public Vector3 Position;
        public Vector3 Velocity;
        public float YawDegrees;
        public float PitchDegrees;
        public EntityFlags Flags;
        public byte Health;

        public bool IsGrounded => (Flags & EntityFlags.OnGround) != 0;
    }

    /// 스냅샷 히스토리와 보간.
    ///
    /// 서버는 30Hz 로 보내고 화면은 그보다 빠르게 그린다. 도착한 스냅샷을 그대로 적용하면
    /// 위치가 33ms 마다 계단으로 튀고, 걸음 속도를 실제 변위에서 재는 이 프로젝트에서는
    /// 그것이 곧 다리가 덜컥거리는 증상으로 나타난다. 그래서 항상 <see cref="Delay"/> 만큼
    /// 과거를 그린다. 지연 하나를 지불하고 부드러움과 손실 내성을 함께 산다.
    ///
    /// 보간 기준을 서버 틱이 아니라 로컬 도착 시각으로 잡는다. 시계 동기를 하지 않아도
    /// 지터가 그대로 흡수되고, 스냅샷이 하나 빠지면 구간이 길어질 뿐 끊기지 않는다.
    public sealed class SnapshotBuffer
    {
        /// 한 프레임에 담는 몸의 **상한**. 서버 정원(`RealtimeConstants.Rooms.MaxPlayers`, 지금 5)
        /// 보다 **크거나 같아야** 한다.
        ///
        /// 같은 값으로 못 박지 않는다. 클라이언트는 그 상수를 볼 수 없고(서버 모듈의 `internal`
        /// 이며 Unity 는 `Shared` 만 컴파일한다), **작을 때의 증상이 조용하다** — `Accept` 가
        /// `count` 를 이 값으로 잘라 넣으므로 정원이 늘어난 서버에 붙으면 뒤쪽 플레이어가 화면에
        /// 없는 상태가 되고, 로그는 남지 않는다. 그래서 여유를 두는 쪽으로 틀린다.
        ///
        /// 남는 칸의 비용은 배열 몇 개뿐이다(엔티티 13B + 명단 항목 하나).
        public const int MaxEntities = 8;

        /// 30Hz 기준 약 1초. 이보다 오래된 스냅샷은 쓸 일이 없다.
        private const int Capacity = 32;

        private readonly Frame[] _frames = new Frame[Capacity];
        private int _count;
        private int _head;   // 가장 최근이 들어간 칸

        public SnapshotBuffer(float delaySeconds)
        {
            Delay = delaySeconds;
            for (var index = 0; index < Capacity; index++)
            {
                _frames[index].Entities = new EntitySample[MaxEntities];
            }
        }

        /// 보간 버퍼 길이(초). 고정 파라미터의 클라이언트측 값이다.
        public float Delay { get; set; }

        public uint LatestTick { get; private set; }

        public int LatestEntityCount => _count == 0 ? 0 : _frames[_head].Count;

        public bool HasData => _count > 0;

        /// 디코드된 스냅샷을 넣는다. receivedAt 은 Time.unscaledTime 이다.
        public void Add(uint tick, EntityState[] entities, int count, float receivedAt)
        {
            // 순서가 뒤바뀐 스냅샷은 버린다. 지나간 상태를 최신으로 올리면 위치가 뒤로 튄다.
            if (_count > 0 && tick <= LatestTick)
            {
                return;
            }

            _head = (_head + 1) % Capacity;
            if (_count < Capacity)
            {
                _count++;
            }

            ref var frame = ref _frames[_head];
            frame.Tick = tick;
            frame.ReceivedAt = receivedAt;
            frame.Count = count > MaxEntities ? MaxEntities : count;

            for (var index = 0; index < frame.Count; index++)
            {
                frame.Entities[index] = Decode(entities[index]);
            }

            LatestTick = tick;
        }

        /// 지금 그려야 하는 시점의 엔티티 상태. 해당 id 가 스냅샷에 없으면 false.
        public bool TrySample(byte id, float now, out EntitySample sample)
        {
            sample = default;
            if (_count == 0)
            {
                return false;
            }

            var renderTime = now - Delay;

            // 최신에서 과거로 내려가며 renderTime 을 감싸는 두 프레임을 찾는다.
            for (var step = 0; step < _count - 1; step++)
            {
                ref var newer = ref _frames[Index(step)];
                ref var older = ref _frames[Index(step + 1)];

                if (newer.ReceivedAt < renderTime)
                {
                    // renderTime 이 최신보다도 미래다. 지연이 부족하거나 수신이 끊겼다.
                    break;
                }

                if (older.ReceivedAt > renderTime)
                {
                    continue;
                }

                if (!TryFind(ref older, id, out var from) || !TryFind(ref newer, id, out var to))
                {
                    break;
                }

                var span = newer.ReceivedAt - older.ReceivedAt;
                var t = span > 1e-5f ? Mathf.Clamp01((renderTime - older.ReceivedAt) / span) : 1f;

                sample = Lerp(from, to, t, span);
                return true;
            }

            // 감싸는 구간이 없으면 가장 최근 상태를 그대로 쓴다. 접속 직후와 수신 정지 시점이다.
            return TryFind(ref _frames[_head], id, out sample);
        }

        /// 보간하지 않은 가장 최근 상태. 로컬 플레이어에 쓴다 — 자기 캐릭터에까지
        /// 보간 지연을 얹으면 왕복 지연 위에 100ms 가 더해져 조작이 눈에 띄게 무거워진다.
        public bool TryLatest(byte id, out EntitySample sample)
        {
            if (_count == 0)
            {
                sample = default;
                return false;
            }

            return TryFind(ref _frames[_head], id, out sample);
        }

        /// 최신 스냅샷에 실린 엔티티 id 목록. 원격 플레이어의 입·퇴장 판정에 쓴다.
        public int ReadLatestIds(byte[] destination)
        {
            if (_count == 0)
            {
                return 0;
            }

            ref var frame = ref _frames[_head];
            var count = frame.Count < destination.Length ? frame.Count : destination.Length;

            for (var index = 0; index < count; index++)
            {
                destination[index] = frame.Entities[index].Id;
            }

            return count;
        }

        private int Index(int stepsBack)
        {
            var index = (_head - stepsBack) % Capacity;
            return index < 0 ? index + Capacity : index;
        }

        private static bool TryFind(ref Frame frame, byte id, out EntitySample sample)
        {
            for (var index = 0; index < frame.Count; index++)
            {
                if (frame.Entities[index].Id == id)
                {
                    sample = frame.Entities[index];
                    return true;
                }
            }

            sample = default;
            return false;
        }

        /// 속도는 와이어에 없다. 두 스냅샷의 위치 차이에서 만든다.
        /// 애니메이터가 점프와 낙하를 이 값으로 갈라 보므로 y 성분이 특히 필요하다.
        private static EntitySample Lerp(in EntitySample from, in EntitySample to, float t, float span)
        {
            var result = to;
            result.Position = Vector3.Lerp(from.Position, to.Position, t);
            result.YawDegrees = Mathf.LerpAngle(from.YawDegrees, to.YawDegrees, t);
            result.PitchDegrees = Mathf.Lerp(from.PitchDegrees, to.PitchDegrees, t);
            result.Velocity = span > 1e-5f ? (to.Position - from.Position) / span : Vector3.zero;
            return result;
        }

        private static EntitySample Decode(in EntityState state)
        {
            var sample = default(EntitySample);
            sample.Id = state.Id;
            sample.Position = new Vector3(
                Quantization.ToMeters(state.X),
                Quantization.ToMeters(state.Y),
                Quantization.ToMeters(state.Z));
            sample.YawDegrees = Quantization.ToYawRadians(state.Yaw) * Mathf.Rad2Deg;
            sample.PitchDegrees = Quantization.ToPitchRadians(state.Pitch) * Mathf.Rad2Deg;
            sample.Flags = state.Flags;
            sample.Health = state.Health;
            return sample;
        }

        private struct Frame
        {
            public uint Tick;
            public float ReceivedAt;
            public int Count;
            public EntitySample[] Entities;
        }
    }
}
