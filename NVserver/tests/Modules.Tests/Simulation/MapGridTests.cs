using System.Numerics;
using NV.Shared.Collision;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Simulation
{
    /// 배치가 실제로 부르는 질의. 요구는 셋이다 — 돌려준 자리가 정말 설 수 있는 곳이고,
    /// 같은 씨드가 같은 자리를 고르고, 격자가 없을 때 조용히 원점을 내놓지 않는다.
    public class MapGridTests
    {
        /// 3×3 한 층. 가운데 열(x=1)만 몸이 들어가고, 나머지는 벽이거나 계단이다.
        private static MapGridData ColumnGrid()
        {
            var grid = new MapGridData
            {
                Floors = 1,
                Width = 3,
                Depth = 3,
                CellSize = 2f,
                FloorHeight = 3f,
                OriginX = 0f,
                OriginZ = 0f,
                Cells = new byte[9],
            };

            for (var z = 0; z < 3; z++)
            {
                grid.Cells[grid.CellIndex(0, 1, z)] =
                    (byte)(MapCellFlags.Standable | MapCellFlags.FreeFloor);
            }

            return grid;
        }

        // ==================================================== 무작위 선택

        [Fact]
        public void 무작위_선택은_항상_FreeFloor_셀을_돌려준다()
        {
            var grid = new MapGrid(ColumnGrid());
            var sequence = new DeterministicSequence(11);

            Assert.Equal(3, grid.FreeFloorCount);

            for (var draw = 0; draw < 200; draw++)
            {
                Assert.True(grid.TryRandomFreeFloor(ref sequence, out var feet));
                Assert.True(
                    grid.Data.TryWorldToCell(feet, out var floor, out var x, out var z),
                    $"돌려준 좌표 {feet} 가 격자 밖이다.");
                Assert.True(grid.Data.Has(floor, x, z, MapCellFlags.FreeFloor));
                Assert.Equal(1, x);
            }
        }

        /// 클라이언트와 서버가 같은 배치를 계산해야 하므로 재현성이 필수다.
        [Fact]
        public void 같은_씨드는_같은_자리를_고른다()
        {
            var grid = new MapGrid(ColumnGrid());

            var a = new DeterministicSequence(4242);
            var b = new DeterministicSequence(4242);

            for (var draw = 0; draw < 64; draw++)
            {
                Assert.True(grid.TryRandomFreeFloor(ref a, out var first));
                Assert.True(grid.TryRandomFreeFloor(ref b, out var second));
                Assert.Equal(first, second);
            }
        }

        /// 수열을 `ref` 로 받지 않으면 호출자의 상태가 진행하지 않아 매번 같은 자리가
        /// 나온다. 증상은 "목표물이 전부 한 자리에 겹침" 이다.
        [Fact]
        public void 뽑을수록_수열이_진행한다()
        {
            var grid = new MapGrid(ColumnGrid());
            var sequence = new DeterministicSequence(7);

            var before = sequence.State;
            grid.TryRandomFreeFloor(ref sequence, out _);

            Assert.NotEqual(before, sequence.State);
        }

        [Fact]
        public void 후보가_여러_개면_같은_자리만_나오지_않는다()
        {
            var grid = new MapGrid(ColumnGrid());
            var sequence = new DeterministicSequence(20260804);

            var distinct = 0;
            var seen = new bool[3];

            for (var draw = 0; draw < 300; draw++)
            {
                grid.TryRandomFreeFloor(ref sequence, out var feet);
                grid.Data.TryWorldToCell(feet, out _, out _, out var z);

                if (!seen[z])
                {
                    seen[z] = true;
                    distinct++;
                }
            }

            Assert.Equal(3, distinct);
        }

        // ==================================================== 가장 가까운 자리

        [Fact]
        public void 가장_가까운_자리는_자기_셀이면_그대로다()
        {
            var data = ColumnGrid();
            var grid = new MapGrid(data);
            var target = data.CellToWorld(0, 1, 1);

            Assert.True(grid.TryNearestFreeFloor(target, out var feet));
            Assert.Equal(target, feet);
        }

        /// 벽 안에서 부르는 것이 이 함수의 본래 용도다 — 순간이동이 유효하지 않은 자리에
        /// 떨어졌을 때 되돌릴 자리를 찾는다.
        [Fact]
        public void 벽_안에서_부르면_옆의_FreeFloor_를_찾는다()
        {
            var data = ColumnGrid();
            var grid = new MapGrid(data);

            // (0,0,1) 은 벽이다. 한 칸 옆 (0,1,1) 이 답이어야 한다.
            Assert.False(data.Has(0, 0, 1, MapCellFlags.FreeFloor));

            Assert.True(grid.TryNearestFreeFloor(data.CellToWorld(0, 0, 1), out var feet));
            Assert.True(data.TryWorldToCell(feet, out _, out var x, out _));
            Assert.Equal(1, x);
        }

        [Fact]
        public void 격자_밖에서_부르면_가장자리에서_안쪽으로_찾아_들어온다()
        {
            var data = ColumnGrid();
            var grid = new MapGrid(data);

            Assert.True(grid.TryNearestFreeFloor(new Vector3(-50f, 0f, -50f), out var feet));
            Assert.True(data.TryWorldToCell(feet, out _, out var x, out _));
            Assert.Equal(1, x);
        }

        /// 격자 거리로는 위층 셀이 가장 가까울 수 있지만 그리로 걸어갈 수는 없다.
        [Fact]
        public void 가장_가까운_자리를_다른_층에서_찾지_않는다()
        {
            var data = new MapGridData
            {
                Floors = 2,
                Width = 3,
                Depth = 3,
                CellSize = 2f,
                FloorHeight = 3f,
                OriginX = 0f,
                OriginZ = 0f,
                Cells = new byte[18],
            };

            // 몸이 들어가는 셀은 위층에만 있다.
            data.Cells[data.CellIndex(1, 1, 1)] =
                (byte)(MapCellFlags.Standable | MapCellFlags.FreeFloor);

            var grid = new MapGrid(data);

            // 아래층에서 물으면 같은 층에 답이 없으므로 실패해야 한다.
            Assert.False(grid.TryNearestFreeFloor(data.CellToWorld(0, 1, 1), out _));

            // 위층에서 물으면 찾는다.
            Assert.True(grid.TryNearestFreeFloor(data.CellToWorld(1, 1, 1), out var feet));
            Assert.Equal(data.CellToWorld(1, 1, 1), feet);
        }

        // ==================================================== 격자가 없을 때

        /// 조용히 원점을 돌려주면 목표물이 전부 (0,0,0) 에 생긴다. 실패로 답해야
        /// 호출자가 거절할 수 있다.
        [Fact]
        public void FreeFloor_가_하나도_없으면_실패로_답한다()
        {
            var data = ColumnGrid();
            for (var index = 0; index < data.Cells.Length; index++)
            {
                // Standable 만 남기고 FreeFloor 를 지운다.
                data.Cells[index] &= (byte)~MapCellFlags.FreeFloor;
            }

            var grid = new MapGrid(data);
            var sequence = new DeterministicSequence(3);

            Assert.Equal(0, grid.FreeFloorCount);
            Assert.False(grid.TryRandomFreeFloor(ref sequence, out _));
            Assert.False(grid.TryNearestFreeFloor(data.CellToWorld(0, 1, 1), out _));
        }

        [Fact]
        public void 셀_배열이_없어도_던지지_않는다()
        {
            var grid = new MapGrid(new MapGridData { Floors = 1, Width = 1, Depth = 1 });
            var sequence = new DeterministicSequence(3);

            Assert.Equal(0, grid.FreeFloorCount);
            Assert.False(grid.TryRandomFreeFloor(ref sequence, out _));
        }

        [Fact]
        public void 격자가_null_이어도_던지지_않는다()
        {
            var grid = new MapGrid(null);
            var sequence = new DeterministicSequence(3);

            Assert.Equal(0, grid.FreeFloorCount);
            Assert.False(grid.TryRandomFreeFloor(ref sequence, out _));
            Assert.False(grid.TryNearestFreeFloor(new Vector3(0f, 0f, 0f), out _));
        }

        // ==================================================== 좌표 왕복

        [Fact]
        public void 셀에서_월드로_갔다_돌아오면_같은_셀이다()
        {
            var data = ColumnGrid();

            for (var x = 0; x < data.Width; x++)
            {
                for (var z = 0; z < data.Depth; z++)
                {
                    var world = data.CellToWorld(0, x, z);

                    Assert.True(data.TryWorldToCell(world, out var floor, out var backX, out var backZ));
                    Assert.Equal(0, floor);
                    Assert.Equal(x, backX);
                    Assert.Equal(z, backZ);
                }
            }
        }
    }
}
