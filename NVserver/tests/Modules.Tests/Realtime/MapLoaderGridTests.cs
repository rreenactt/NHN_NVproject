using System;
using System.IO;
using NV.Infrastructure.FileSystem;
using NV.Shared.Collision;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 격자가 JSON 을 왕복하는지 본다.
    ///
    /// **이 테스트가 실증하는 가정은 `Cells` 가 base64 문자열로 오간다는 것이다.**
    /// System.Text.Json 이 `byte[]` 를 그렇게 다루므로 서버는 파싱 코드를 한 줄도
    /// 쓰지 않지만, 클라이언트 export 는 JSON 을 손으로 쓴다(`MapCollisionExporter`).
    /// 그쪽이 같은 형식을 내야 하고, 가정이 틀리면 격자가 조용히 비어서 온다.
    public class MapLoaderGridTests
    {
        private const string BoxesAndSpawns =
            "\"boxes\": [ { \"minX\": 0, \"minY\": 0, \"minZ\": 0, \"maxX\": 1, \"maxY\": 1, \"maxZ\": 1 } ], " +
            "\"spawns\": [ { \"x\": 0, \"y\": 0, \"z\": 0, \"yaw\": 0 } ]";

        private static WorldMap LoadJson(string json)
        {
            var path = Path.Combine(Path.GetTempPath(), $"nv-map-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, json);

            try
            {
                return MapLoader.Load(path);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void 격자가_없는_맵도_로드된다()
        {
            var map = LoadJson("{ \"name\": \"g\", " + BoxesAndSpawns + " }");

            Assert.False(map.Data.HasGrid);
            Assert.Null(map.Data.Grid);
            Assert.NotEqual(0u, map.Hash);
        }

        /// 2층 2×2 격자, 8셀. 바이트는 각각
        /// 1(Standable), 3(Standable|FreeFloor), 0, 5(Standable|StairLink) 를 두 층에 걸쳐 둔다.
        /// base64 는 그 8바이트를 그대로 인코딩한 것이다.
        [Fact]
        public void 격자가_base64_로_왕복한다()
        {
            var cells = new byte[] { 1, 3, 0, 5, 3, 1, 5, 0 };
            var base64 = Convert.ToBase64String(cells);

            var map = LoadJson(
                "{ \"name\": \"g\", " + BoxesAndSpawns + ", " +
                "\"grid\": { \"floors\": 2, \"width\": 2, \"depth\": 2, " +
                "\"cellSize\": 3, \"floorHeight\": 3.2, \"originX\": -3, \"originZ\": -3, " +
                "\"cells\": \"" + base64 + "\" } }");

            Assert.True(map.Data.HasGrid);

            var grid = map.Data.Grid;
            Assert.Equal(2, grid.Floors);
            Assert.Equal(2, grid.Width);
            Assert.Equal(2, grid.Depth);
            Assert.Equal(3f, grid.CellSize);
            Assert.Equal(3.2f, grid.FloorHeight);
            Assert.Equal(-3f, grid.OriginX);
            Assert.Equal(-3f, grid.OriginZ);

            Assert.Equal(cells, grid.Cells);

            // 플래그가 셀 좌표에 제대로 앉았는지 인덱스 식을 통해 확인한다.
            Assert.True(grid.Has(0, 0, 0, MapCellFlags.Standable));
            Assert.True(grid.Has(0, 1, 0, MapCellFlags.FreeFloor));
            Assert.Equal(MapCellFlags.None, grid.At(0, 0, 1));
            Assert.True(grid.Has(0, 1, 1, MapCellFlags.StairLink));
        }

        /// 격자가 실려 오면 해시에 반영되어야 한다. 같은 박스·같은 이름인데 격자만
        /// 다른 두 맵이 같은 해시를 내면 맵 해시 대조가 격자를 감시하지 못한다.
        [Fact]
        public void 격자가_해시에_반영된다()
        {
            var bare = LoadJson("{ \"name\": \"g\", " + BoxesAndSpawns + " }");

            var withGrid = LoadJson(
                "{ \"name\": \"g\", " + BoxesAndSpawns + ", " +
                "\"grid\": { \"floors\": 1, \"width\": 2, \"depth\": 2, " +
                "\"cellSize\": 3, \"floorHeight\": 3.2, \"originX\": 0, \"originZ\": 0, " +
                "\"cells\": \"" + Convert.ToBase64String(new byte[] { 1, 1, 1, 1 }) + "\" } }");

            var otherCells = LoadJson(
                "{ \"name\": \"g\", " + BoxesAndSpawns + ", " +
                "\"grid\": { \"floors\": 1, \"width\": 2, \"depth\": 2, " +
                "\"cellSize\": 3, \"floorHeight\": 3.2, \"originX\": 0, \"originZ\": 0, " +
                "\"cells\": \"" + Convert.ToBase64String(new byte[] { 1, 1, 1, 3 }) + "\" } }");

            Assert.NotEqual(bare.Hash, withGrid.Hash);
            Assert.NotEqual(withGrid.Hash, otherCells.Hash);
        }

        /// 어긋난 격자는 로드 단계에서 거절한다. 그대로 받으면 잘못이 한참 뒤
        /// 배치 단계에서 "열쇠가 벽 안에 생김" 으로만 드러난다.
        [Fact]
        public void 셀_수가_맞지_않는_격자는_기동을_실패시킨다()
        {
            var exception = Assert.Throws<InvalidOperationException>(() => LoadJson(
                "{ \"name\": \"g\", " + BoxesAndSpawns + ", " +
                "\"grid\": { \"floors\": 2, \"width\": 4, \"depth\": 4, " +
                "\"cellSize\": 3, \"floorHeight\": 3.2, \"originX\": 0, \"originZ\": 0, " +
                "\"cells\": \"" + Convert.ToBase64String(new byte[] { 1, 1, 1 }) + "\" } }"));

            Assert.Contains("격자", exception.Message);
        }
    }
}
