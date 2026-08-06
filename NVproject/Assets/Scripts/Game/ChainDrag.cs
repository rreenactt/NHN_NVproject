using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace NV.Game
{
    /// <summary>
    /// What emptying the magazine costs. The Seeker fires three rounds; a chain then comes out of
    /// the <see cref="ChainAltar"/> in the middle of the ground floor, hauls them back to it, and
    /// holds them there for three seconds before the reload even begins.
    ///
    /// This is the game's main balancing lever, and anchoring it to one fixed place is what gives
    /// it teeth. A chain that grabbed the nearest wall cost three seconds; a chain that always
    /// pulls you back to the middle of the ground floor costs three seconds *and everything you
    /// had walked to reach*. The Seeker knows exactly where the third shot sends them, which is
    /// the difference between a punishment and a nuisance.
    ///
    /// The drag is not a teleport, and it is not a straight line either. The chain takes the
    /// shortest *walkable* route — round corners, through doorways, down the stairs — and the
    /// Seeker is reeled along it, so going the long way round genuinely takes longer. The look is
    /// left free throughout: the point is to watch the Runners get away.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChainDrag : MonoBehaviour
    {
        private static Material _chainMaterial;

        public PlayerAgent agent;
        public FirstPersonController controller;
        public WeaponController weapon;

        private Transform _chain;       // pool root; the links are its children
        private Vector3 _chainOrigin;   // the ring on the altar, not the point you land on
        private Vector3[] _route;       // corners from the Seeker to the landing spot
        private float _routeLength;
        private Coroutine _routine;
        private bool _serverDriven;
        private bool _routeWalkable;    // true only when the NavMesh solved it; never the fallback

        public bool Active { get; private set; }

        /// <summary>Seconds left of the hold, for the HUD. 0 when not chained.</summary>
        public float Remaining { get; private set; }

        private void Awake()
        {
            if (agent == null) agent = GetComponent<PlayerAgent>();
            if (controller == null) controller = GetComponent<FirstPersonController>();
            if (weapon == null) weapon = GetComponent<WeaponController>();
        }

        /// <summary>Called by the weapon the moment the last round leaves the magazine.</summary>
        public void Trigger()
        {
            if (Active || !isActiveAndEnabled) return;
            if (agent != null && !agent.InPlay) return;

            _routine = StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            MatchManager match = MatchManager.Instance;
            GameConfig config = match != null ? match.Config : null;
            if (config == null) yield break;

            // **누가 몸을 옮기는가.** 서버가 전투를 판정하는 매치에서는 견인도 서버의 것이다
            // (`Room.StepChain`) — 여기서 `Teleport` 를 부르면 다음 스냅샷이 그것을 되돌리고,
            // 증상은 Seeker 가 제단 쪽으로 끌리다 말고 튕겨 돌아오는 것이다. 그때 이 컴포넌트가
            // 하는 일은 체인을 그리는 것뿐이고, 몸은 서버가 보내는 위치를 따라간다.
            bool serverDriven = match.ServerOwnsCombat;
            _serverDriven = serverDriven;

            Active = true;
            agent?.SetChained(true);
            if (weapon != null) weapon.FireBlocked = true;

            FindAnchor(config, match, out Vector3 destination, out Vector3 chainOrigin);
            _chainOrigin = chainOrigin;

            // The route the chain takes, and the route the Seeker is hauled along: corridors and
            // doorways, not walls.
            BuildRoute(transform.position, destination);
            UpdateChain(0, transform.position);

            match?.Notify(ChainAltar.Instance != null ? "THE ALTAR TAKES YOU BACK" : "CHAINED");

            // 1. the haul, paced by the length of the route rather than the gap across the map —
            // going the long way round genuinely takes longer.
            float length = _routeLength;
            float travel = Mathf.Clamp(length / Mathf.Max(1f, config.chainDragSpeed),
                                       config.chainDragTime, config.chainDragMaxTime);

            if (serverDriven)
            {
                // 체인이 끝나는 시점도 서버가 정한다. 여기서 로컬 타이머로 끝내면 서버가 아직
                // 끌고 가는 중인데 화면에서만 풀려 — 조작이 돌아온 것처럼 보이다가 다음
                // 스냅샷에 다시 묶인다. 서버가 놓아주는 신호는 **탄창이 다시 찬 것**이다
                // (`Room.StepChain` 이 해제 틱에 그것 하나를 한다).
                //
                // 상한을 둔다. 신호가 오지 않는 경우(매치가 끝났다, 소켓이 끊겼다)에 영원히
                // 묶인 채로 남는 것이 가장 나쁘다.
                float deadline = travel + config.chainWait + 5f;

                for (float t = 0f; t < deadline; t += Time.deltaTime)
                {
                    // 몸은 서버가 옮긴다. 체인은 지금 몸이 있는 곳에서 제단까지 다시 그린다.
                    UpdateChain(NearestSegment(transform.position), transform.position);

                    Remaining = Mathf.Max(0f, (travel + config.chainWait) - t);

                    if (weapon != null && weapon.Ammo > 0)
                    {
                        break;
                    }

                    yield return null;
                }

                Remaining = 0f;
                DestroyChain();
                agent?.SetChained(false);

                // **재장전하지 않는다.** 탄창은 서버가 채우고 `MatchSync` 가 `AcceptAmmo` 로
                // 넘긴다. 여기서 `ForceReload` 를 부르면 로컬로 3발이 생겼다가 0.5초 뒤
                // 전문이 그것을 되돌린다.
                if (weapon != null) weapon.FireBlocked = false;

                Active = false;
                _routine = null;
                yield break;
            }

            // Collision stays off for the haul. The route is walkable by construction, but the
            // corners hug wall edges and a CharacterController scraping them at 26 m/s would
            // arrive somewhere of its own choosing.
            if (controller != null) controller.Phasing = true;

            for (float t = 0f; t < travel; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / travel);
                Vector3 here = PointAlongRoute(k * length, out int segment);

                if (controller != null) controller.Teleport(here);
                else transform.position = here;

                UpdateChain(segment, here);
                Remaining = config.chainWait + (travel - t);
                yield return null;
            }

            // The loop always stops a fraction short. Land exactly on the spot, then hand the body
            // back to physics.
            if (controller != null)
            {
                controller.Teleport(destination);
                controller.Phasing = false;
            }
            else transform.position = destination;

            // 2. the wait. This is the part that hurts.
            for (float t = config.chainWait; t > 0f; t -= Time.deltaTime)
            {
                Remaining = t;
                UpdateChain(_route.Length - 1, transform.position);
                yield return null;
            }

            // 3. only now does the magazine come back
            Remaining = 0f;
            DestroyChain();
            agent?.SetChained(false);

            if (weapon != null)
            {
                weapon.FireBlocked = false;
                weapon.ForceReload(config.chainReloadTime);
            }

            Active = false;
            _routine = null;
        }

        /// <summary>
        /// Where the chain bites, and where it puts you.
        ///
        /// The altar on the ground floor is the anchor whenever one exists, and the two points are
        /// not the same: the chain is bolted to the ring on top of it, but the Seeker lands on the
        /// floor *beside* it, because dropping a body inside a solid plinth is how somebody ends up
        /// stuck in the scenery.
        ///
        /// The wall search is only the fallback for a level with no altar in it.
        /// </summary>
        private void FindAnchor(GameConfig config, MatchManager match,
            out Vector3 destination, out Vector3 chainOrigin)
        {
            ChainAltar altar = ChainAltar.Instance;
            if (altar != null)
            {
                destination = altar.DragPoint;
                chainOrigin = altar.ChainOrigin;
                return;
            }

            destination = FindWallAnchor(config, match);
            chainOrigin = destination + Vector3.up * 1.1f;
        }

        private Vector3 FindWallAnchor(GameConfig config, MatchManager match)
        {
            Vector3 origin = transform.position + Vector3.up * 1.2f;
            float best = float.MaxValue;
            Vector3 point = transform.position;
            bool found = false;

            for (int i = 0; i < 12; i++)
            {
                float angle = i * (Mathf.PI * 2f / 12f);
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                if (!Physics.Raycast(origin, direction, out RaycastHit hit, config.chainAnchorRange,
                                     ~0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.distance < 1.2f) continue;                 // already against that wall
                if (hit.distance >= best) continue;

                best = hit.distance;
                point = hit.point - direction * 0.75f;              // stop short of the wall itself
                found = true;
            }

            if (!found) return transform.position;

            point.y = transform.position.y;

            // Never end up inside geometry: the grid knows what is standable, the raycast does not.
            if (match != null && match.Map != null
                && match.Map.TryNearestStandablePoint(point, out Vector3 standable))
                point = new Vector3(standable.x, point.y, standable.z);

            return point;
        }

        // ============================================================ the route

        /// <summary>
        /// Works out how the chain actually gets to you: the shortest walkable route, which on this
        /// level means down corridors, through doorways and down the stairwell if you were upstairs.
        ///
        /// It asks the NavMesh rather than running a grid A* by hand, and that is not laziness —
        /// Unity's path solve *is* A*, over the navmesh polygons, and it already knows the one thing
        /// a grid search would have to be taught: that the two floors are joined by a staircase and
        /// not by a hole in the ceiling. The bake happens with the level, so the data is free.
        ///
        /// If there is no navmesh, or no route (someone standing somewhere unreachable), it falls
        /// back to the straight line — a chain that arrives late is better than a Seeker stuck with
        /// an empty magazine forever.
        /// </summary>
        private void BuildRoute(Vector3 from, Vector3 to)
        {
            _route = null;
            _routeLength = 0f;
            _routeWalkable = false;

            // 1.5 m, not 4. The bake leaves a walkable strip along the *tops of the top-floor
            // walls*, 3.1 m above the upper floor, and a generous sample radius snaps a Seeker
            // standing upstairs onto that roof instead of onto the floor they are on — the route
            // then comes back PathPartial and wanders around up there. A metre and a half is
            // plenty to find the floor beneath your feet and far too short to reach the roof.
            if (NavMesh.SamplePosition(from, out NavMeshHit fromHit, 1.5f, NavMesh.AllAreas)
                && NavMesh.SamplePosition(to, out NavMeshHit toHit, 1.5f, NavMesh.AllAreas))
            {
                var path = new NavMeshPath();
                if (NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, path)
                    && path.status == NavMeshPathStatus.PathComplete
                    && path.corners.Length >= 2)
                {
                    _route = path.corners;
                    _routeWalkable = true;

                    // Both ends are pinned to the real thing rather than to the navmesh's nearest
                    // point, so the chain starts at the Seeker and finishes on the landing spot.
                    _route[0] = from;
                    _route[_route.Length - 1] = to;
                }
            }

            _route ??= new[] { from, to };

            for (int i = 1; i < _route.Length; i++)
                _routeLength += Vector3.Distance(_route[i - 1], _route[i]);
        }

        /// <summary>
        /// Which corner of the route the body has been hauled past, judged from where the body
        /// actually is.
        ///
        /// The offline haul knows this for free — it *is* the thing walking the route, so it hands
        /// the segment index straight to <see cref="UpdateChain"/>. Server-driven, the body arrives
        /// as a stream of positions with no index attached, so the chain has to work out how much
        /// of itself has been reeled in. Nearest corner is enough: the route's corners are metres
        /// apart and the chain only needs to lose a bend at roughly the right moment.
        /// </summary>
        private int NearestSegment(Vector3 position)
        {
            if (_route == null || _route.Length < 2) return 0;

            int best = 0;
            float bestDistance = float.MaxValue;

            // The last corner is the landing spot; the chain should still draw one link to the
            // ring when the body is standing on it, so the search stops one short.
            for (int i = 0; i < _route.Length - 1; i++)
            {
                float distance = (_route[i] - position).sqrMagnitude;
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = i;
            }

            return best;
        }

        /// <summary>
        /// Whether the body being dragged should be *drawn on this route* rather than at the raw
        /// server position. Server-driven only: the server sends one on-route point per tick, and
        /// anything that connects those points with straight lines — snapshot interpolation,
        /// SmoothDamp — turns each corner into a chord that passes through the wall beside it.
        /// <see cref="NetworkBootstrap"/> projects the server position onto this route instead.
        ///
        /// The straight-line fallback is deliberately excluded. The server hauls along its own
        /// grid A* — walls respected — and projecting those positions onto a two-point chord
        /// *forces* the body through every wall between here and the altar: worse than doing
        /// nothing. No walkable route, no projection.
        /// </summary>
        public bool HasRoute => Active && _serverDriven && _routeWalkable
            && _route != null && _route.Length >= 2;

        public float RouteLength => _routeLength;

        /// <summary>
        /// Projects a position onto the route and returns how far along it that is, in metres.
        /// Nearest-segment, like <see cref="NearestSegment"/>, and for the same reason: the body
        /// arrives as a stream of positions with no route index attached.
        /// </summary>
        public float ProjectOntoRoute(Vector3 position)
        {
            if (_route == null || _route.Length < 2) return 0f;

            float best = 0f;
            float bestDistance = float.MaxValue;
            float arc = 0f;

            for (int i = 1; i < _route.Length; i++)
            {
                Vector3 from = _route[i - 1];
                Vector3 segment = _route[i] - from;
                float lengthSq = segment.sqrMagnitude;
                float t = lengthSq < 1e-8f
                    ? 0f
                    : Mathf.Clamp01(Vector3.Dot(position - from, segment) / lengthSq);

                float distance = (position - (from + segment * t)).sqrMagnitude;
                float segmentLength = Mathf.Sqrt(lengthSq);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = arc + segmentLength * t;
                }

                arc += segmentLength;
            }

            return best;
        }

        /// <summary>The point a given distance along the route.</summary>
        public Vector3 PointOnRoute(float distance) => PointAlongRoute(distance, out _);

        /// <summary>Walks the route to find the point a given distance along it.</summary>
        private Vector3 PointAlongRoute(float distance, out int segment)
        {
            segment = 0;
            if (_route == null || _route.Length == 0) return transform.position;

            float remaining = distance;
            for (int i = 1; i < _route.Length; i++)
            {
                float step = Vector3.Distance(_route[i - 1], _route[i]);
                if (remaining <= step || i == _route.Length - 1)
                {
                    segment = i - 1;
                    return Vector3.Lerp(_route[i - 1], _route[i],
                                        step > 1e-4f ? Mathf.Clamp01(remaining / step) : 1f);
                }
                remaining -= step;
            }

            segment = _route.Length - 1;
            return _route[_route.Length - 1];
        }

        // ============================================================ the chain itself

        /// <summary>
        /// Redraws the chain as the *remaining* route: from the Seeker's waist, through every
        /// corner still ahead of them, to the ring on the altar. It shortens and loses a bend each
        /// time they are pulled around one.
        ///
        /// Links are reused from the pool under <see cref="_chain"/> rather than rebuilt, and the
        /// pool is read back out of the hierarchy rather than held in a list — a List of Transforms
        /// is managed state, and this project has been bitten by that often enough.
        /// </summary>
        private void UpdateChain(int fromSegment, Vector3 seekerPosition)
        {
            if (_route == null) return;
            if (_chain == null)
            {
                var root = new GameObject("Chain");
                root.transform.SetParent(transform.parent, false);
                _chain = root.transform;
            }

            // waist -> every corner still ahead -> the ring
            int cornerCount = Mathf.Max(0, _route.Length - 1 - (fromSegment + 1) + 1);
            int pointCount = 1 + cornerCount + 1;

            EnsureLinks(pointCount - 1);

            int link = 0;
            Vector3 previous = seekerPosition + Vector3.up * 1.1f;

            for (int i = fromSegment + 1; i < _route.Length; i++)
            {
                Vector3 corner = _route[i] + Vector3.up * 1.1f;
                PlaceLink(link++, previous, corner);
                previous = corner;
            }

            PlaceLink(link++, previous, _chainOrigin);

            for (int i = link; i < _chain.childCount; i++)
                _chain.GetChild(i).gameObject.SetActive(false);
        }

        private void EnsureLinks(int count)
        {
            while (_chain.childCount < count)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Link";
                Destroy(go.GetComponent<Collider>());
                go.GetComponent<MeshRenderer>().sharedMaterial = ChainMaterial();
                go.transform.SetParent(_chain, false);
            }
        }

        private void PlaceLink(int index, Vector3 from, Vector3 to)
        {
            if (index >= _chain.childCount) return;

            Transform link = _chain.GetChild(index);
            link.gameObject.SetActive(true);

            float length = Vector3.Distance(from, to);
            link.position = (from + to) * 0.5f;
            link.rotation = length > 0.01f
                ? Quaternion.LookRotation((to - from).normalized)
                : Quaternion.identity;
            link.localScale = new Vector3(0.11f, 0.11f, Mathf.Max(0.05f, length));
        }

        private void DestroyChain()
        {
            if (_chain != null) Destroy(_chain.gameObject);
            _chain = null;
        }

        private void OnDisable()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            DestroyChain();

            // Never leave the body phased through the world because the drag was interrupted —
            // a role swap or a restart mid-haul would otherwise drop collision permanently.
            if (controller != null) controller.Phasing = false;
            Remaining = 0f;
            Active = false;
            agent?.SetChained(false);
            if (weapon != null) weapon.FireBlocked = false;
        }

        private static Material ChainMaterial()
        {
            if (_chainMaterial != null) return _chainMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _chainMaterial = new Material(shader)
            {
                name = "Chain",
                color = new Color(0.18f, 0.17f, 0.16f),
            };
            if (_chainMaterial.HasProperty("_Metallic")) _chainMaterial.SetFloat("_Metallic", 0.9f);
            if (_chainMaterial.HasProperty("_Smoothness")) _chainMaterial.SetFloat("_Smoothness", 0.6f);
            return _chainMaterial;
        }
    }
}
