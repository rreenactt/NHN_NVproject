using System.Numerics;
using NV.Shared.Simulation;

namespace NV.Shared.Collision
{
    /// 격자에 대한 질의. 목표물 배치와 순간이동 착지점이 이것을 쓴다.
    ///
    /// `MapGridData` 와 나누는 이유는 수명이 다르기 때문이다. 그쪽은 JSON 스키마이고
    /// 파일에서 그대로 읽히는 자료이며, 이쪽은 그 자료에서 **한 번 계산해 들고 있는**
    /// 것이다 — 후보 목록을 매 질의마다 다시 만들면 배치 한 번에 격자를 열 번 훑는다.
    ///
    /// 불변이다. 맵은 로드 후 변하지 않으므로 틱 루프와 조회 스레드가 함께 읽어도 된다.
    public sealed class MapGrid
    {
        private static readonly Vector3[] NoPoints = new Vector3[0];

        /// `FreeFloor` 인 셀의 발밑 좌표. 무작위 선택이 이 배열에서 뽑는다.
        ///
        /// 좌표로 미리 바꿔 둔다. 셀 인덱스를 저장하면 뽑을 때마다 역변환(인덱스 →
        /// floor·x·z)이 필요하고, 그 식은 `CellIndex` 의 역이라 두 곳에서 어긋날 수 있다.
        private readonly Vector3[] _freeFloor;

        public MapGrid(MapGridData data)
        {
            Data = data;

            if (data == null || data.Cells == null)
            {
                _freeFloor = NoPoints;
                return;
            }

            var count = 0;
            for (var index = 0; index < data.Cells.Length; index++)
            {
                if ((((MapCellFlags)data.Cells[index]) & MapCellFlags.FreeFloor) == MapCellFlags.FreeFloor)
                {
                    count++;
                }
            }

            if (count == 0)
            {
                _freeFloor = NoPoints;
                return;
            }

            _freeFloor = new Vector3[count];
            var next = 0;

            // 층 → z → x 순으로 훑는다. 순서가 곧 무작위 선택의 색인이므로, 같은 맵에서
            // 같은 씨드가 항상 같은 자리를 고르려면 이 순서가 고정되어 있어야 한다.
            for (var floor = 0; floor < data.Floors; floor++)
            {
                for (var z = 0; z < data.Depth; z++)
                {
                    for (var x = 0; x < data.Width; x++)
                    {
                        if (data.Has(floor, x, z, MapCellFlags.FreeFloor))
                        {
                            _freeFloor[next] = data.CellToWorld(floor, x, z);
                            next++;
                        }
                    }
                }
            }
        }

        public MapGridData Data { get; }

        /// 몸이 들어가는 셀의 수. 0 이면 이 맵에 배치할 수 없다.
        public int FreeFloorCount => _freeFloor.Length;

        /// 무작위 `FreeFloor` 셀의 **중심**.
        ///
        /// 수열을 `ref` 로 받는다. 값으로 받으면 호출자의 상태가 진행하지 않아 **매번
        /// 같은 자리**가 나오고, 증상은 "목표물이 전부 한 자리에 겹침" 이다.
        ///
        /// **셀 안에서 흔들지 않는다.** 클라이언트의 같은 함수는 `margin` 0.55 로 지터를
        /// 주어 열쇠 10개가 격자에 정렬되지 않게 하는데, 그 지터는 `FreeFloor` 의 보장
        /// 밖이다 — 이 플래그는 셀 **중심**에서 플레이어 박스를 검사해 세워진 값이고,
        /// 셀 중심에서 벽 내측면까지는 1.375m 인데 지터 폭이 0.95m 이면 여유가 0.425m 로
        /// 줄어 반지름 0.4 인 서버 박스와 0.025m 차이다. 열쇠는 콜라이더가 없어 무해하지만
        /// 순간이동 착지점은 플레이어다. 지터가 필요하면 흔든 뒤 `MapGridBuilder.IsFree`
        /// 로 다시 검사해야 하고, 그것은 배치 태스크(IG-011)의 몫이다.
        public bool TryRandomFreeFloor(ref DeterministicSequence sequence, out Vector3 feet)
        {
            if (_freeFloor.Length == 0)
            {
                feet = new Vector3(0f, 0f, 0f);
                return false;
            }

            feet = _freeFloor[sequence.NextInt(_freeFloor.Length)];
            return true;
        }

        /// 주어진 곳에서 가장 가까운 `FreeFloor` 셀의 중심. **같은 층에서만 찾는다.**
        ///
        /// 층을 넘어가며 찾지 않는 이유는 거리가 뜻을 잃기 때문이다. 격자 거리로는 바로
        /// 위층 셀이 가장 가깝지만 그리로 걸어갈 수는 없다. 순간이동이 유효하지 않은
        /// 자리에 떨어졌을 때 되돌릴 자리를 찾는 용도이므로, 있던 층에 남는 것이 맞다.
        ///
        /// 정사각 링을 반지름 순으로 넓히며 처음 걸린 것을 쓴다. 링 안에서는 유클리드
        /// 거리로 더 가까운 셀이 나중에 나올 수 있지만, 셀 크기 단위의 차이이고 이 함수의
        /// 목적은 최적해가 아니라 "벽 안이 아닌 곳" 이다.
        public bool TryNearestFreeFloor(Vector3 near, out Vector3 feet)
        {
            feet = near;

            if (Data == null || _freeFloor.Length == 0)
            {
                return false;
            }

            Data.TryWorldToCell(near, out var floor, out var centerX, out var centerZ);

            // **시작 셀을 격자 안으로 당겨 놓는다.** 링의 반지름 상한이 격자 크기라,
            // 격자 밖 먼 좌표에서 그대로 시작하면 링이 격자에 닿기 전에 상한에 걸려
            // "찾지 못했다" 로 끝난다. 격자에서 50m 떨어진 곳을 물으면 셀 좌표가
            // -25 쯤 나오는데 상한은 35 이하다. 당겨 놓은 자리가 곧 그 좌표에서 가장
            // 가까운 격자 셀이므로 답의 뜻도 달라지지 않는다.
            floor = Clamp(floor, 0, Data.Floors - 1);
            centerX = Clamp(centerX, 0, Data.Width - 1);
            centerZ = Clamp(centerZ, 0, Data.Depth - 1);

            var maxRadius = Data.Width > Data.Depth ? Data.Width : Data.Depth;

            for (var radius = 0; radius <= maxRadius; radius++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    for (var dz = -radius; dz <= radius; dz++)
                    {
                        // 링만 본다. 안쪽은 이전 반지름에서 이미 검사했다.
                        var ring = (dx > 0 ? dx : -dx) > (dz > 0 ? dz : -dz)
                            ? (dx > 0 ? dx : -dx)
                            : (dz > 0 ? dz : -dz);

                        if (ring != radius)
                        {
                            continue;
                        }

                        var x = centerX + dx;
                        var z = centerZ + dz;

                        if (!Data.Has(floor, x, z, MapCellFlags.FreeFloor))
                        {
                            continue;
                        }

                        feet = Data.CellToWorld(floor, x, z);
                        return true;
                    }
                }
            }

            return false;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
