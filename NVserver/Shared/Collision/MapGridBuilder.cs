using System.Numerics;
using NV.Shared.Simulation;

namespace NV.Shared.Collision
{
    /// 격자의 `FreeFloor` 플래그를 채운다.
    ///
    /// **왜 이것이 `Shared` 에 있는가.** 격자는 클라이언트가 export 하지만, 그 플래그가
    /// 뜻하는 것은 "서버가 여기에 플레이어를 놓아도 밀려나지 않는다" 다. 판정 기준이
    /// 시뮬레이션 기준과 달라지면 서버는 자기가 유효하다고 표시한 자리에 플레이어를
    /// 놓고 곧바로 밀어낸다. 그래서 계산을 양쪽이 공유하는 곳에 두고, 서버의 플레이어
    /// 박스와 서버의 충돌 코드로 답한다.
    ///
    /// **원래 이 판정은 클라이언트에서 `Physics.CheckCapsule` 이 했다**
    /// (`MatchManager.IsFreeFloor`, 반지름 0.32 캡슐). 그것을 그대로 옮기지 않은 이유가
    /// 둘이다.
    ///
    /// 첫째, 옮길 수 없다. export 는 지오메트리를 만들지 않는 경로
    /// (`INetworkMapSource.ComputeCollision`)로 도는데, `Physics.CheckCapsule` 은 씬에
    /// 실제 콜라이더가 있어야 답한다. 그 경로에서 물리 질의는 전부 "아무것도 없음" 을
    /// 돌려주고, 결과는 **모든 셀이 통과** 다.
    ///
    /// 둘째, 옮기더라도 기준이 틀렸다. 캡슐 반지름 0.32 는 서버 플레이어 박스의
    /// `SimConstants.PlayerRadius`(0.4)보다 **작다.** 작은 프로브는 서버가 밀어낼 자리를
    /// 통과시키고, 증상은 "순간이동한 플레이어가 벽에서 튀어나옴" 이다.
    ///
    /// 계단이 걸러지는 것은 이 방식으로도 유지된다. 계단 스텝은 콜리전 박스로 export
    /// 되므로(`AddBox("Step", …)`) 박스 목록 안에 있고, 플레이어 박스가 그것과 겹친다.
    public static class MapGridBuilder
    {
        /// `Standable` 인 셀 중 플레이어가 실제로 설 수 있는 곳에 `FreeFloor` 를 세운다.
        ///
        /// 이미 세워져 있던 `FreeFloor` 는 지운 뒤 다시 계산한다. 두 번 호출해도 같은
        /// 결과가 나와야 한다 — export 를 반복하면 맵 해시가 흔들리기 때문이다.
        ///
        /// 반환값은 `FreeFloor` 로 표시된 셀 수다. 0 이면 격자나 콜리전이 어긋났다는
        /// 신호이고, export 쪽에서 그것을 보고 멈출 수 있다.
        public static int MarkFreeFloor(MapGridData grid, CollisionWorld collision)
        {
            if (grid == null || grid.Cells == null || collision == null)
            {
                return 0;
            }

            var halfExtents = StandingHalfExtents();
            var marked = 0;

            for (var floor = 0; floor < grid.Floors; floor++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    for (var z = 0; z < grid.Depth; z++)
                    {
                        var index = grid.CellIndex(floor, x, z);
                        if (index < 0 || index >= grid.Cells.Length)
                        {
                            continue;
                        }

                        var flags = (MapCellFlags)grid.Cells[index];

                        // 다시 계산하므로 이전 값을 먼저 지운다.
                        flags &= ~MapCellFlags.FreeFloor;

                        if ((flags & MapCellFlags.Standable) == MapCellFlags.Standable
                            && IsFree(grid.CellToWorld(floor, x, z), halfExtents, collision))
                        {
                            flags |= MapCellFlags.FreeFloor;
                            marked++;
                        }

                        grid.Cells[index] = (byte)flags;
                    }
                }
            }

            return marked;
        }

        /// 이 발밑 좌표에 선 플레이어가 지형과 겹치지 않는가.
        ///
        /// `Depenetrate` 가 위치를 바꾸지 않으면 겹치지 않은 것이다. `ExportedMapTests`
        /// 가 스폰을 검사하는 것과 **같은 판정**이며, 그래서 격자가 통과시킨 자리는
        /// 그 테스트도 통과한다.
        ///
        /// **발밑을 `SkinWidth` 만큼 올려서 판정한다. 이 한 줄이 없으면 1층을 제외한
        /// 모든 층이 통째로 탈락한다.**
        ///
        /// 박스는 중심에서 만들어지고(`Aabb.FromCenter`) 그 중심은 `feet + halfY` 로
        /// 계산되므로, 하단은 `(feet + halfY) - halfY` 라는 float 왕복을 거친다. 그 왕복은
        /// 값을 보존하지 않는다 — `(3.2f + 0.9f) - 0.9f` 는 `3.1999999` 이고 발밑보다
        /// **아래**다. 그래서 박스가 바닥 슬래브를 1e-7 만큼 파고들고 `Depenetrate` 가
        /// 밀어낸다. 1층만 무사한 이유는 발밑이 정확히 `0f` 여서 왕복 오차가 0 이기
        /// 때문이고, 그 탓에 이 문제는 "위층에만 목표물이 생기지 않는다" 로만 나타난다.
        ///
        /// 올리는 양이 `SkinWidth` 인 것은 임의가 아니다. 서버가 접촉면에 정지할 때
        /// 실제로 그만큼 띄우므로(`CollisionWorld.MoveBox`), 서 있는 플레이어의 진짜
        /// 발밑 높이가 이것이다. 1e-3 은 왕복 오차 1e-7 보다 네 자리 크고, 벽 침투
        /// (수 cm)보다 두 자리 작아서 두 경우를 갈라내기에 알맞다.
        public static bool IsFree(Vector3 feet, Vector3 halfExtents, CollisionWorld collision)
        {
            if (collision == null)
            {
                return false;
            }

            var center = new Vector3(
                feet.X,
                feet.Y + SimConstants.SkinWidth + halfExtents.Y,
                feet.Z);

            var resolved = collision.Depenetrate(center, halfExtents);

            return resolved == center;
        }

        /// 서 있는 플레이어의 박스 절반 크기. `PlayerState.BoxHalfExtents` 와 같은 식이다.
        ///
        /// 웅크린 높이를 쓰지 않는다. 웅크려야 들어가는 자리를 순간이동 착지점으로
        /// 고르면 플레이어가 일어서는 순간 천장에 끼인다.
        public static Vector3 StandingHalfExtents()
        {
            return new Vector3(
                SimConstants.PlayerRadius,
                SimConstants.PlayerHeight * 0.5f,
                SimConstants.PlayerRadius);
        }
    }
}
