using UnityEngine;

namespace NV.Client.Net
{
    /// 씬에 손으로 놓은 지형을 서버에도 알린다.
    ///
    /// **이 컴포넌트가 없으면 서버는 씬의 콜라이더를 전혀 모른다.** export 대상은
    /// <see cref="INetworkMapSource"/> 구현체 하나뿐이고, 그 구현체는 코드가 `AddBox` 로 등록한
    /// 박스만 내놓는다. 씬에 프랍이나 기물을 직접 놓으면 클라이언트에는 벽이 있고 서버에는
    /// 없으며, 증상은 "아무것도 없는데 막힘" 또는 "벽을 통과함" 이다. **맵 해시는 그때도
    /// 일치한다** — 해시는 export 된 목록만 보므로 export 되지 않은 지형을 잡을 수 없다.
    ///
    /// **씬의 모든 콜라이더를 자동으로 긁지 않는 이유.** 뷰모델, 트리거, 장식용 콜라이더가 전부
    /// 지형이 된다. 무엇을 서버에 알릴지는 결정이므로 명시해야 하고, 이 컴포넌트를 붙이는 것이
    /// 그 결정이다.
    ///
    /// **축 정렬 박스만 지원한다.** 서버의 판정은 AABB 스윕이고 스키마도 AABB 다. 회전한
    /// 콜라이더를 AABB 로 감싸면 클라이언트가 막지 않는 곳을 서버가 막으므로, 회전한 것은
    /// export 가 **거절한다** — 나중에 "왜 여기서 걸리지" 로 만나게 두지 않는다.
    [RequireComponent(typeof(BoxCollider))]
    [DisallowMultipleComponent]
    public sealed class NVCollisionVolume : MonoBehaviour
    {
        /// 회전을 이 값까지는 봐준다. 손으로 놓은 오브젝트는 정확히 0 이 아니기 쉽고,
        /// 그 정도의 기울기는 AABB 로 감싸도 서버와 클라이언트가 갈리지 않는다.
        public const float RotationToleranceDegrees = 0.5f;

        /// 이 볼륨을 서버에 알릴 수 없는 이유. 알릴 수 있으면 <c>null</c>.
        ///
        /// 판정을 컴포넌트에 두는 이유는 인스펙터에서도 같은 답을 보여 줄 수 있기 때문이고,
        /// export 와 그 표시가 갈리지 않게 하려면 한 곳에 있어야 한다.
        public string DescribeRejection()
        {
            var collider = GetComponent<BoxCollider>();

            if (collider == null)
            {
                return "BoxCollider 가 없다.";
            }

            var tilt = Quaternion.Angle(transform.rotation, Quaternion.identity);

            if (tilt > RotationToleranceDegrees)
            {
                return $"{tilt:F1}° 회전해 있다. 서버는 축 정렬 박스만 판정하므로 " +
                       "AABB 로 감싸면 클라이언트가 막지 않는 곳을 서버가 막는다.";
            }

            return null;
        }

        /// 서버에 알릴 박스. 알릴 수 없으면 <c>false</c>.
        public bool TryGetWorldBounds(out Bounds bounds)
        {
            bounds = default;

            var collider = GetComponent<BoxCollider>();

            if (collider == null || DescribeRejection() != null)
            {
                return false;
            }

            // `Collider.bounds` 는 런타임에만 믿을 수 있다(에디트 모드에서 갱신이 늦는다).
            // 중심과 크기를 직접 변환하면 두 모드에서 같은 값이 나온다.
            var centre = transform.TransformPoint(collider.center);
            var scale = transform.localToWorldMatrix.lossyScale;

            bounds = new Bounds(
                centre,
                new Vector3(
                    Mathf.Abs(collider.size.x * scale.x),
                    Mathf.Abs(collider.size.y * scale.y),
                    Mathf.Abs(collider.size.z * scale.z)));

            return true;
        }

        private void OnDrawGizmosSelected()
        {
            if (!TryGetWorldBounds(out var bounds))
            {
                return;
            }

            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.6f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
