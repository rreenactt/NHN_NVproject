using System.Collections.Generic;
using System.Numerics;
using NV.Shared.Contracts.Enums;

namespace NV.Shared.Simulation
{
    /// 놓인 장치 하나.
    public readonly struct DevicePlacement
    {
        public DevicePlacement(MatchDeviceType type, Vector3 position, float yaw)
        {
            Type = type;
            Position = position;
            Yaw = yaw;
        }

        public MatchDeviceType Type { get; }

        /// 발밑 기준. 서버의 위치 규약이 그렇다.
        public Vector3 Position { get; }

        /// 라디안. 0 이 +Z 다 — 스폰 yaw 와 같은 규약이다.
        public float Yaw { get; }
    }

    /// 한 매치의 목표물 배치. 서버가 정하고 룸이 소유한다.
    ///
    /// **이것이 서버로 옮겨 오는 것이 이 루프의 보안 목표다.** 지금까지 배치는
    /// `RoomStateHeader.PlacementSeed` 를 받아 **모든 클라이언트가 같은 씨드로 계산**했고,
    /// 그래서 문의 좌표가 Seeker 의 프로세스 메모리에도 들어 있었다. 룰셋은 문이 Runner 에게만
    /// 보여야 한다고 정하지만, WebGL 빌드가 디컴파일되는 전제에서 컬링 레이어로 막을 수 있는
    /// 종류의 정보가 아니다. 서버가 배치하고 역할별로 걸러 좌표를 내려보내야 닫힌다.
    ///
    /// 이 클래스는 **결과만** 담는다. 배치 규칙은 `ObjectivePlacement` 에 있다.
    ///
    /// **`Shared` 에 있는 이유는 ADR 0002 다.** 클라이언트가 오프라인 연습에서 같은 배치를
    /// 계산하므로 같은 코드를 쓴다. 네트워크 매치에서는 서버가 계산한 좌표만 전문으로
    /// 내려오고, 씨드가 와이어에 없으므로 이 코드를 가진 Seeker 클라이언트도 문을 계산할
    /// 수 없다 — 막아야 하는 것은 코드가 아니라 입력이다.
    public sealed class Objectives
    {
        private readonly List<Vector3> _keys = new();
        private readonly List<DevicePlacement> _devices = new();

        /// 배치가 성공했는가. 격자가 없는 맵에서는 false 다.
        public bool Placed { get; private set; }

        /// 체인 제단의 위치. 기획서 §4.3 의 강제 이동 지점이다.
        ///
        /// **매치마다 움직이지 않는다** — 격자 중앙에서 가장 가까운, 몸이 들어가는 셀이다.
        /// Seeker 는 세 번째 총알이 자기를 어디로 보낼지 알아야 하고, 예측할 수 없는 벌칙은
        /// 그저 짜증이다.
        public Vector3 AltarPosition { get; private set; }

        /// 체인이 Seeker 를 실제로 내려놓는 자리. 제단 옆의 몸이 들어가는 셀이다.
        ///
        /// 제단 자리 자체에 내려놓지 않는다 — 그 셀은 제단이 차지하고 있다.
        public Vector3 AltarDragPoint { get; private set; }

        /// 탈출 문. 기획서 §6 — 매치마다 무작위 위치이고 **Runner 에게만 보인다.**
        public Vector3 DoorPosition { get; private set; }

        public float DoorYaw { get; private set; }

        /// 아직 주워지지 않은 열쇠의 위치.
        public IReadOnlyList<Vector3> Keys => _keys;

        public IReadOnlyList<DevicePlacement> Devices => _devices;

        public void Reset()
        {
            Placed = false;
            AltarPosition = default;
            AltarDragPoint = default;
            DoorPosition = default;
            DoorYaw = 0f;
            _keys.Clear();
            _devices.Clear();
        }

        public void SetAltar(Vector3 position, Vector3 dragPoint)
        {
            AltarPosition = position;
            AltarDragPoint = dragPoint;
        }

        public void SetDoor(Vector3 position, float yaw)
        {
            DoorPosition = position;
            DoorYaw = yaw;
        }

        public void AddKey(Vector3 position)
        {
            _keys.Add(position);
        }

        /// 주워진 열쇠를 목록에서 뺀다. 인덱스로 지운다 — 좌표 비교로 찾으면 같은 자리에
        /// 두 개가 놓인 경우(간격 조건을 포기한 경우)에 엉뚱한 것이 사라진다.
        public void RemoveKeyAt(int index)
        {
            if (index >= 0 && index < _keys.Count)
            {
                _keys.RemoveAt(index);
            }
        }

        public void AddDevice(in DevicePlacement device)
        {
            _devices.Add(device);
        }

        public void MarkPlaced()
        {
            Placed = true;
        }
    }
}
