using NV.Shared.Collision;
using Xunit;

namespace NV.Modules.Tests.Simulation
{
    /// 격자 스키마. 이 자료는 서버가 계산하지 않고 클라이언트에서 받아 신뢰하므로,
    /// 어긋난 격자를 조용히 받아들이지 않는 것이 이 테스트의 목적이다.
    public class MapGridDataTests
    {
        private static MapGridData Grid(int floors = 2, int width = 4, int depth = 3)
        {
            return new MapGridData
            {
                Floors = floors,
                Width = width,
                Depth = depth,
                CellSize = 3f,
                FloorHeight = 3.2f,
                OriginX = -6f,
                OriginZ = -4.5f,
                Cells = new byte[floors * width * depth],
            };
        }

        /// 인덱스 식이 어긋나면 격자가 돌아간 채로 크기와 해시가 모두 맞는다.
        /// 그래서 유일성을 직접 확인한다.
        [Fact]
        public void 셀_인덱스는_모든_좌표에_대해_유일하다()
        {
            var grid = Grid();
            var seen = new bool[grid.CellCount];

            for (var floor = 0; floor < grid.Floors; floor++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    for (var z = 0; z < grid.Depth; z++)
                    {
                        var index = grid.CellIndex(floor, x, z);

                        Assert.InRange(index, 0, grid.CellCount - 1);
                        Assert.False(seen[index], $"인덱스 {index} 가 ({floor},{x},{z}) 에서 다시 나왔다.");
                        seen[index] = true;
                    }
                }
            }

            Assert.DoesNotContain(false, seen);
        }

        [Fact]
        public void 플래그를_넣은_셀만_그_플래그를_갖는다()
        {
            var grid = Grid();
            grid.Cells[grid.CellIndex(1, 2, 1)] =
                (byte)(MapCellFlags.Standable | MapCellFlags.FreeFloor);

            Assert.True(grid.Has(1, 2, 1, MapCellFlags.Standable));
            Assert.True(grid.Has(1, 2, 1, MapCellFlags.FreeFloor));
            Assert.False(grid.Has(1, 2, 1, MapCellFlags.StairLink));

            Assert.Equal(MapCellFlags.None, grid.At(0, 2, 1));
            Assert.False(grid.Has(0, 2, 1, MapCellFlags.Standable));
        }

        /// 배치 후보를 훑는 코드는 경계에서 좌표를 하나씩 넘겨본다.
        /// 그것이 정상 경로이므로 예외가 아니라 `None` 이어야 한다.
        [Theory]
        [InlineData(-1, 0, 0)]
        [InlineData(0, -1, 0)]
        [InlineData(0, 0, -1)]
        [InlineData(2, 0, 0)]
        [InlineData(0, 4, 0)]
        [InlineData(0, 0, 3)]
        public void 범위_밖은_None_이고_예외를_던지지_않는다(int floor, int x, int z)
        {
            var grid = Grid();

            Assert.False(grid.InBounds(floor, x, z));
            Assert.Equal(MapCellFlags.None, grid.At(floor, x, z));
        }

        [Fact]
        public void 셀_수가_크기와_맞으면_통과한다()
        {
            Assert.True(Grid().TryValidate(out var error));
            Assert.Null(error);
        }

        [Fact]
        public void 셀_수가_크기와_맞지_않으면_거절한다()
        {
            var grid = Grid();
            grid.Cells = new byte[grid.CellCount - 1];

            Assert.False(grid.TryValidate(out var error));
            Assert.Contains("셀 수", error);
        }

        [Theory]
        [InlineData(0, 4, 3)]
        [InlineData(2, 0, 3)]
        [InlineData(2, 4, 0)]
        public void 크기가_0_이면_거절한다(int floors, int width, int depth)
        {
            var grid = Grid();
            grid.Floors = floors;
            grid.Width = width;
            grid.Depth = depth;

            Assert.False(grid.TryValidate(out var error));
            Assert.Contains("격자 크기", error);
        }

        [Fact]
        public void 셀_배열이_없으면_거절한다()
        {
            var grid = Grid();
            grid.Cells = null;

            Assert.False(grid.TryValidate(out var error));
            Assert.Contains("셀 배열", error);
        }

        // ==================================================== 해시

        private static MapData MapWithBox()
        {
            return new MapData
            {
                Name = "t",
                Boxes = new[]
                {
                    new MapBox { MinX = 0f, MinY = 0f, MinZ = 0f, MaxX = 1f, MaxY = 1f, MaxZ = 1f },
                },
                Spawns = new[] { new MapSpawn { X = 0f, Y = 0f, Z = 0f, Yaw = 0f } },
            };
        }

        /// 격자를 도입하는 커밋이 기존 맵 파일의 해시를 바꾸지 않아야 한다.
        /// 바꾸면 아무 정보도 늘지 않는 re-export 를 전부 강요한다.
        [Fact]
        public void 격자가_없으면_해시에_영향이_없다()
        {
            var map = MapWithBox();
            var withoutGrid = map.ComputeHash();

            map.Grid = null;

            Assert.Equal(withoutGrid, map.ComputeHash());
            Assert.False(map.HasGrid);
        }

        /// 반대로 격자가 있으면 반드시 해시에 들어가야 한다. 빠지면 격자가 어긋난
        /// 채로 해시가 일치하고, 이동 판정은 격자를 쓰지 않으므로 걸어 다니는
        /// 동안에는 아무 신호도 나지 않는다.
        [Fact]
        public void 격자를_붙이면_해시가_바뀌고_떼면_돌아온다()
        {
            var map = MapWithBox();
            var bare = map.ComputeHash();

            map.Grid = Grid();
            var withGrid = map.ComputeHash();

            Assert.NotEqual(bare, withGrid);
            Assert.True(map.HasGrid);

            map.Grid = null;
            Assert.Equal(bare, map.ComputeHash());
        }

        [Fact]
        public void 셀_하나만_달라도_해시가_다르다()
        {
            var map = MapWithBox();
            map.Grid = Grid();
            var before = map.ComputeHash();

            map.Grid.Cells[5] = (byte)MapCellFlags.FreeFloor;

            Assert.NotEqual(before, map.ComputeHash());
        }

        /// 셀 내용이 같아도 좌표계가 어긋나면 다른 맵이다.
        [Fact]
        public void 원점만_달라도_해시가_다르다()
        {
            var map = MapWithBox();
            map.Grid = Grid();
            var before = map.ComputeHash();

            map.Grid.OriginX += 3f;

            Assert.NotEqual(before, map.ComputeHash());
        }

        [Fact]
        public void 층_수만_달라도_해시가_다르다()
        {
            var a = MapWithBox();
            a.Grid = Grid(floors: 2, width: 4, depth: 3);

            var b = MapWithBox();
            b.Grid = Grid(floors: 3, width: 4, depth: 2);

            // 셀 수는 24 로 같지만 격자 모양이 다르다.
            Assert.Equal(a.Grid.CellCount, b.Grid.CellCount);
            Assert.NotEqual(a.ComputeHash(), b.ComputeHash());
        }
    }
}
