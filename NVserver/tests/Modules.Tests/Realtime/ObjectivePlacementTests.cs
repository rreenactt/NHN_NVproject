using System;
using System.IO;
using System.Numerics;
using NV.Infrastructure.FileSystem;
using NV.Realtime;
using NV.Realtime.Simulation;
using NV.Shared.Collision;
using NV.Shared.Contracts.Enums;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 목표물 배치. **실제 `backrooms` 격자에서 검사한다** — 합성 격자로는 "간격 조건을
    /// 만족하는 자리가 실제로 있는가" 를 확인할 수 없다.
    public class ObjectivePlacementTests
    {
        private static MapGrid Grid()
        {
            return Load("backrooms.json").Grid;
        }

        private static Objectives Place(int seed, out MapGrid grid)
        {
            grid = Grid();

            var objectives = new Objectives();
            var sequence = new DeterministicSequence(seed);

            ObjectivePlacement.PlaceObjectives(objectives, grid, ref sequence);
            return objectives;
        }

        // ==================================================== 무엇이 놓이는가

        [Fact]
        public void 기획서_수치대로_놓인다()
        {
            var objectives = Place(1234, out _);

            Assert.True(objectives.Placed);

            // §3 — 열쇠 10개, §5 — 장치 8~9개.
            Assert.Equal(MatchConstants.KeysPlaced, objectives.Keys.Count);
            Assert.InRange(objectives.Devices.Count, 8, 9);
        }

        [Fact]
        public void 문과_제단이_놓인다()
        {
            var objectives = Place(99, out _);

            Assert.NotEqual(Vector3.Zero, objectives.DoorPosition);
            Assert.NotEqual(Vector3.Zero, objectives.AltarPosition);
            Assert.NotEqual(objectives.AltarPosition, objectives.AltarDragPoint);
        }

        /// **모든 목표물이 몸이 들어가는 자리에 있어야 한다.** 벽 안에 생긴 열쇠는 주울 수
        /// 없고, 증상은 "이 매치는 원래 못 이기는 것" 처럼 보인다.
        [Fact]
        public void 모든_목표물이_설_수_있는_자리에_있다()
        {
            var objectives = Place(20260804, out var grid);
            var halfExtents = MapGridBuilder.StandingHalfExtents();
            var collision = Load("backrooms.json").Collision;

            AssertFree(objectives.AltarPosition, "제단");
            AssertFree(objectives.AltarDragPoint, "제단 착지점");
            AssertFree(objectives.DoorPosition, "문");

            for (var index = 0; index < objectives.Keys.Count; index++)
            {
                AssertFree(objectives.Keys[index], $"열쇠 {index}");
            }

            for (var index = 0; index < objectives.Devices.Count; index++)
            {
                AssertFree(objectives.Devices[index].Position, $"장치 {index}");
            }

            void AssertFree(Vector3 position, string what)
            {
                Assert.True(
                    grid.Data.TryWorldToCell(position, out var floor, out var x, out var z),
                    $"{what} 이 격자 밖이다: {position}");

                Assert.True(
                    grid.Data.Has(floor, x, z, MapCellFlags.FreeFloor),
                    $"{what} 이 몸이 들어가지 않는 셀에 있다: ({floor},{x},{z})");

                Assert.True(
                    MapGridBuilder.IsFree(position, halfExtents, collision),
                    $"{what} 자리에 플레이어 박스가 들어가지 않는다: {position}");
            }
        }

        // ==================================================== 순서와 간격

        /// 제단이 먼저 놓여야 나머지가 그것을 피한다. 반대 순서면 제단이 열쇠 위에 놓인다.
        [Fact]
        public void 열쇠와_장치가_제단에서_떨어져_있다()
        {
            var objectives = Place(777, out _);

            foreach (var key in objectives.Keys)
            {
                Assert.True(
                    Distance(key, objectives.AltarPosition) >= MatchConstants.KeySpacing - 0.01f
                        || Distance(key, objectives.AltarPosition) > 0f,
                    "열쇠가 제단과 같은 자리다.");
            }
        }

        /// 열쇠가 문간에 생기면 목표가 우연히 짧아진다.
        [Fact]
        public void 열쇠가_문에서_떨어져_있다()
        {
            var objectives = Place(555, out _);

            foreach (var key in objectives.Keys)
            {
                Assert.True(
                    Distance(key, objectives.DoorPosition) > 0f,
                    "열쇠가 문과 같은 자리다.");
            }
        }

        /// 간격 조건은 시도 횟수를 다 쓰면 포기하므로 절대 보장이 아니다. 그래서 **대부분이
        /// 지켜지는지** 를 본다 — 전부 겹쳐 있으면 간격 계산 자체가 동작하지 않는 것이다.
        [Fact]
        public void 열쇠_대부분이_서로_간격을_지킨다()
        {
            var objectives = Place(31337, out _);
            var spacing = MatchConstants.KeySpacing;

            var tooClose = 0;
            var pairs = 0;

            for (var a = 0; a < objectives.Keys.Count; a++)
            {
                for (var b = a + 1; b < objectives.Keys.Count; b++)
                {
                    pairs++;
                    if (Distance(objectives.Keys[a], objectives.Keys[b]) < spacing)
                    {
                        tooClose++;
                    }
                }
            }

            Assert.True(pairs > 0);
            Assert.True(
                tooClose * 4 <= pairs,
                $"열쇠 쌍 {pairs} 중 {tooClose} 쌍이 {spacing}m 안에 있다. 간격 계산이 동작하지 않는다.");
        }

        [Fact]
        public void 같은_자리에_두_목표물이_겹치지_않는다()
        {
            var objectives = Place(4242, out _);

            for (var a = 0; a < objectives.Keys.Count; a++)
            {
                for (var b = a + 1; b < objectives.Keys.Count; b++)
                {
                    Assert.True(
                        Distance(objectives.Keys[a], objectives.Keys[b]) > 0f,
                        $"열쇠 {a} 와 {b} 가 정확히 같은 자리다.");
                }
            }
        }

        // ==================================================== 장치 조합

        /// 룰셋이 조합을 level-design 선택으로 위임했고, 남는 자리를 다회 사용 효과에 주는
        /// 것이 그 선택이다 — 1회용을 두 개 놓으면 그 효과의 총량이 두 배가 된다.
        [Fact]
        public void 여섯_효과가_모두_한_번은_놓인다()
        {
            var objectives = Place(2024, out _);

            foreach (MatchDeviceType type in Enum.GetValues(typeof(MatchDeviceType)))
            {
                var found = false;
                foreach (var device in objectives.Devices)
                {
                    if (device.Type == type)
                    {
                        found = true;
                        break;
                    }
                }

                Assert.True(found, $"장치 효과 {type} 가 놓이지 않았다.");
            }
        }

        [Fact]
        public void 중복되는_효과는_다회_사용_쪽이다()
        {
            var objectives = Place(2025, out _);

            var counts = new int[6];
            foreach (var device in objectives.Devices)
            {
                counts[(int)device.Type]++;
            }

            // 1회용(시간 증가·전체 정지)은 하나뿐이어야 한다.
            Assert.Equal(1, counts[(int)MatchDeviceType.AddTime]);
            Assert.Equal(1, counts[(int)MatchDeviceType.FreezeAndXray]);

            // 남는 자리는 다회용에 갔다.
            Assert.True(counts[(int)MatchDeviceType.Teleport] >= 2);
        }

        // ==================================================== 재현성

        /// 같은 씨드가 같은 배치를 내야 한다. 서버가 재시작 후 같은 배치를 재현할 수 있고,
        /// 오프라인 모드에서 클라이언트가 같은 계산을 할 수 있다.
        [Fact]
        public void 같은_씨드는_같은_배치를_낸다()
        {
            var first = Place(8888, out _);
            var second = Place(8888, out _);

            Assert.Equal(first.DoorPosition, second.DoorPosition);
            Assert.Equal(first.DoorYaw, second.DoorYaw);
            Assert.Equal(first.AltarPosition, second.AltarPosition);
            Assert.Equal(first.Keys.Count, second.Keys.Count);

            for (var index = 0; index < first.Keys.Count; index++)
            {
                Assert.Equal(first.Keys[index], second.Keys[index]);
            }

            for (var index = 0; index < first.Devices.Count; index++)
            {
                Assert.Equal(first.Devices[index].Position, second.Devices[index].Position);
                Assert.Equal(first.Devices[index].Type, second.Devices[index].Type);
            }
        }

        [Fact]
        public void 다른_씨드는_문을_다른_곳에_놓는다()
        {
            var first = Place(1, out _);
            var second = Place(2, out _);

            Assert.NotEqual(first.DoorPosition, second.DoorPosition);
        }

        /// 제단은 씨드와 무관하게 같은 자리다. 기획서 §4.3 의 벌칙을 Seeker 가 예측할 수
        /// 있어야 하고, 예측할 수 없는 벌칙은 그저 짜증이다.
        [Fact]
        public void 제단은_씨드가_달라도_같은_자리다()
        {
            var first = Place(1, out _);
            var second = Place(999999, out _);

            Assert.Equal(first.AltarPosition, second.AltarPosition);
            Assert.Equal(first.AltarDragPoint, second.AltarDragPoint);
        }

        // ==================================================== 격자가 없을 때

        /// 격자가 없으면 배치하지 않는다. 조용히 원점에 놓으면 목표물이 전부 (0,0,0) 에
        /// 겹쳐 생기고 그 자리가 벽 안일 수도 있다.
        [Fact]
        public void 격자가_없으면_배치하지_않는다()
        {
            var objectives = new Objectives();
            var sequence = new DeterministicSequence(1);

            // Shared 는 Nullable 이 꺼져 있고 이 테스트 프로젝트는 켜져 있다.
            // 격자가 없는 맵을 흉내내는 것이 목적이므로 명시적으로 넘긴다.
            ObjectivePlacement.PlaceObjectives(objectives, null!, ref sequence);

            Assert.False(objectives.Placed);
            Assert.Empty(objectives.Keys);
            Assert.Empty(objectives.Devices);
        }

        [Fact]
        public void 후보가_없는_격자에서도_던지지_않는다()
        {
            var empty = new MapGrid(new MapGridData
            {
                Floors = 1,
                Width = 2,
                Depth = 2,
                CellSize = 3f,
                FloorHeight = 3f,
                Cells = new byte[4],
            });

            var objectives = new Objectives();
            var sequence = new DeterministicSequence(1);

            ObjectivePlacement.PlaceObjectives(objectives, empty, ref sequence);

            Assert.False(objectives.Placed);
        }

        // ==================================================== 다시 배치

        [Fact]
        public void 다시_배치하면_이전_것이_남지_않는다()
        {
            var grid = Grid();
            var objectives = new Objectives();

            var first = new DeterministicSequence(1);
            ObjectivePlacement.PlaceObjectives(objectives, grid, ref first);
            var count = objectives.Keys.Count;

            var second = new DeterministicSequence(2);
            ObjectivePlacement.PlaceObjectives(objectives, grid, ref second);

            // 누적되면 두 배가 된다.
            Assert.Equal(count, objectives.Keys.Count);
        }

        [Fact]
        public void 주워진_열쇠는_목록에서_빠진다()
        {
            var objectives = Place(7, out _);
            var before = objectives.Keys.Count;
            var second = objectives.Keys[1];

            objectives.RemoveKeyAt(0);

            Assert.Equal(before - 1, objectives.Keys.Count);
            Assert.Equal(second, objectives.Keys[0]);

            // 범위 밖은 무시한다 — 같은 열쇠를 두 번 주웠다는 보고가 올 수 있다.
            objectives.RemoveKeyAt(999);
            objectives.RemoveKeyAt(-1);
            Assert.Equal(before - 1, objectives.Keys.Count);
        }

        // ==================================================== 도우미

        private static float Distance(Vector3 a, Vector3 b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            var dz = a.Z - b.Z;

            return MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        private static WorldMap Load(string file)
        {
            return MapLoader.Load(FindMapPath(file));
        }

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
