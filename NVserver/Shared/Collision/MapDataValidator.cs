using System.Collections.Generic;
using System.Numerics;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Simulation;

namespace NV.Shared.Collision
{
    /// 맵 파일이 쓸 만한 상태인가.
    ///
    /// **`Shared` 에 있는 이유는 검사를 두 곳에 쓰지 않기 위해서다.** 서버는 로드할 때
    /// 검사하고 클라이언트는 export 하기 전에 검사하는데, 두 검사가 갈리면 export 가
    /// 통과시킨 파일이 서버 기동을 멈춘다 — 실제로 그 상태였다. 서버가 네 가지를 보는 동안
    /// export 는 격자 하나만 봤다.
    ///
    /// 검사는 두 층으로 나뉘고, 나누는 기준은 **얼마나 비싼가** 다.
    ///
    /// `TryValidateSchema` 는 파일이 자기 자신과 맞는지만 본다. 싸므로 서버 기동 경로에
    /// 둘 수 있다. 여기서 걸리는 것은 그대로 두면 조용히 사라지는 것들이다 — 박스가 없으면
    /// 플레이어가 지형을 통과하고, `min > max` 인 박스는 스윕에서 무시되어 벽 하나가
    /// 사라진 것처럼 보인다.
    ///
    /// `InspectSimulation` 은 시뮬레이션을 돌려 검산한다. 격자가 있는 맵에서 셀 하나마다
    /// 겹침 해소를 부르므로 export 시점에 한 번 낼 비용이고, 기동 경로에 둘 것은 아니다.
    /// 여기서 걸리는 것은 **스키마 검사와 맵 해시를 모두 통과하는** 잘못들이다: 스폰이
    /// 지형에 파묻혀 있거나, 격자가 콜리전과 다른 좌표계를 말하거나, 층 하나가 통째로
    /// 배치 후보에서 빠진 경우.
    public static class MapDataValidator
    {
        /// 박스가 이보다 많으면 경고한다. **상한이 아니라 검토 신호다.**
        ///
        /// `CollisionWorld` 에는 브로드페이즈가 없어 스윕과 겹침 해소가 박스를 선형으로
        /// 훑는다. 이동 한 번이 박스 목록을 여덟 번쯤 지나므로(겹침 해소 4회 + 미끄러짐
        /// 4회) 30Hz × 8명이면 `backrooms` 의 736박스에서 초당 17만 회 순회다. 그 1.5배를
        /// 넘기는 맵을 만들 때는 브로드페이즈를 먼저 넣을지 재어 보라는 뜻이고, 거절하지
        /// 않는 이유는 이것이 판정의 정확성 문제가 아니기 때문이다.
        public const int BoxCountReviewThreshold = 1100;

        /// 파일이 자기 자신과 맞는가. 서버 기동과 export 가 **같은 이것**을 부른다.
        ///
        /// 오류를 하나 만나고 멈추지 않고 모아서 돌려준다. export 창이 목록을 한 번에
        /// 보여줘야 하고, 한 번에 하나씩 고치게 하면 같은 왕복을 여러 번 돈다.
        public static bool TryValidateSchema(MapData data, List<string> errors)
        {
            if (errors == null)
            {
                return false;
            }

            var before = errors.Count;

            if (data == null)
            {
                errors.Add("맵 자료가 비어 있다.");
                return false;
            }

            // **버전을 먼저 본다.** 읽을 수 없는 파일의 나머지 필드를 검사하는 것은 뜻이
            // 없고, 그 오류 목록은 사람을 엉뚱한 곳으로 보낸다.
            if (!MapSchema.IsReadable(data.Version))
            {
                errors.Add(
                    $"스키마 버전 {MapSchema.Effective(data.Version)} 은 이 서버가 모른다" +
                    $"(아는 최대 {MapSchema.Current}). 서버가 오래됐거나 맵이 새 도구로 만들어졌다.");
                return false;
            }

            if (data.Boxes == null || data.Boxes.Length == 0)
            {
                errors.Add("콜리전 박스가 없다. 이대로면 플레이어가 지형을 통과한다.");
            }
            else
            {
                for (var index = 0; index < data.Boxes.Length; index++)
                {
                    var box = data.Boxes[index];

                    // min > max 인 박스는 스윕에서 조용히 무시되어 벽이 사라진 것처럼 보인다.
                    if (box.MinX > box.MaxX || box.MinY > box.MaxY || box.MinZ > box.MaxZ)
                    {
                        errors.Add($"박스 {index} 의 min 이 max 보다 크다. 스윕이 이 박스를 무시한다.");
                    }
                }
            }

            if (data.Spawns == null || data.Spawns.Length == 0)
            {
                errors.Add("스폰 지점이 없다.");
            }

            // 격자는 없어도 된다 — 이동 판정은 박스만으로 되고, 격자를 요구하는 것은 목표물
            // 배치처럼 나중에 붙는 기능이다. 다만 **있으면서 어긋난** 격자는 거절한다.
            // 그대로 받으면 서버가 그것을 신뢰하고, 잘못은 한참 뒤 배치 단계에서
            // "열쇠가 벽 안에 생김" 으로만 드러난다.
            if (data.Grid != null && !data.Grid.TryValidate(out var gridError))
            {
                errors.Add($"격자가 잘못됐다: {gridError}");
            }

            return errors.Count == before;
        }

