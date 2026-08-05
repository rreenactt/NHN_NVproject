using System;
using System.Collections.Generic;
using System.Numerics;
using NV.Shared.Collision;

namespace NV.Realtime.Simulation
{
    /// 걸어갈 수 있는 경로를 격자 위에서 찾는다. A\* 다.
    ///
    /// **체인 견인이 이것을 쓴다.** 그전에는 제단까지 직선으로 끌어당겼고, 그것은 벽을 뚫고
    /// 지나갔다 — 미로 반대편에서 걸리면 Seeker 가 방 몇 개를 관통해 날아왔다. 벌칙의 값어치는
    /// "걸어서 온 거리를 통째로 되돌린다" 인데, 직선으로 끌면 그 거리가 아니라 지도상의 간격이
    /// 되어서 멀리 돌아온 사람일수록 덜 손해를 본다.
    ///
    /// **`Standable` 위를 걷지, `FreeFloor` 위를 걷지 않는다.** 둘의 차이가 여기서 결정적이다 —
    /// 계단 셀은 격자상 통행 가능하지만 몸이 서는 자리는 아니라서 `FreeFloor` 가 빠져 있다.
    /// `FreeFloor` 로 탐색하면 계단이 통째로 막힌 것이 되어 층을 넘는 경로가 아예 없다.
    /// 반대로 **도착지**는 `FreeFloor` 여야 한다(제단 옆자리가 그렇다).
    ///
    /// 층은 <see cref="MapCellFlags.StairLink"/> 로만 넘는다. 그 플래그가 계단이 지나가는 셀에
    /// 붙어 있고(생성기가 계단 사각형 전체에 칠한다), 그것이 없으면 두 층은 서로 다른 섬이다.
    ///
    /// 대각선으로 가지 않는다. 4방향이면 모서리를 스쳐 벽을 통과하는 경우를 따로 막지 않아도
    /// 되고, 체인은 어차피 꺾이는 것이 보기 좋다.
    ///
    /// 버퍼를 들고 재사용한다. 틱 루프에서 부르므로(탄창을 비울 때마다 한 번) 호출마다 격자
    /// 크기의 배열을 새로 잡지 않는다.
    internal sealed class GridRoute
    {
        private readonly MapGridData _grid;
        private readonly float[] _cost;
        private readonly int[] _cameFrom;
        private readonly bool[] _closed;
        private readonly List<int> _open = new();

        public GridRoute(MapGridData grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));

