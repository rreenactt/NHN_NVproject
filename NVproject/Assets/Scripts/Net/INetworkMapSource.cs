using System.Collections.Generic;
using NV.Shared.Collision;
using UnityEngine;

namespace NV.Client.Net
{
    /// 서버에 넘길 콜리전을 내놓을 수 있는 레벨.
    ///
    /// 레벨은 전부 코드로 생성되므로 콜리전의 출처도 그 생성기여야 한다. 서버용 맵을
    /// 따로 만들면 둘이 갈리고, 증상은 "아무것도 없는 곳에서 막힘" 또는 "벽을 통과함"
    /// 으로만 나타난다. 새 레벨을 만들면 이 인터페이스를 구현하고, export 와 해시 대조는
    /// <see cref="MapExport"/> 가 그대로 처리한다.
    public interface INetworkMapSource
    {
        /// 맵 파일명과 해시에 함께 들어간다. 서버와 클라이언트가 같은 값을 써야 한다.
        string MapName { get; }

        /// 런타임에 이미 만들어진 콜리전 박스. 생성 전이면 비어 있다.
        IReadOnlyList<Bounds> CollisionBoxes { get; }

        /// 지오메트리를 만들지 않고 콜리전만 계산한다. 에디터 export 가 쓴다 —
        /// 에디트 모드에서 레벨을 씬에 통째로 쏟아 놓을 수는 없다.
        IReadOnlyList<Bounds> ComputeCollision();

        /// 스폰 지점. 위치는 발밑 기준, yaw 는 라디안이고 0 이 +Z 다.
        void GetSpawns(List<(Vector3 position, float yaw)> into);

        /// 걸을 수 있는 곳의 격자. 격자를 내놓지 않는 레벨은 <c>null</c> 을 돌려준다.
        ///
        /// 서버는 지형을 콜리전 박스로만 알아서 "여기 설 수 있는가" 를 답할 수 없다.
        /// 목표물 배치와 피격 시 순간이동 지점이 그 답을 필요로 하므로, 격자를 만드는
        /// 쪽이 — 레벨 생성기 자신이 — export 에 함께 실어 준다. 서버가 생성기 로직을
        /// 다시 구현하면 씨드를 바꿀 때마다 두 곳이 갈린다.
        ///
        /// **<see cref="MapCellFlags.Standable"/> 과 <see cref="MapCellFlags.StairLink"/>
        /// 만 채운다.** <see cref="MapCellFlags.FreeFloor"/> 는 <see cref="MapExport"/> 가
        /// 콜리전 박스와 서버의 플레이어 박스로 계산해 덧붙인다 — 그 플래그의 뜻이
        /// "서버가 여기에 플레이어를 놓아도 밀려나지 않는다" 이므로, 판정을 서버와
        /// 공유하는 코드에 두어야 기준이 갈리지 않는다.
        ///
        /// <c>null</c> 이 정상인 경우가 있다. 매치 규칙을 돌리지 않는 개발용 맵은 배치할
        /// 목표물이 없으므로 격자가 필요 없고, 없으면 맵 해시에도 들어가지 않는다.
        MapGridData BuildGrid();
    }
}
