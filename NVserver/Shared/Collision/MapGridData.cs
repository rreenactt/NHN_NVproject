using System;
using System.Numerics;
using NV.Shared.Simulation;

namespace NV.Shared.Collision
{
    /// 셀 하나가 무엇을 허용하는가.
    ///
    /// 세 플래그를 나누는 이유는 쓰임이 다르기 때문이다. 하나로 합치면 가장 엄격한
    /// 조건이 모든 배치에 걸려, 열쇠가 놓일 수 있는 자리가 필요 이상으로 줄어든다.
    ///
    /// 8비트를 넘기지 않는다. 셀당 1바이트가 `MapGridData.Cells` 의 전제다.
    public enum MapCellFlags : byte
    {
        None = 0,

        /// 격자상 통행 가능. 벽이 아니고 층 안에 있다.
        /// 열쇠는 이것으로 충분하다 — 물건은 계단 위에 놓여도 집을 수 있다.
        Standable = 1 << 0,

        /// 플레이어 캡슐이 실제로 들어가는 자리. 계단·기물이 차지한 셀은 빠진다.
        ///
        /// `Standable` 과 갈라야 하는 이유가 있다. 계단이 있는 셀은 격자상 통행
        /// 가능하지만 몸이 설 자리는 아니다. 제단과 순간이동 착지점은 이 플래그를
        /// 봐야 한다 — 클라이언트에서 이것을 `Physics.CheckCapsule` 로 확인하다가
        /// 제단이 매번 계단 위에 놓였던 것이 이 구분의 출처다.
        FreeFloor = 1 << 1,

        /// 위층과 수직으로 이어진다. 계단이 지나가는 셀이다.
        /// 경로 탐색이 층을 넘으려면 이것이 필요하다.
        StairLink = 1 << 2,
    }

    /// 걸을 수 있는 곳의 격자. 콜리전 박스만으로는 답할 수 없는 질문에 답한다.
    ///
    /// 서버는 지형을 AABB 박스 목록으로만 알아서 "여기 설 수 있는가" 를 계산할 수
    /// 없다. 목표물을 배치하고 피격 시 순간이동 지점을 고르려면 그 답이 필요하다.
    ///
    /// **격자는 클라이언트가 export 한다.** 서버가 레벨 생성기를 다시 구현하면 씨드를
    /// 바꿀 때마다 두 곳이 갈리고, 증상은 "가끔 열쇠가 벽 안에 생김" 으로만 나타난다.
    /// Unity 물리가 필요한 판정(`FreeFloor`)은 export 시점에 구워 넣는다.
    ///
    /// `Cells` 는 `byte[]` 다. System.Text.Json 이 byte 배열을 base64 문자열로 쓰므로
    /// 서버 파싱이 공짜이고, 2층 35×35 = 2450 셀이 한 줄에 들어간다. 숫자 배열로 두면
    /// 같은 정보가 4배 넘게 커진다.
    public sealed class MapGridData
    {
        public int Floors { get; set; }

        public int Width { get; set; }

        public int Depth { get; set; }

        public float CellSize { get; set; }

        public float FloorHeight { get; set; }

        /// 셀 (0,0) 의 바깥 모서리. 격자 좌표를 월드로 옮길 때의 기준점이다.
        public float OriginX { get; set; }

        public float OriginZ { get; set; }

        /// 셀당 `MapCellFlags` 한 바이트. 길이는 `Floors * Width * Depth` 다.
        public byte[] Cells { get; set; }

        public int CellCount => Floors * Width * Depth;

        /// 격자 좌표 → `Cells` 인덱스.
        ///
        /// **이 식은 여기에만 있어야 한다.** 클라이언트가 export 할 때와 서버가 조회할
        /// 때 순서가 어긋나면 격자가 90도 돌아간 채 크기와 해시가 모두 맞아, 증상이
        /// "맵의 절반에서만 열쇠가 벽에 박힘" 으로 나타난다.
        public int CellIndex(int floor, int x, int z)
        {
            return ((floor * Depth) + z) * Width + x;
        }

        /// `Cells` 인덱스 → 격자 좌표. <see cref="CellIndex"/> 의 역이다.
        ///
        /// **역도 여기에 둔다.** 경로 탐색이 셀 번호로 큐를 돌리고 좌표로 되돌려야 하는데,
        /// 그쪽에서 나누기를 다시 적으면 순서가 어긋날 자리가 하나 더 생긴다 — 그리고 그
        /// 어긋남은 위의 주석대로 크기도 해시도 맞은 채 격자만 90도 돌아간 모습으로 나타난다.
        public void CellCoords(int cell, out int floor, out int x, out int z)
        {
            var perFloor = Width * Depth;

            floor = cell / perFloor;

            var rest = cell - (floor * perFloor);

            z = rest / Width;
            x = rest - (z * Width);
        }

        public bool InBounds(int floor, int x, int z)
        {
            return floor >= 0 && floor < Floors
                && x >= 0 && x < Width
                && z >= 0 && z < Depth;
        }

        /// 범위를 벗어난 좌표는 `None` 이다. 예외를 던지지 않는 편이 맞다 —
        /// 배치 후보를 훑는 코드가 경계에서 좌표를 하나씩 넘겨보는 것이 정상 경로다.
        public MapCellFlags At(int floor, int x, int z)
        {
            if (Cells == null || !InBounds(floor, x, z))
            {
                return MapCellFlags.None;
            }

            var index = CellIndex(floor, x, z);
            return index >= 0 && index < Cells.Length
                ? (MapCellFlags)Cells[index]
                : MapCellFlags.None;
        }

