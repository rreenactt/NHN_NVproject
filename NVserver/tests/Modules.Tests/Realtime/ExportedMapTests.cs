using System;
using System.IO;
using System.Numerics;
using NV.Infrastructure.FileSystem;
using NV.Shared.Collision;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// Unity 에서 export 한 실제 맵을 검사한다.
    ///
    /// 스폰이 지형에 파묻히거나 바닥이 없으면 증상이 "접속하면 끝없이 떨어짐" 또는
    /// "스폰 직후 벽에 끼임" 으로 나타난다. 그때는 이미 클라이언트를 의심하고 있게 되므로
    /// 맵 파일 단계에서 잡는다.
    public sealed class ExportedMapTests
    {
        /// 서버가 로드할 수 있는 맵 전부. 어느 쪽으로 돌려도 플레이 가능해야 한다.
        /// `backrooms` 는 게임, `test-room` 은 멀티플레이 확인용이다.
        public static TheoryData<string, string> Maps => new TheoryData<string, string>
        {
            { "backrooms.json", "backrooms" },
            { "test-room.json", "test-room" },
        };

        [Theory]
        [MemberData(nameof(Maps))]
        public void Export된_맵이_로드되고_해시가_0이_아니다(string file, string name)
        {
            var map = Load(file);

            Assert.Equal(name, map.Name);
            Assert.True(map.Collision.BoxCount > 0, "박스가 없다.");
            Assert.NotEqual(0u, map.Hash);
            Assert.Equal(8, map.SpawnCount);
        }

        [Theory]
        [MemberData(nameof(Maps))]
        public void 모든_스폰이_지형과_겹치지_않는다(string file, string name)
        {
            _ = name;
            var map = Load(file);

            for (var index = 0; index < map.SpawnCount; index++)
            {
                var state = PlayerState.Spawn(map.SpawnPosition(index), map.SpawnYaw(index), 100);
                var resolved = map.Collision.Depenetrate(state.BoxCenter, state.BoxHalfExtents);

                Assert.True(
                    resolved == state.BoxCenter,
                    $"스폰 {index} 가 지형에 파묻혀 {resolved - state.BoxCenter} 만큼 밀려났다.");
            }
        }

        [Theory]
        [MemberData(nameof(Maps))]
        public void 스폰에서_가만히_있으면_바닥에_선다(string file, string name)
        {
            _ = name;
            var map = Load(file);

            for (var index = 0; index < map.SpawnCount; index++)
            {
                var state = PlayerState.Spawn(map.SpawnPosition(index), map.SpawnYaw(index), 100);
                var neutral = new InputFrame(
                    ButtonFlags.None,
                    0,
                    0,
                    Quantization.ToFixedYaw(state.Yaw),
                    0);

                // 중력이 적용되고 착지 판정이 서기까지 몇 틱이 필요하다.
                for (var tick = 0; tick < 10; tick++)
                {
                    state = PlayerMovement.Step(state, neutral, map.Collision);
                }

                Assert.True(state.IsGrounded, $"스폰 {index} 에서 착지하지 못했다. y = {state.Position.Y}");
                Assert.True(
                    Math.Abs(state.Position.Y) < 0.05f,
                    $"스폰 {index} 의 발밑이 바닥에서 {state.Position.Y} 만큼 떨어져 있다.");
            }
        }

        [Theory]
        [MemberData(nameof(Maps))]
        public void 스폰_지점에서_앞으로_걸어도_지형을_통과하지_않는다(string file, string name)
        {
            _ = name;
            var map = Load(file);
            var state = PlayerState.Spawn(map.SpawnPosition(0), map.SpawnYaw(0), 100);

            // 전진 입력 60틱. 2초면 벽에 닿고 미끄러진다.
            var forward = new InputFrame(
                ButtonFlags.None,
                0,
                127,
                Quantization.ToFixedYaw(state.Yaw),
                0);

            for (var tick = 0; tick < 60; tick++)
            {
                state = PlayerMovement.Step(state, forward, map.Collision);

                var resolved = map.Collision.Depenetrate(state.BoxCenter, state.BoxHalfExtents);
                Assert.True(
                    Vector3.Distance(resolved, state.BoxCenter) < 0.05f,
                    $"틱 {tick} 에서 지형에 {Vector3.Distance(resolved, state.BoxCenter)} 만큼 파묻혔다.");
            }
        }

        // ==================================================== 격자

        /// 격자를 내놓는 맵. `test-room` 은 매치 규칙을 돌리지 않으므로 격자가 없고,
        /// 그것이 정상이다.
        public static TheoryData<string> GriddedMaps => new TheoryData<string>
        {
            "backrooms.json",
        };

        [Theory]
        [MemberData(nameof(GriddedMaps))]
        public void Export된_격자가_자기_크기와_맞는다(string file)
        {
            var map = Load(file);

            Assert.True(map.Data.HasGrid, "격자가 실려 있지 않다.");
            Assert.True(map.Data.Grid.TryValidate(out var error), error);
            Assert.True(map.Data.Grid.Floors >= 1);
        }

        [Fact]
        public void 격자를_내놓지_않는_맵도_유효하다()
        {
            var map = Load("test-room.json");

            Assert.False(map.Data.HasGrid);
            Assert.NotEqual(0u, map.Hash);
        }

        /// `FreeFloor` 는 `Standable` 의 부분집합이어야 한다. 서지도 못하는 칸이 몸이
        /// 들어가는 칸으로 표시되면 배치가 벽 안을 고른다.
        [Theory]
        [MemberData(nameof(GriddedMaps))]
        public void 몸이_들어가는_셀은_전부_설_수_있는_셀이다(string file)
        {
            var grid = Load(file).Data.Grid;

            for (var index = 0; index < grid.Cells.Length; index++)
            {
                var flags = (MapCellFlags)grid.Cells[index];

                if ((flags & MapCellFlags.FreeFloor) == MapCellFlags.FreeFloor)
                {
                    Assert.True(
                        (flags & MapCellFlags.Standable) == MapCellFlags.Standable,
                        $"셀 {index} 가 FreeFloor 인데 Standable 이 아니다.");
                }
            }
        }

        /// **이 테스트가 잡는 것은 좌표계 어긋남이다.**
        ///
        /// export 가 표시한 `FreeFloor` 를 서버가 자기 충돌 코드로 다시 검산한다. 격자가
        /// 반 셀 밀렸거나 `CellIndex` 의 축 순서가 뒤바뀌었으면, 벽에 걸린 칸이
        /// `FreeFloor` 로 실려 오고 여기서 걸린다. 크기와 해시는 그 두 경우에도 맞는다.
        [Theory]
        [MemberData(nameof(GriddedMaps))]
        public void FreeFloor_로_표시된_칸에는_실제로_플레이어가_들어간다(string file)
        {
            var map = Load(file);
            var grid = map.Data.Grid;
            var halfExtents = MapGridBuilder.StandingHalfExtents();

            var checked_ = 0;

            for (var floor = 0; floor < grid.Floors; floor++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    for (var z = 0; z < grid.Depth; z++)
                    {
                        if (!grid.Has(floor, x, z, MapCellFlags.FreeFloor))
                        {
                            continue;
                        }

                        Assert.True(
                            MapGridBuilder.IsFree(grid.CellToWorld(floor, x, z), halfExtents, map.Collision),
                            $"셀 ({floor},{x},{z}) 이 FreeFloor 인데 플레이어 박스가 지형과 겹친다.");

                        checked_++;
                    }
                }
            }

            Assert.True(checked_ > 0, "FreeFloor 인 셀이 하나도 없다.");
        }

        /// **회귀 테스트.** 한때 1층 말고 모든 층의 `FreeFloor` 가 0 이었다.
        ///
        /// 원인은 박스 하단을 `(feet + halfY) - halfY` 로 왕복 계산하는 데 있었다. 그
        /// 왕복은 값을 보존하지 않아서(`(3.2f + 0.9f) - 0.9f == 3.1999999`) 박스가 바닥을
        /// 1e-7 만큼 파고들었고, 발밑이 정확히 `0f` 인 1층만 무사했다. 증상은 "위층에만
        /// 목표물이 생기지 않는다" 였다 — 크기·해시·검증은 전부 통과한다.
        [Theory]
        [MemberData(nameof(GriddedMaps))]
        public void 모든_층에_몸이_들어가는_셀이_있다(string file)
        {
            var grid = Load(file).Data.Grid;

            for (var floor = 0; floor < grid.Floors; floor++)
            {
                var free = 0;

                for (var x = 0; x < grid.Width; x++)
                {
                    for (var z = 0; z < grid.Depth; z++)
                    {
                        if (grid.Has(floor, x, z, MapCellFlags.FreeFloor))
                        {
                            free++;
                        }
                    }
                }

                Assert.True(free > 0, $"{floor} 층에 FreeFloor 인 셀이 하나도 없다.");
            }
        }

        // ==================================================== 실제 맵에 대한 질의

        /// 질의가 돌려준 자리를 서버의 충돌 코드로 검산한다. 합성 격자로는 드러나지
        /// 않는 것 — 실제 지형에서 후보 목록이 벽에 걸린 셀을 품는 경우 — 를 잡는다.
        [Theory]
        [MemberData(nameof(GriddedMaps))]
        public void 무작위_질의가_돌려준_자리에_플레이어가_들어간다(string file)
        {
            var map = Load(file);
            var halfExtents = MapGridBuilder.StandingHalfExtents();
            var sequence = new DeterministicSequence(20260804);

            Assert.True(map.HasGrid);
            Assert.True(map.Grid.FreeFloorCount > 0);

            for (var draw = 0; draw < 500; draw++)
            {
                Assert.True(map.Grid.TryRandomFreeFloor(ref sequence, out var feet));
                Assert.True(
                    MapGridBuilder.IsFree(feet, halfExtents, map.Collision),
                    $"질의가 돌려준 {feet} 에서 플레이어 박스가 지형과 겹친다.");
            }
        }

        /// 순간이동이 벽 안으로 떨어졌을 때 되돌릴 자리를 찾는 경로다. 스폰마다
        /// 확인한다 — 스폰은 실제로 사람이 서는 자리이므로 근처에 답이 있어야 한다.
        [Theory]
        [MemberData(nameof(GriddedMaps))]
        public void 스폰_근처에서_가장_가까운_자리를_찾는다(string file)
        {
            var map = Load(file);
            var halfExtents = MapGridBuilder.StandingHalfExtents();

            for (var index = 0; index < map.SpawnCount; index++)
            {
                Assert.True(
                    map.Grid.TryNearestFreeFloor(map.SpawnPosition(index), out var feet),
                    $"스폰 {index} 근처에서 FreeFloor 를 찾지 못했다.");

                Assert.True(
                    MapGridBuilder.IsFree(feet, halfExtents, map.Collision),
                    $"스폰 {index} 근처에서 찾은 {feet} 에 플레이어가 들어가지 않는다.");
            }
        }

        /// 격자를 내놓지 않는 맵에서는 `Grid` 가 `null` 이다. 빈 격자를 만들어 주면
        /// 호출자가 "후보 0개" 와 "격자 없음" 을 구분할 수 없다.
        [Fact]
        public void 격자가_없는_맵의_Grid_는_null_이다()
        {
            var map = Load("test-room.json");

            Assert.False(map.HasGrid);
            Assert.Null(map.Grid);
        }

        private static WorldMap Load(string file)
        {
            return MapLoader.Load(FindMapPath(file));
        }

        /// 테스트는 artifacts/ 아래에서 실행되므로 저장소 루트를 거슬러 찾는다.
        private static string FindMapPath(string file)
        {
            var relative = Path.Combine("MapData", file);
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, relative);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException($"MapData/{file} 를 찾지 못했다.");
        }
    }
}
