using UnityEngine;

namespace NV.Game
{
    /// <summary>
    /// The altar in the middle of the ground floor. The chain that punishes an empty magazine
    /// comes out of the iron ring on top of it, and hauls the Seeker back here from wherever they
    /// were standing.
    ///
    /// Giving the chain a *place* changes what it means. Anchored to whatever wall happened to be
    /// nearest, it was an inconvenience — three seconds against some wall. Anchored here, the
    /// Seeker knows exactly where the third shot sends them, and every magazine is a bet on
    /// whether the kill is worth being dragged back to the middle of the map.
    ///
    /// It looks deliberately wrong for the level: everything else in here is pale yellow office,
    /// and this is dark stone with a hot ring of light on it.
    /// </summary>
    public sealed class ChainAltar : MonoBehaviour
    {
        private static ChainAltar _instance;

        /// <summary>Lazily re-found, like the other singletons here — a domain reload wipes the static.</summary>
        public static ChainAltar Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<ChainAltar>();
                return _instance;
            }
            private set => _instance = value;
        }

        private static Material _stoneMaterial, _slabMaterial, _ringMaterial;

        private Transform _ring;
        private Vector3 _dragPoint;

        /// <summary>Where the chain is bolted — the top of the ring, not the floor.</summary>
        public Vector3 ChainOrigin => _ring != null ? _ring.position : transform.position + Vector3.up * 1.1f;

        /// <summary>Where a dragged Seeker actually lands: standable floor beside the altar, not inside it.</summary>
        public Vector3 DragPoint => _dragPoint;

        public static ChainAltar Spawn(Vector3 groundPosition, Vector3 dragPoint, Transform parent)
        {
            var go = new GameObject("Chain Altar");
            go.transform.SetParent(parent, false);
            go.transform.position = groundPosition;

            var altar = go.AddComponent<ChainAltar>();
            altar._dragPoint = dragPoint;
            altar.Build();
            return altar;
        }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Build()
        {
            EnsureMaterials();

            // A plinth, a slab, and the ring the chain is bolted through. Waist high: tall enough
            // to read as an object across a foggy room, low enough not to hide a Runner behind.
            AddBlock("Plinth", new Vector3(0f, 0.25f, 0f), new Vector3(1.7f, 0.5f, 1.7f), _stoneMaterial, true);
            AddBlock("Step", new Vector3(0f, 0.58f, 0f), new Vector3(1.35f, 0.16f, 1.35f), _stoneMaterial, true);
            AddBlock("Slab", new Vector3(0f, 0.73f, 0f), new Vector3(1.05f, 0.14f, 1.05f), _slabMaterial, true);

            var ring = new GameObject("Ring");
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            _ring = ring.transform;

            // Four short bars around a gap read as a ring at this size; a torus would need a mesh.
            AddBlock("Ring N", new Vector3(0f, 0.95f, 0.16f), new Vector3(0.34f, 0.07f, 0.07f), _ringMaterial, false);
            AddBlock("Ring S", new Vector3(0f, 0.95f, -0.16f), new Vector3(0.34f, 0.07f, 0.07f), _ringMaterial, false);
            AddBlock("Ring E", new Vector3(0.16f, 0.95f, 0f), new Vector3(0.07f, 0.07f, 0.34f), _ringMaterial, false);
            AddBlock("Ring W", new Vector3(-0.16f, 0.95f, 0f), new Vector3(0.07f, 0.07f, 0.34f), _ringMaterial, false);
            AddBlock("Post", new Vector3(0f, 0.86f, 0f), new Vector3(0.1f, 0.24f, 0.1f), _ringMaterial, false);
        }

        private void Update()
        {
            if (_ringMaterial == null) return;

            // A slow breath rather than a flicker. The fluorescent tubes in this level stutter;
            // this thing is steady, which is what makes it read as not belonging to the building.
            float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.time * 1.1f));
            _ringMaterial.SetColor("_EmissionColor", new Color(0.85f, 0.22f, 0.12f) * pulse * 2.6f);
        }

        private void AddBlock(string name, Vector3 offset, Vector3 size, Material material, bool collider)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(transform, false);
            block.transform.localPosition = offset;
            block.transform.localScale = size;
            block.GetComponent<MeshRenderer>().sharedMaterial = material;

            // The plinth is furniture and stops a body; the ring must not stop a bullet, or the
            // Seeker can shoot their own altar and wonder why the round vanished.
            if (!collider) Destroy(block.GetComponent<Collider>());
        }

        private static void EnsureMaterials()
        {
            if (_stoneMaterial != null && _ringMaterial != null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            _stoneMaterial = new Material(shader) { name = "Altar Stone", color = new Color(0.17f, 0.16f, 0.15f) };
            _slabMaterial = new Material(shader) { name = "Altar Slab", color = new Color(0.09f, 0.08f, 0.08f) };

            _ringMaterial = new Material(shader) { name = "Altar Ring", color = new Color(0.35f, 0.11f, 0.07f) };
            _ringMaterial.EnableKeyword("_EMISSION");
            _ringMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

            if (_stoneMaterial.HasProperty("_Smoothness")) _stoneMaterial.SetFloat("_Smoothness", 0.08f);
            if (_ringMaterial.HasProperty("_Metallic")) _ringMaterial.SetFloat("_Metallic", 0.85f);
        }
    }
}