        public bool Has(int floor, int x, int z, MapCellFlags flag)
        {
            return (At(floor, x, z) & flag) == flag;
        }

        /// 셀 중심의 **발밑** 월드 좌표. y 는 그 층의 바닥면이다.
        ///
        /// 서버의 위치 규약이 발밑 기준이므로(`MapSpawn` 과 같다) 여기도 발밑이다.
        /// 눈높이나 박스 중심이 필요한 쪽에서 `SimConstants` 로 올려 쓴다.
        ///
        /// **이 식도 여기에만 있어야 한다.** 클라이언트의 셀 중심 계산과 어긋나면
        /// export 한 격자와 클라이언트가 그리는 것이 반 셀씩 밀리고, 증상은 "열쇠가
        /// 벽에 반쯤 박혀 보임" 이다.
        public Vector3 CellToWorld(int floor, int x, int z)
        {
            return new Vector3(
                OriginX + ((x + 0.5f) * CellSize),
                floor * FloorHeight,
                OriginZ + ((z + 0.5f) * CellSize));
        }

        /// 월드 좌표가 어느 셀인가. 그 셀이 걸을 수 있는 곳인지는 보지 않는다.
        ///
        /// 범위를 벗어나면 `false` 이고, 그때도 `floor`/`x`/`z` 에는 계산된 값이 들어간다 —
        /// 호출자가 경계를 클램프해 쓰는 경우가 있다.
        ///
        /// `CellToWorld` 의 역이다. 두 식이 갈리면 "가장 가까운 자리" 탐색이 엉뚱한 셀에서
        /// 시작하고, 증상은 순간이동이 가끔 벽 쪽으로 붙는 것으로만 나타난다.
        public bool TryWorldToCell(Vector3 world, out int floor, out int x, out int z)
        {
            floor = FloorIndexAt(world.Y);

            if (CellSize <= 0f)
            {
                x = 0;
                z = 0;
                return false;
            }

            // MathF.Floor 는 IEEE 754 가 결과를 규정하므로 결정성에 안전하다
            // (`conventions.md` §시뮬레이션). 단순 (int) 캐스팅은 음수를 0 쪽으로
            // 절단해 격자 밖 좌표에서 셀 하나가 밀린다.
            x = (int)MathF.Floor((world.X - OriginX) / CellSize);
            z = (int)MathF.Floor((world.Z - OriginZ) / CellSize);

            return InBounds(floor, x, z);
        }

        /// 월드 y 가 몇 층인가. 내림이며 올림하지 않는다.
        ///
        /// 올리면 점프 중인 플레이어가 위층으로 올라간다 — 점프 정점이 1.2m 이고 층
        /// 간격이 3.2m 라, *가장 가까운* 층 높이는 머리 위쪽이 된다. 클라이언트의
        /// `FloorIndexAt` 이 같은 이유로 같은 규칙을 쓴다.
        public int FloorIndexAt(float worldY)
        {
            if (FloorHeight <= 0f)
            {
                return 0;
            }

            // 한쪽으로만 여유를 둔다. 서버의 발밑은 접촉면에서 SkinWidth 만큼 낮게 앉는다.
            var index = (int)((worldY + 0.35f) / FloorHeight);

            if (index < 0)
            {
                return 0;
            }

            return index >= Floors ? Floors - 1 : index;
        }

        /// 스키마가 자기 자신과 맞는가.
        ///
        /// `Shared` 에 두어 클라이언트도 export 직전에 같은 검사를 할 수 있게 한다.
        /// 어긋난 격자를 파일로 내보내면 서버가 그것을 그대로 신뢰하고, 잘못은
        /// 한참 뒤 배치 단계에서 드러난다.
        public bool TryValidate(out string error)
        {
            if (Floors <= 0 || Width <= 0 || Depth <= 0)
            {
                error = $"격자 크기가 잘못됐다: floors={Floors} width={Width} depth={Depth}";
                return false;
            }

            if (CellSize <= 0f || FloorHeight <= 0f)
            {
                error = $"셀 크기나 층 높이가 0 이하다: cellSize={CellSize} floorHeight={FloorHeight}";
                return false;
            }

            if (Cells == null)
            {
                error = "격자에 셀 배열이 없다.";
                return false;
            }

            if (Cells.Length != CellCount)
            {
                error = $"셀 수가 크기와 맞지 않는다: cells={Cells.Length} 기대값={CellCount}";
                return false;
            }

            error = null;
            return true;
        }

        /// 해시에 격자를 싣는다.
        ///
        /// 크기와 원점을 셀 내용보다 먼저 넣는다. 셀 수가 같고 내용만 같은 두 격자가
        /// 서로 다른 원점을 가질 수 있고, 그것을 빼면 좌표계가 어긋난 채 해시가 맞는다.
        public uint CombineInto(uint hash)
        {
            hash = StateHash.Combine(hash, Floors);
            hash = StateHash.Combine(hash, Width);
            hash = StateHash.Combine(hash, Depth);
            hash = StateHash.Combine(hash, CellSize);
            hash = StateHash.Combine(hash, FloorHeight);
            hash = StateHash.Combine(hash, OriginX);
            hash = StateHash.Combine(hash, OriginZ);

            if (Cells != null)
            {
                hash = StateHash.Combine(hash, (uint)Cells.Length);

                for (var index = 0; index < Cells.Length; index++)
                {
                    hash = StateHash.Combine(hash, Cells[index]);
                }
            }

            return hash;
        }
    }
}
