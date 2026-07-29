using System.Collections.Generic;
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
    }
}
