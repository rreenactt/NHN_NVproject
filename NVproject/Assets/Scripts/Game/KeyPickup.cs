using UnityEngine;

namespace NV.Game
{
    /// <summary>
    /// One of the ten keys. Picked up by walking over it — no keypress, because a Runner being
    /// chased has none to spare — and only by Runners.
    ///
    /// Keys are visible to *everyone*, unlike the door. The ruleset hides the door and the key
    /// *progress* from the Seeker, not the objects: a key lying in a corridor is a physical thing,
    /// and letting the Seeker see one is what makes camping a key a real tactic.
    ///
    /// Pickup is a distance poll rather than a trigger volume. A CharacterController raises
    /// trigger events but a NavMeshAgent-driven practice Runner does not, and ten keys against a
    /// handful of agents is a few dozen distance checks a frame.
    /// </summary>
    public sealed class KeyPickup : MonoBehaviour
    {
        private static Material _sharedMaterial;

        private Transform _spin;
        private float _bobPhase;

        public bool Collected { get; private set; }

        public static KeyPickup Spawn(Vector3 groundPosition, Transform parent)
        {
            var go = new GameObject("Key");
            go.transform.SetParent(parent, false);
            go.transform.position = groundPosition + Vector3.up * 0.55f;

            var key = go.AddComponent<KeyPickup>();
            key.Build();
            return key;
        }

        private void Build()
        {
            _bobPhase = Random.value * Mathf.PI * 2f;

            var pivot = new GameObject("Spin");
            pivot.transform.SetParent(transform, false);
            _spin = pivot.transform;

            // A key made of blocks, like everything else in this project: a shaft and two teeth.
            AddBlock(_spin, new Vector3(0f, 0f, 0f), new Vector3(0.09f, 0.34f, 0.09f));
            AddBlock(_spin, new Vector3(0.09f, -0.10f, 0f), new Vector3(0.10f, 0.08f, 0.09f));
            AddBlock(_spin, new Vector3(0.09f, 0.03f, 0f), new Vector3(0.10f, 0.08f, 0.09f));
            AddBlock(_spin, new Vector3(0f, 0.24f, 0f), new Vector3(0.20f, 0.14f, 0.09f));
        }

        private static void AddBlock(Transform parent, Vector3 offset, Vector3 size)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(block.GetComponent<Collider>());   // a key must never stop a bullet
            block.transform.SetParent(parent, false);
            block.transform.localPosition = offset;
            block.transform.localScale = size;
            block.GetComponent<MeshRenderer>().sharedMaterial = SharedMaterial();
        }

        private void Update()
        {
            if (Collected) return;

            // Spin and bob. In a level this uniform a static prop on the carpet is invisible;
            // motion is the only thing that separates a key from a floor tile at ten metres.
            if (_spin != null) _spin.Rotate(Vector3.up, 70f * Time.deltaTime, Space.Self);
            float bob = Mathf.Sin(Time.time * 1.9f + _bobPhase) * 0.07f;
            transform.localPosition = new Vector3(transform.localPosition.x,
                                                  _baseY + bob,
                                                  transform.localPosition.z);

            MatchManager match = MatchManager.Instance;
            if (match == null || match.Phase != MatchPhase.Playing) return;

            // Networked, the server decides who picked this up: it polls the same distance against
            // the authoritative positions and the key disappears from the objective bulletin, which
            // is what removes this object. Deciding it here as well is the cheatable seam — a client
            // that says "I took it" is a client that took every key in the level.
            //
            // The offline path keeps the poll. There is no server to ask, and the practice level is
            // where the Seeker's half of the ruleset gets exercised.
            if (match.ServerOwnsObjectives) return;

            float radius = match.Config.keyPickupRadius;
            var agents = match.Agents;
            for (int i = 0; i < agents.Count; i++)
            {
                PlayerAgent agent = agents[i];
                if (agent == null || agent.Role != Role.Runner || !agent.InPlay) continue;

                Vector3 delta = agent.FeetPosition - transform.position;
                // Generous horizontally, tight vertically — the floor above must not vacuum up
                // keys lying on the floor below. The number is shared with the server's copy of
                // this test, so the offline poll and the authoritative one cannot drift apart.
                if (Mathf.Abs(delta.y) > match.Config.keyPickupHeight) continue;
                delta.y = 0f;
                if (delta.sqrMagnitude > radius * radius) continue;

                if (match.TryPickUpKey(agent, this)) return;
            }
        }

        private float _baseY;

        private void Start() => _baseY = transform.localPosition.y;

        /// <summary>Taken by a Runner. The manager calls this; it is not a pickup decision of its own.</summary>
        internal void Collect()
        {
            Collected = true;
            Destroy(gameObject);
        }

        private static Material SharedMaterial()
        {
            if (_sharedMaterial != null) return _sharedMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _sharedMaterial = new Material(shader) { name = "Key" };
            var gold = new Color(1f, 0.83f, 0.32f);
            _sharedMaterial.color = gold;
            _sharedMaterial.EnableKeyword("_EMISSION");
            _sharedMaterial.SetColor("_EmissionColor", gold * 2.2f);
            _sharedMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            _sharedMaterial.enableInstancing = true;
            return _sharedMaterial;
        }
    }
}
