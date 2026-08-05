using System.Collections.Generic;
using System.Numerics;
using NV.Realtime.Simulation;
using NV.Shared.Collision;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 격자 위 경로 탐색(A\*). 체인 견인이 이것으로 끌고 간다.
    ///
    /// **이 파일이 지키는 것은 "벽을 뚫지 않는다" 하나다.** 체인의 첫 판은 제단까지 직선으로
    /// 보간했고, 화면에서는 Seeker 가 방 몇 개를 관통해 날아오는 것으로 보였다. 그 증상은
    /// 룸 테스트로는 잡히지 않는다 — `RoomFixture` 의 격자는 6×6 이 전부 통행 가능이라
    /// 직선과 우회 경로가 같기 때문이다. 그래서 벽이 있는 격자를 여기서 따로 만든다.
    public class GridRouteTests
    {
        /// 가로막힌 벽을 돌아간다. 지나는 모든 점이 통행 가능해야 한다.
        [Fact]
        public void 벽을_돌아간다()
        {
            // 7×7 한 층. z=3 줄에 x=0..5 까지 벽을 세운다. x=6 한 칸만 뚫려 있으므로
            // 반대편으로 가려면 반드시 그 틈으로 돌아야 한다.
            var grid = OpenGrid(floors: 1, width: 7, depth: 7);

            for (var x = 0; x <= 5; x++)
            {
                Block(grid, 0, x, 3);
            }

            var route = new List<Vector3>();
            var finder = new GridRoute(grid);

            var from = grid.CellToWorld(0, 0, 0);
            var to = grid.CellToWorld(0, 0, 6);

            Assert.True(finder.TryFind(from, to, route), "경로를 찾지 못했다.");

            AssertWalkable(grid, route);

            // 직선이면 벽을 뚫는다. 우회했으므로 직선거리보다 길어야 한다.
            Assert.True(
                GridRoute.Length(route) > Vector3.Distance(from, to),
                "경로가 직선거리보다 짧거나 같다 — 벽을 통과했다는 뜻이다.");
        }

        /// 벽 너머로 아예 길이 없으면 실패한다. **뚫고 가지 않는다.**
        ///
        /// 실패를 돌려주는 것이 규칙이다 — 호출하는 쪽(`Room.BeginChain`)이 그때 체인을
        /// 걸지 않는다. 한 번이라도 통과를 허용하면 그것이 규칙이 된다.
        [Fact]
        public void 길이_없으면_실패한다()
        {
            var grid = OpenGrid(floors: 1, width: 5, depth: 5);

            // z=2 줄을 통째로 막는다. 위아래가 완전히 갈린다.
            for (var x = 0; x < 5; x++)
            {
                Block(grid, 0, x, 2);
            }

            var route = new List<Vector3>();
            var finder = new GridRoute(grid);

            Assert.False(
                finder.TryFind(grid.CellToWorld(0, 0, 0), grid.CellToWorld(0, 0, 4), route),
                "갈 수 없는 곳으로 경로가 나왔다.");
        }

        /// 층은 계단으로만 넘는다. 계단이 있으면 넘고,
        [Fact]
        public void 계단이_있으면_층을_넘는다()
        {
            var grid = OpenGrid(floors: 2, width: 5, depth: 5);

            // 한 칸만 계단으로 잇는다.
            Link(grid, 0, 4, 4);
            Link(grid, 1, 4, 4);

            var route = new List<Vector3>();
            var finder = new GridRoute(grid);

            Assert.True(
                finder.TryFind(grid.CellToWorld(0, 0, 0), grid.CellToWorld(1, 0, 0), route),
                "계단이 있는데 층을 넘지 못했다.");

            AssertWalkable(grid, route);
        }

        /// 계단이 없으면 두 층은 서로 다른 섬이다. **천장을 뚫고 올라가지 않는다.**
        [Fact]
        public void 계단이_없으면_층을_넘지_못한다()
        {
            var grid = OpenGrid(floors: 2, width: 5, depth: 5);

            var route = new List<Vector3>();
            var finder = new GridRoute(grid);

            Assert.False(
                finder.TryFind(grid.CellToWorld(0, 0, 0), grid.CellToWorld(1, 0, 0), route),
                "계단이 없는데 층을 넘었다.");
        }

        /// 계단 셀은 `FreeFloor` 가 아니다(몸이 설 자리가 아니다). 그래도 **지나갈 수는**
        /// 있어야 한다 — `FreeFloor` 로 탐색하면 계단이 막힌 것이 되어 층이 갈린다.
        [Fact]
        public void 계단_셀은_설_수_없어도_지나갈_수_있다()
        {
            var grid = OpenGrid(floors: 2, width: 5, depth: 5);

            Link(grid, 0, 4, 4);
            Link(grid, 1, 4, 4);

            // 계단 셀에서 FreeFloor 를 지운다. 진짜 맵이 그렇다.
            Clear(grid, 0, 4, 4, MapCellFlags.FreeFloor);
            Clear(grid, 1, 4, 4, MapCellFlags.FreeFloor);

            var route = new List<Vector3>();
            var finder = new GridRoute(grid);

            Assert.True(
                finder.TryFind(grid.CellToWorld(0, 0, 0), grid.CellToWorld(1, 0, 0), route),
                "계단이 FreeFloor 가 아니라고 길이 막혔다.");
        }

        /// 시작점과 끝점은 셀 중심이 아니라 준 그대로다. 중심으로 스냅하면 견인이 시작하는
        /// 순간 몸이 최대 반 셀만큼 튄다.
        [Fact]
        public void 시작점과_끝점이_그대로_남는다()
        {
            var grid = OpenGrid(floors: 1, width: 5, depth: 5);

            var route = new List<Vector3>();
            var finder = new GridRoute(grid);

            var from = new Vector3(-9.3f, 0f, -9.7f);
            var to = new Vector3(6.1f, 0f, 5.2f);

            Assert.True(finder.TryFind(from, to, route));

            Assert.Equal(from, route[0]);
            Assert.Equal(to, route[route.Count - 1]);
        }

        /// 일직선 구간은 꺾임 점을 남기지 않는다. 셀마다 점을 남기면 체인이 링크 수백 개가 된다.
        [Fact]
        public void 곧은_구간은_점을_남기지_않는다()
        {
            var grid = OpenGrid(floors: 1, width: 9, depth: 9);

            var route = new List<Vector3>();
            var finder = new GridRoute(grid);

            // 완전히 열린 격자를 가로지른다. 꺾일 이유가 없으므로 점은 양 끝뿐이어야 한다.
            Assert.True(finder.TryFind(grid.CellToWorld(0, 0, 0), grid.CellToWorld(0, 8, 0), route));

            Assert.Equal(2, route.Count);
        }

        // ==================================================== 격자 만들기

        private static MapGridData OpenGrid(int floors, int width, int depth)
        {
            var grid = new MapGridData
            {
                Floors = floors,
                Width = width,
                Depth = depth,
                CellSize = 4f,
                FloorHeight = 4f,
                OriginX = -(width * 4f * 0.5f),
                OriginZ = -(depth * 4f * 0.5f),
                Cells = new byte[floors * width * depth],
            };

            for (var index = 0; index < grid.Cells.Length; index++)
            {
                grid.Cells[index] = (byte)(MapCellFlags.Standable | MapCellFlags.FreeFloor);
            }

            return grid;
        }

        private static void Block(MapGridData grid, int floor, int x, int z)
        {
            grid.Cells[grid.CellIndex(floor, x, z)] = (byte)MapCellFlags.None;
        }

        private static void Link(MapGridData grid, int floor, int x, int z)
        {
            grid.Cells[grid.CellIndex(floor, x, z)] |= (byte)MapCellFlags.StairLink;
        }

        private static void Clear(MapGridData grid, int floor, int x, int z, MapCellFlags flag)
        {
            var cell = grid.CellIndex(floor, x, z);
            grid.Cells[cell] = (byte)(((MapCellFlags)grid.Cells[cell]) & ~flag);
        }

        /// 폴리라인 위를 촘촘히 훑어 전부 통행 가능한 셀인지 본다.
        ///
        /// 꼭짓점만 보면 부족하다 — 두 꼭짓점이 모두 통행 가능해도 그 사이를 잇는 직선이
        /// 벽을 가로지를 수 있고, 그것이 정확히 고치려는 증상이다.
        private static void AssertWalkable(MapGridData grid, List<Vector3> route)
        {
            var length = GridRoute.Length(route);

            for (var step = 0f; step <= length; step += 0.25f)
            {
                var point = GridRoute.PointAlong(route, step);

                Assert.True(
                    grid.TryWorldToCell(point, out var floor, out var x, out var z),
                    $"{point} 가 격자 밖이다.");

                Assert.True(
                    grid.Has(floor, x, z, MapCellFlags.Standable),
                    $"{point}(층 {floor}, 셀 {x},{z}) 는 통행 불가인데 경로가 지나간다.");
            }
        }
    }
}
