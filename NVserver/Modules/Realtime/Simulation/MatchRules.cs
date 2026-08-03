using System.Numerics;
using NV.Shared.Collision;
using NV.Shared.Simulation;

namespace NV.Realtime.Simulation
{
    /// 매치 규칙의 판정. 상태는 갖지 않는다 — 입력을 받아 결과를 돌려준다.
    ///
    /// `Match` 와 나누는 기준은 상태 소유다. 그쪽은 단계와 시계를 들고 있고, 여기는 계산만
    /// 한다. 그래서 테스트가 룸 없이 이 함수들을 직접 부를 수 있다.
    internal static class MatchRules
    {
        /// 한 매치의 목표물을 배치한다.
        ///
        /// **순서가 규칙의 일부다** — 제단 → 문 → 열쇠 → 장치. 제단이 먼저인 이유는 그것이
        /// 유일한 고정물이기 때문이다(격자 중앙 근처, 매치마다 같은 자리). 나머지가 제단을
        /// 피해 가야 하고 그 반대는 아니다. 문이 그다음이고 열쇠·장치가 문에서 떨어지는
        /// 이유도 같다 — 열쇠가 문간에 생기면 목표가 우연히 짧아진다.
        ///
        /// 클라이언트의 `MatchManager.PlaceObjectives` 에 있던 순서와 간격을 그대로 옮겼다.
        ///
        /// 씨드를 `ref` 로 받는다. 값으로 받으면 호출자의 수열이 진행하지 않아 배치가 매번
        /// 같은 자리를 뽑는다 — 증상은 "목표물이 전부 겹쳐서 생김" 이다.
        public static void PlaceObjectives(Objectives objectives, MapGrid grid, ref DeterministicSequence sequence)
        {
            objectives.Reset();

            // 격자가 없는 맵에서는 배치하지 않는다. 조용히 원점에 놓으면 목표물이 전부
            // (0,0,0) 에 겹쳐 생기고, 그 자리가 벽 안일 수도 있다.
            if (grid == null || grid.FreeFloorCount == 0)
            {
                return;
            }

            PlaceAltar(objectives, grid);

            if (grid.TryRandomFreeFloor(ref sequence, out var doorPoint))
            {
                objectives.SetDoor(doorPoint, RandomYaw(ref sequence));
            }

            PlaceKeys(objectives, grid, ref sequence);
            PlaceDevices(objectives, grid, ref sequence);

            objectives.MarkPlaced();
        }

        /// 격자 중앙에서 밖으로 링을 넓혀 가며 몸이 들어가는 셀을 찾는다.
        ///
        /// 중앙 자체가 계단일 수 있어서 링 탐색이 필요하다 — 클라이언트도 같은 이유로
        /// 같은 방식이었다. 착지점은 인접 셀에서 찾는다: 제단이 놓인 셀에 Seeker 를
        /// 내려놓을 수는 없다.
        ///
        /// 무작위가 아니다. 제단은 매치마다 같은 자리여야 하고(기획서 §4.3 의 벌칙을
        /// 예측할 수 있어야 한다), 그래서 씨드를 받지 않는다.
        private static void PlaceAltar(Objectives objectives, MapGrid grid)
        {
            var data = grid.Data;
            var centerX = data.Width / 2;
            var centerZ = data.Depth / 2;
            var maxRadius = data.Width > data.Depth ? data.Width : data.Depth;

            for (var radius = 0; radius <= maxRadius; radius++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    for (var dz = -radius; dz <= radius; dz++)
                    {
                        var ring = Abs(dx) > Abs(dz) ? Abs(dx) : Abs(dz);
                        if (ring != radius)
                        {
                            continue;
                        }

                        var x = centerX + dx;
                        var z = centerZ + dz;

                        // 1층에만 놓는다. 제단은 지상의 고정물이다.
                        if (!data.Has(0, x, z, MapCellFlags.FreeFloor))
                        {
                            continue;
                        }

                        if (!TryFindLandingSpot(grid, x, z, out var dragPoint))
                        {
                            continue;
                        }

                        objectives.SetAltar(data.CellToWorld(0, x, z), dragPoint);
                        return;
                    }
                }
            }
        }