            var cells = grid.CellCount;
            _cost = new float[cells];
            _cameFrom = new int[cells];
            _closed = new bool[cells];
        }

        /// <paramref name="from"/> 에서 <paramref name="to"/> 까지 걸어가는 경로의 꺾이는 점들.
        ///
        /// 첫 점은 <paramref name="from"/>, 마지막 점은 <paramref name="to"/> 다 — 둘 다 셀
        /// 중심이 아니라 진짜 좌표이므로, 견인이 시작점에서 튀거나 도착점을 빗나가지 않는다.
        ///
        /// 경로가 없으면 false. 호출한 쪽이 그때 무엇을 할지 정한다.
        public bool TryFind(Vector3 from, Vector3 to, List<Vector3> route)
        {
            route.Clear();

            if (!_grid.TryWorldToCell(from, out var startFloor, out var startX, out var startZ)
                || !_grid.TryWorldToCell(to, out var goalFloor, out var goalX, out var goalZ))
            {
                return false;
            }

            var start = _grid.CellIndex(startFloor, startX, startZ);
            var goal = _grid.CellIndex(goalFloor, goalX, goalZ);

            // 출발 셀이 통행 불가일 수 있다. 몸이 계단 위나 기물 위에 있을 때가 그렇다 —
            // 그 경우 가장 가까운 통행 가능한 셀에서 출발한다. 막고 끝내면 그 자리에서
            // 걸린 Seeker 는 영원히 끌려가지 않는다.
            if (!Walkable(start) && !TryNearestWalkable(startFloor, startX, startZ, out start))
            {
                return false;
            }

            if (!Walkable(goal) && !TryNearestWalkable(goalFloor, goalX, goalZ, out goal))
            {
                return false;
            }

            if (!Search(start, goal))
            {
                return false;
            }

            Rebuild(start, goal, from, to, route);
            return true;
        }

        /// 폴리라인의 총 길이(m).
        public static float Length(List<Vector3> route)
        {
            var total = 0f;

            for (var index = 1; index < route.Count; index++)
            {
                total += Vector3.Distance(route[index - 1], route[index]);
            }

            return total;
        }

        /// 폴리라인을 따라 <paramref name="distance"/> 만큼 걸어간 지점.
        public static Vector3 PointAlong(List<Vector3> route, float distance)
        {
            if (route.Count == 0)
            {
                return Vector3.Zero;
            }

            if (distance <= 0f || route.Count == 1)
            {
                return route[0];
            }

            var remaining = distance;

            for (var index = 1; index < route.Count; index++)
            {
                var step = Vector3.Distance(route[index - 1], route[index]);

                if (remaining <= step)
                {
                    var t = step <= 1e-4f ? 1f : remaining / step;
                    return Vector3.Lerp(route[index - 1], route[index], t);
                }

                remaining -= step;
            }

            return route[route.Count - 1];
        }

        // ==================================================== 탐색

        private bool Search(int start, int goal)
        {
            Array.Clear(_closed, 0, _closed.Length);
            _open.Clear();

            for (var index = 0; index < _cost.Length; index++)
            {
                _cost[index] = float.MaxValue;
                _cameFrom[index] = -1;
            }

            _cost[start] = 0f;
            _open.Add(start);

            while (_open.Count > 0)
            {
                var current = TakeBest(goal);

                if (current == goal)
                {
                    return true;
                }

                _closed[current] = true;

                _grid.CellCoords(current, out var floor, out var x, out var z);

                Relax(current, floor, x + 1, z, _grid.CellSize);
                Relax(current, floor, x - 1, z, _grid.CellSize);
                Relax(current, floor, x, z + 1, _grid.CellSize);
                Relax(current, floor, x, z - 1, _grid.CellSize);

                // 층 이동. 양쪽 셀 모두 계단이어야 한다 — 한쪽만 보면 계단이 없는 층에서
                // 위로 솟아오르는 경로가 나온다.
                if (_grid.Has(floor, x, z, MapCellFlags.StairLink))
                {
                    RelayStair(current, floor + 1, x, z);
                    RelayStair(current, floor - 1, x, z);
                }
            }

            return false;
        }

        private void RelayStair(int current, int floor, int x, int z)
        {
            if (!_grid.InBounds(floor, x, z) || !_grid.Has(floor, x, z, MapCellFlags.StairLink))
            {
                return;
            }

            Relax(current, floor, x, z, _grid.FloorHeight);
        }

        private void Relax(int current, int floor, int x, int z, float step)
        {
            if (!_grid.InBounds(floor, x, z))
            {
                return;
            }

            var next = _grid.CellIndex(floor, x, z);

            if (_closed[next] || !Walkable(next))
            {
                return;
            }

            var cost = _cost[current] + step;

            if (cost >= _cost[next])
            {
                return;
            }

            _cost[next] = cost;
            _cameFrom[next] = current;

            if (!_open.Contains(next))
            {
                _open.Add(next);
            }
        }

        /// 열린 목록에서 f 가 가장 작은 것을 꺼낸다.
        ///
        /// 이진 힙을 쓰지 않는다. 격자가 2450 셀(backrooms) 이고 이 함수는 탄창을 비울 때마다
        /// 한 번 도는 것이라, 선형 훑기의 비용이 힙을 들이는 복잡도보다 싸다. 격자가 훨씬
        /// 커지면 그때 바꾼다.
        private int TakeBest(int goal)
        {
            var bestIndex = 0;
            var bestScore = float.MaxValue;

            for (var index = 0; index < _open.Count; index++)
            {
                var cell = _open[index];
                var score = _cost[cell] + Heuristic(cell, goal);

                if (score >= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bestIndex = index;
            }

            var best = _open[bestIndex];
            _open.RemoveAt(bestIndex);

            return best;
        }

        /// 맨해튼 거리. 4방향으로만 움직이므로 절대 실제 비용을 넘지 않는다(허용적이다).
        private float Heuristic(int cell, int goal)
        {
            _grid.CellCoords(cell, out var floor, out var x, out var z);
            _grid.CellCoords(goal, out var goalFloor, out var goalX, out var goalZ);

            return ((Math.Abs(x - goalX) + Math.Abs(z - goalZ)) * _grid.CellSize)
                + (Math.Abs(floor - goalFloor) * _grid.FloorHeight);
        }

        private void Rebuild(int start, int goal, Vector3 from, Vector3 to, List<Vector3> route)
        {
            // 거꾸로 모은 뒤 뒤집는다.
            var reversed = new List<int>();

            for (var cell = goal; cell != -1; cell = _cameFrom[cell])
            {
                reversed.Add(cell);

                if (cell == start)
                {
                    break;
                }
            }

            reversed.Reverse();

            route.Add(from);

            // 일직선으로 이어지는 셀은 버린다. 남는 것이 꺾이는 점이고, 체인은 그 점마다
            // 한 마디를 잃는다 — 셀마다 한 점을 남기면 3m 짜리 링크가 수백 개 생긴다.
            for (var index = 1; index < reversed.Count - 1; index++)
            {
                _grid.CellCoords(reversed[index - 1], out var previousFloor, out var previousX, out var previousZ);
                _grid.CellCoords(reversed[index], out var floor, out var x, out var z);
                _grid.CellCoords(reversed[index + 1], out var nextFloor, out var nextX, out var nextZ);

                var straight = (x - previousX) == (nextX - x)
                    && (z - previousZ) == (nextZ - z)
                    && (floor - previousFloor) == (nextFloor - floor);

                if (straight)
                {
                    continue;
                }

                route.Add(_grid.CellToWorld(floor, x, z));
            }

            route.Add(to);
        }

        private bool TryNearestWalkable(int floor, int x, int z, out int cell)
        {
            // 같은 층에서 바깥으로 넓혀 가며 찾는다. 층을 넘어 찾으면 계단을 지나지 않고
            // 위층으로 건너뛰는 출발점이 나온다.
            for (var radius = 1; radius <= 4; radius++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    for (var dz = -radius; dz <= radius; dz++)
                    {
                        if (Math.Abs(dx) != radius && Math.Abs(dz) != radius)
                        {
                            continue;
                        }

                        if (!_grid.InBounds(floor, x + dx, z + dz))
                        {
                            continue;
                        }

                        var candidate = _grid.CellIndex(floor, x + dx, z + dz);

                        if (Walkable(candidate))
                        {
                            cell = candidate;
                            return true;
                        }
                    }
                }
            }

            cell = -1;
            return false;
        }

        private bool Walkable(int cell)
        {
            return (((MapCellFlags)_grid.Cells[cell]) & MapCellFlags.Standable) == MapCellFlags.Standable;
        }

    }
}
