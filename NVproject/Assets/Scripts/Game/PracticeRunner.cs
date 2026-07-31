using UnityEngine;
using UnityEngine.AI;

namespace NV.Game
{
    /// <summary>
    /// A Runner with nobody behind it. Not an AI opponent — it wanders, it does not hide, it does
    /// not go for keys — but it is enough to make the Seeker's half of the ruleset testable
    /// offline: something to hit, to make bleed, to teleport across the map and to wipe out.
    ///
    /// It exists because the Seeker's rules (two hits, bleeding trail, teleport-on-hit, the chain)
    /// cannot be checked at all with one player in the level, and none of them should have to wait
    /// on a netcode layer to be verified. In a real match <see cref="GameConfig.practiceRunners"/>
    /// is 0.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PracticeRunner : MonoBehaviour
    {
        private static Material _bodyMaterial;

        private NavMeshAgent _navAgent;
        private PlayerAgent _agent;
        private System.Random _random;
        private float _repathTimer;

        public static PracticeRunner Spawn(string name, Vector3 position, float speed, Transform parent, int seed)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            // Torso and head, blocks like everything else. The collider is one capsule on the
            // root: a bullet that lands anywhere on the figure has to count exactly once.
            AddBlock(go.transform, "Torso", new Vector3(0f, 0.85f, 0f), new Vector3(0.5f, 0.75f, 0.28f));
            AddBlock(go.transform, "Head", new Vector3(0f, 1.45f, 0f), new Vector3(0.32f, 0.32f, 0.32f));
            AddBlock(go.transform, "Leg L", new Vector3(-0.13f, 0.24f, 0f), new Vector3(0.22f, 0.48f, 0.25f));
            AddBlock(go.transform, "Leg R", new Vector3(0.13f, 0.24f, 0f), new Vector3(0.22f, 0.48f, 0.25f));

            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.height = 1.7f;
            capsule.radius = 0.32f;
            capsule.center = new Vector3(0f, 0.85f, 0f);

            var nav = go.AddComponent<NavMeshAgent>();
            nav.speed = speed;
            nav.angularSpeed = 240f;
            nav.acceleration = 12f;
            nav.radius = 0.35f;
            nav.height = 1.7f;
            nav.autoBraking = false;

            var agent = go.AddComponent<PlayerAgent>();
            agent.displayName = name;
            agent.navAgent = nav;
            agent.collectsKeys = false;   // it wanders; it would strip the level of keys

            var runner = go.AddComponent<PracticeRunner>();
            runner._random = new System.Random(seed);
            return runner;
        }

        private void Awake()
        {
            _navAgent = GetComponent<NavMeshAgent>();
            _agent = GetComponent<PlayerAgent>();
        }

        /// <summary>
        /// The wander's random source, rebuilt if it has gone. A plain <see cref="System.Random"/>
        /// is managed state with no Unity serialization behind it, so a domain reload during play
        /// leaves this null while the component keeps running — Awake does not get a second turn.
        /// Every frame afterwards threw inside the level query until this was lazy.
        /// </summary>
        private System.Random Rng => _random ??= new System.Random(GetInstanceID());

        private void Update()
        {
            MatchManager match = MatchManager.Instance;
            if (match == null || _navAgent == null || _agent == null) return;

            if (!_agent.InPlay || _agent.IsFrozen || match.Phase != MatchPhase.Playing) return;
            if (!_navAgent.enabled || !_navAgent.isOnNavMesh) return;

            _repathTimer -= Time.deltaTime;

            bool arrived = !_navAgent.pathPending
                           && _navAgent.remainingDistance <= _navAgent.stoppingDistance + 0.6f;

            // The timer is not decoration: a wandering agent that picks an unreachable point
            // otherwise stands still for the rest of the match, and a Seeker practising against
            // statues learns nothing.
            if (!arrived && _repathTimer > 0f) return;

            if (match.Map != null && match.Map.TryRandomPoint(Rng, out Vector3 point)
                && NavMesh.SamplePosition(point, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                _navAgent.SetDestination(hit.position);
            }
            _repathTimer = 6f;
        }

        private static void AddBlock(Transform parent, string name, Vector3 offset, Vector3 size)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(block.GetComponent<Collider>());   // the root capsule is the only hitbox
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = offset;
            block.transform.localScale = size;
            block.GetComponent<MeshRenderer>().sharedMaterial = BodyMaterial();
        }

        private static Material BodyMaterial()
        {
            if (_bodyMaterial != null) return _bodyMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _bodyMaterial = new Material(shader)
            {
                name = "Practice Runner",
                color = new Color(0.86f, 0.86f, 0.88f),
            };
            _bodyMaterial.enableInstancing = true;
            return _bodyMaterial;
        }
    }
}