        /// 제단 옆에서 몸이 들어가는 셀. 8방향을 본다.
        private static bool TryFindLandingSpot(MapGrid grid, int x, int z, out Vector3 point)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0)
                    {
                        continue;
                    }

                    if (grid.Data.Has(0, x + dx, z + dz, MapCellFlags.FreeFloor))
                    {
                        point = grid.Data.CellToWorld(0, x + dx, z + dz);
                        return true;
                    }
                }
            }

            point = default;
            return false;
        }

        /// 기획서 §3 — 열쇠 10개. `KeysPlaced` 가 `KeysRequired` 보다 적으면 Runner 가 이길
        /// 수 없으므로 큰 쪽을 쓴다.
        private static void PlaceKeys(Objectives objectives, MapGrid grid, ref DeterministicSequence sequence)
        {
            var count = MatchConstants.KeysPlaced > MatchConstants.KeysRequired
                ? MatchConstants.KeysPlaced
                : MatchConstants.KeysRequired;

            for (var index = 0; index < count; index++)
            {
                if (TryFindSpacedPoint(objectives, grid, ref sequence, RealtimeConstants.Match.KeySpacing, out var point))
                {
                    objectives.AddKey(point);
                }
            }
        }

        /// 기획서 §5 — 장치 8~9개. 조합은 `RealtimeConstants.Match.DeviceMix` 가 정한다.
        private static void PlaceDevices(Objectives objectives, MapGrid grid, ref DeterministicSequence sequence)
        {
            var mix = RealtimeConstants.Match.DeviceMix;

            var count = MatchConstants.DeviceCount < mix.Length
                ? MatchConstants.DeviceCount
                : mix.Length;

            for (var index = 0; index < count; index++)
            {
                if (!TryFindSpacedPoint(objectives, grid, ref sequence, RealtimeConstants.Match.DeviceSpacing, out var point))
                {
                    continue;
                }

                objectives.AddDevice(new DevicePlacement(mix[index], point, RandomYaw(ref sequence)));
            }
        }

        /// 이미 놓인 것들에서 떨어진 무작위 자리.
        ///
        /// 시도 횟수를 다 쓰면 **간격을 포기하고 아무 자리나 돌려준다.** 좁은 맵에서는 조건을
        /// 만족하는 자리가 아예 없을 수 있고, 그때 목표물이 하나도 안 생기는 것보다 겹쳐서라도
        /// 생기는 편이 낫다 — 열쇠가 0개면 매치가 성립하지 않는다.
        private static bool TryFindSpacedPoint(
            Objectives objectives,
            MapGrid grid,
            ref DeterministicSequence sequence,
            float spacing,
            out Vector3 point)
        {
            for (var attempt = 0; attempt < RealtimeConstants.Match.PlacementAttempts; attempt++)
            {
                if (!grid.TryRandomFreeFloor(ref sequence, out point))
                {
                    return false;
                }

                if (IsClearOfPlacements(objectives, point, spacing))
                {
                    return true;
                }
            }

            return grid.TryRandomFreeFloor(ref sequence, out point);
        }

        /// 이 자리가 이미 놓인 것들에서 충분히 떨어져 있는가.
        ///
        /// 제단과 문도 본다. 열쇠가 문간에 생기면 목표가 우연히 짧아지고, 제단 위에 생기면
        /// Seeker 가 벌칙을 받으러 갈 때마다 열쇠를 밟는다.
        private static bool IsClearOfPlacements(Objectives objectives, Vector3 point, float spacing)
        {
            var sqr = spacing * spacing;

            if (DistanceSquared(point, objectives.AltarPosition) < sqr)
            {
                return false;
            }

            if (DistanceSquared(point, objectives.DoorPosition) < sqr)
            {
                return false;
            }

            for (var index = 0; index < objectives.Keys.Count; index++)
            {
                if (DistanceSquared(point, objectives.Keys[index]) < sqr)
                {
                    return false;
                }
            }

            for (var index = 0; index < objectives.Devices.Count; index++)
            {
                if (DistanceSquared(point, objectives.Devices[index].Position) < sqr)
                {
                    return false;
                }
            }

            return true;
        }

        /// [0, 2π). 스폰 yaw 와 같은 규약이다 — 0 이 +Z 다.
        private static float RandomYaw(ref DeterministicSequence sequence)
        {
            return sequence.NextUnitFloat() * DeterministicMath.TwoPi;
        }

        /// `Vector3.DistanceSquared` 를 쓰지 않는다. `conventions.md` 가 SIMD 경로의 라운딩
        /// 차이를 이유로 `System.Numerics` 의 벡터 연산을 금지한다.
        private static float DistanceSquared(Vector3 a, Vector3 b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            var dz = a.Z - b.Z;

            return (dx * dx) + (dy * dy) + (dz * dz);
        }

        private static int Abs(int value)
        {
            return value < 0 ? -value : value;
        }
    }
}
