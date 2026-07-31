using UnityEngine;

namespace NV.Game
{
    /// <summary>
    /// The way out. Placed somewhere random each match and, by rule, **visible to Runners only** —
    /// which is enforced by the layer it and all its parts sit on (<see cref="MatchLayers"/>),
    /// not by anything this class does per frame.
    ///
    /// It carries no collider at all. A door the Seeker cannot see but can walk into is worse than
    /// no secret: the Seeker would find it by bumping into thin air. Runners pass through the frame
    /// the same way; standing in the opening once it is unlocked is what escaping means.
    ///
    /// Keys go in one at a time, with a deliberate pause between them. Ten keypresses at a door is
    /// ten seconds of standing in the open, and that exposure is the point of the objective.
    /// </summary>
    public sealed class EscapeDoor : MonoBehaviour, IInteractable
    {
        private static Material _frameMaterial, _lockedMaterial, _openMaterial;

        private Transform _panel;
        private MeshRenderer[] _panelRenderers;
        private float _openAmount;
        private float _panelHeight = 2.3f;

        public bool IsOpen { get; private set; }

        public Vector3 Position => transform.position;

        public float UseRadius => MatchManager.Instance != null
            ? MatchManager.Instance.Config.doorUseRadius
            : 2.2f;

        public static EscapeDoor Spawn(Vector3 groundPosition, Quaternion rotation, Transform parent)
        {
            var go = new GameObject("Escape Door");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(groundPosition, rotation);

            var door = go.AddComponent<EscapeDoor>();
            door.Build();
            return door;
        }

        private void Build()
        {
            EnsureMaterials();
            SetLayerRecursive(gameObject, MatchLayers.RunnerVision);

            // Frame: two jambs and a lintel, so the door reads as a doorway rather than as a slab
            // leaning on a wall.
            AddBlock(transform, "Jamb L", new Vector3(-0.75f, 1.15f, 0f), new Vector3(0.16f, 2.4f, 0.22f), _frameMaterial);
            AddBlock(transform, "Jamb R", new Vector3(0.75f, 1.15f, 0f), new Vector3(0.16f, 2.4f, 0.22f), _frameMaterial);
            AddBlock(transform, "Lintel", new Vector3(0f, 2.4f, 0f), new Vector3(1.66f, 0.18f, 0.22f), _frameMaterial);

            var panel = new GameObject("Panel");
            panel.transform.SetParent(transform, false);
            panel.transform.localPosition = new Vector3(0f, 0f, 0f);
            _panel = panel.transform;

            AddBlock(_panel, "Slab", new Vector3(0f, _panelHeight * 0.5f, 0f),
                new Vector3(1.4f, _panelHeight, 0.1f), _lockedMaterial);

            _panelRenderers = GetComponentsInChildren<MeshRenderer>();
            SetLayerRecursive(gameObject, MatchLayers.RunnerVision);
        }

        private void Update()
        {
            // The panel sinks into the floor as the keys go in, so progress is legible from across
            // the room without reading the HUD.
            MatchManager match = MatchManager.Instance;
            float target = IsOpen ? 1f
                : match != null && match.Config.keysRequired > 0
                    ? Mathf.Clamp01(match.KeysInserted / (float)match.Config.keysRequired) * 0.55f
                    : 0f;

            _openAmount = Mathf.MoveTowards(_openAmount, target, Time.deltaTime * (IsOpen ? 0.9f : 0.5f));
            if (_panel != null)
                _panel.localPosition = new Vector3(0f, -_panelHeight * _openAmount, 0f);
        }

        public string Prompt(PlayerAgent viewer)
        {
            // A Seeker standing on top of the door must be told nothing whatsoever.
            if (viewer == null || viewer.Role != Role.Runner) return null;

            MatchManager match = MatchManager.Instance;
            if (match == null) return null;

            if (IsOpen) return "ESCAPE  —  step through";
            if (viewer.CarriedKeys <= 0)
                return $"DOOR  {match.KeysInserted}/{match.Config.keysRequired}  —  no keys in hand";

            return $"[E]  INSERT KEY   {match.KeysInserted}/{match.Config.keysRequired}";
        }

        public void Interact(PlayerAgent user)
        {
            MatchManager.Instance?.TryInsertKey(user, this);
        }

        /// <summary>Called by the manager when the tenth key goes in. Nothing else opens the door.</summary>
        internal void Open()
        {
            if (IsOpen) return;
            IsOpen = true;

            if (_panelRenderers == null) return;
            foreach (MeshRenderer renderer in _panelRenderers)
                if (renderer != null && renderer.gameObject.name == "Slab")
                    renderer.sharedMaterial = _openMaterial;
        }

        private static void AddBlock(Transform parent, string name, Vector3 offset, Vector3 size, Material material)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(block.GetComponent<Collider>());   // nothing here blocks a body or a bullet
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = offset;
            block.transform.localScale = size;
            block.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
        }

        private static void EnsureMaterials()
        {
            if (_frameMaterial != null) return;

            _frameMaterial = Make("Door Frame", new Color(0.32f, 0.29f, 0.24f), 0f);
            _lockedMaterial = Make("Door Locked", new Color(0.55f, 0.16f, 0.14f), 1.1f);
            _openMaterial = Make("Door Open", new Color(0.45f, 0.95f, 0.55f), 2.4f);
        }

        private static Material Make(string name, Color colour, float emission)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name, color = colour };
            if (emission > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", colour * emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            material.enableInstancing = true;
            return material;
        }
    }
}