        /// 시뮬레이션으로 검산한다. **스키마 검사가 통과한 뒤에만 뜻이 있다.**
        ///
        /// 여기 있는 검사들은 서버의 `ExportedMapTests` 가 이미 하는 것과 같은 검사다.
        /// 다른 것은 시점뿐이다 — 그 테스트는 맵을 커밋한 뒤에 돌고, export 는 커밋 전이다.
        /// 판정에 쓰는 코드가 전부 `Shared` 라 Unity 에서도 그대로 돈다.
        public static void InspectSimulation(
            MapData data,
            List<string> errors,
            List<string> warnings)
        {
            if (data == null || errors == null || warnings == null)
            {
                return;
            }

            if (data.Boxes == null || data.Boxes.Length == 0)
            {
                return;
            }

            if (data.Boxes.Length > BoxCountReviewThreshold)
            {
                warnings.Add(
                    $"박스가 {data.Boxes.Length}개다({BoxCountReviewThreshold} 초과). " +
                    "CollisionWorld 에는 브로드페이즈가 없어 스윕이 목록을 선형으로 훑는다. " +
                    "이 규모를 넘길 계획이면 브로드페이즈를 먼저 재어 본다.");
            }

            var collision = data.ToCollisionWorld();

            InspectSpawns(data, collision, errors);
            InspectGrid(data, collision, errors, warnings);
        }

        /// 스폰이 지형에 파묻혀 있지 않고, 가만히 두면 바닥에 서는가.
        ///
        /// 두 증상이 각각 "스폰 직후 벽에 끼임" 과 "접속하면 끝없이 떨어짐" 이다. 둘 다
        /// 그때는 이미 클라이언트를 의심하고 있으므로 맵 파일 단계에서 잡는다.
        ///
        /// 판정은 `ExportedMapTests` 와 **같은 식**을 쓴다 — `PlayerState` 로 박스를 만들고
        /// `Depenetrate` 가 위치를 바꾸는지 본다. 여기서 통과한 맵이 그 테스트에서 걸리면
        /// 두 판정이 갈렸다는 뜻이므로, 식을 베끼지 말고 같은 타입을 지나게 두었다.
        private static void InspectSpawns(MapData data, CollisionWorld collision, List<string> errors)
        {
            if (data.Spawns == null)
            {
                return;
            }

            for (var index = 0; index < data.Spawns.Length; index++)
            {
                var spawn = data.Spawns[index];
                var state = PlayerState.Spawn(spawn.ToPosition(), spawn.Yaw, 100);

                var resolved = collision.Depenetrate(state.BoxCenter, state.BoxHalfExtents);
                if (resolved != state.BoxCenter)
                {
                    var push = resolved - state.BoxCenter;
                    errors.Add(
                        $"스폰 {index} 이 지형에 파묻혀 있다. 서버가 {push} 만큼 밀어낸다.");
                    continue;
                }

                var neutral = new InputFrame(
                    ButtonFlags.None,
                    0,
                    0,
                    Quantization.ToFixedYaw(state.Yaw),
                    0);

                // 중력이 적용되고 착지 판정이 서기까지 몇 틱이 필요하다.
                for (var tick = 0; tick < GroundingTicks; tick++)
                {
                    state = PlayerMovement.Step(state, neutral, collision);
                }

                if (!state.IsGrounded)
                {
                    errors.Add(
                        $"스폰 {index} 에서 {GroundingTicks}틱이 지나도 착지하지 않는다. " +
                        $"발밑에 바닥이 없다 (y = {state.Position.Y}).");
                    continue;
                }

                if (DeterministicMath.Abs(state.Position.Y - spawn.Y) > GroundedDriftLimit)
                {
                    errors.Add(
                        $"스폰 {index} 이 착지한 높이가 적어 둔 높이와 다르다: " +
                        $"{state.Position.Y} vs {spawn.Y}. 스폰이 공중에 떠 있다.");
                }
            }
        }

        /// 격자가 콜리전과 같은 좌표계를 말하는가.
        ///
        /// **크기 검증과 맵 해시는 좌표계 어긋남을 잡지 못한다.** 격자가 반 셀 밀렸거나
        /// `CellIndex` 의 축 순서가 뒤바뀌어도 셀 수는 맞고 해시도 맞는다. 잡는 방법은
        /// 표시된 자리에 실제로 플레이어 박스를 놓아 보는 것뿐이다.
        private static void InspectGrid(
            MapData data,
            CollisionWorld collision,
            List<string> errors,
            List<string> warnings)
        {
            var grid = data.Grid;

            if (grid == null)
            {
                // 격자를 내놓지 않는 레벨이 있고 그것이 정상이다. 매치 규칙을 돌리지 않는
                // 개발용 맵은 배치할 목표물이 없다.
                return;
            }

            if (grid.Cells == null)
            {
                return;
            }

            var halfExtents = MapGridBuilder.StandingHalfExtents();
            var totalFree = 0;
            var mismatched = 0;
            var notStandable = 0;
            var unsupported = 0;

            for (var floor = 0; floor < grid.Floors; floor++)
            {
                var freeOnFloor = 0;

                for (var x = 0; x < grid.Width; x++)
                {
                    for (var z = 0; z < grid.Depth; z++)
                    {
                        var flags = grid.At(floor, x, z);

                        if ((flags & MapCellFlags.FreeFloor) != MapCellFlags.FreeFloor)
                        {
                            continue;
                        }

                        freeOnFloor++;
                        totalFree++;

                        if ((flags & MapCellFlags.Standable) != MapCellFlags.Standable)
                        {
                            notStandable++;
                        }

                        var feet = grid.CellToWorld(floor, x, z);

                        if (!MapGridBuilder.IsFree(feet, halfExtents, collision))
                        {
                            mismatched++;
                            continue;
                        }

                        // **겹치지 않는 것과 서 있을 수 있는 것은 다르다.** `FreeFloor` 는
                        // 겹침만 보므로 격자가 통째로 맵 밖으로 밀리면 그 셀들은 아무것과도
                        // 겹치지 않아 **전부 통과한다** — 원점이나 셀 크기가 어긋난 격자의
                        // 정확히 이 모습이고, 크기 검증도 맵 해시도 통과한다.
                        //
                        // 여기서만 확인하고 `MarkFreeFloor` 는 건드리지 않는다. 그쪽을 고치면
                        // 플래그가 달라져 모든 맵의 해시가 바뀌고, 재-export 로 늘어나는 정보는
                        // 없다. 파일이 옳은지 보는 것은 검사의 몫이다.
                        var center = new Vector3(
                            feet.X,
                            feet.Y + SimConstants.SkinWidth + halfExtents.Y,
                            feet.Z);

                        if (!collision.IsGrounded(center, halfExtents, SimConstants.GroundProbeDistance))
                        {
                            unsupported++;
                        }
                    }
                }

                // **층마다 본다.** float 왕복 오차로 1층 말고 모든 층의 `FreeFloor` 가 0 이
                // 된 적이 있고, 그때 크기·플래그·해시는 전부 통과했다. 증상은 "위층에만
                // 목표물이 생기지 않는다" 였다.
                if (freeOnFloor == 0)
                {
                    errors.Add(
                        $"{floor}층에 몸이 들어가는 셀(FreeFloor)이 하나도 없다. " +
                        "그 층에는 목표물도 순간이동 착지점도 생기지 않는다.");
                }
            }

            if (totalFree == 0)
            {
                errors.Add(
                    "격자에 몸이 들어가는 셀(FreeFloor)이 하나도 없다. 격자와 콜리전 박스가 " +
                    "서로 다른 좌표계를 말하고 있다 — 원점, 셀 크기, CellIndex 의 축 순서를 본다.");
                return;
            }

            if (notStandable > 0)
            {
                errors.Add(
                    $"셀 {notStandable}개가 FreeFloor 인데 Standable 이 아니다. " +
                    "FreeFloor 는 Standable 의 부분집합이어야 한다.");
            }

            if (mismatched > 0)
            {
                errors.Add(
                    $"FreeFloor 로 표시된 셀 {mismatched}개(전체 {totalFree}개 중)에서 " +
                    "플레이어 박스가 지형과 겹친다. 격자가 콜리전과 어긋났다는 뜻이고, " +
                    "크기 검증과 맵 해시는 이 상태에서도 통과한다.");
            }

            if (unsupported > 0)
            {
                errors.Add(
                    $"FreeFloor 로 표시된 셀 {unsupported}개(전체 {totalFree}개 중)의 발밑에 " +
                    "바닥이 없다. 격자가 지형 밖으로 밀렸을 때의 모습이다 — 원점이나 셀 크기를 " +
                    "본다. 겹침만 보는 검사는 이것을 통과시킨다(허공은 아무것과도 겹치지 않는다).");
            }

            // 격자가 있는데 후보가 극히 적으면 배치가 겹친다. 거절하지 않는 이유는 작은
            // 맵에서는 그것이 정상일 수 있기 때문이다.
            if (totalFree < SparseFreeFloorThreshold)
            {
                warnings.Add(
                    $"몸이 들어가는 셀이 {totalFree}개뿐이다. 목표물이 서로 붙어 배치될 수 있다.");
            }
        }

        /// 중립 입력으로 착지를 기다리는 틱 수. 2/3초면 몇 미터 낙하를 흡수한다.
        private const int GroundingTicks = 10;

        /// 착지한 높이가 적어 둔 스폰 높이에서 이만큼 벗어나면 스폰이 공중에 있다.
        /// 접촉면에서 띄우는 `SkinWidth` 보다 훨씬 크고, 한 계단(0.2m)보다 작다.
        private const float GroundedDriftLimit = 0.05f;

        /// 목표물 배치가 서로 붙지 않을 만한 최소 후보 수. 열쇠 10개 + 장치 9개 + 제단이
        /// 서로 다른 셀에 앉을 여유를 본다.
        private const int SparseFreeFloorThreshold = 64;
    }
}
