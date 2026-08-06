using System.Collections.Generic;
using UnityEngine;

namespace NV.Game
{
    /// <summary>
    /// The trail a bleeding Runner leaves. Marks are real world-space objects rather than
    /// anything screen-space, because players must be able to disagree about seeing the same spot
    /// of blood: the layer decides, and <see cref="MatchLayers.BloodLayer"/> picks it from whether
    /// this client is the one leaking. Your own blood is on the layer everyone renders; everybody
    /// else's is on <c>SeekerVision</c>, which the Seeker's camera renders and a Runner's culls.
    /// One layer choice, made per client, covers the whole rule — no per-player VFX and no second
    /// copy of the trail.
    ///
    /// It was `SeekerVision` for every mark once, and that hid a Runner's blood from the Runner
    /// bleeding it: the pool below is the entire cost of standing still, so the person paying it
    /// could not see the bill.
    ///
    /// The marks are also what enforces "keep moving". Running lays one mark every
    /// <see cref="GameConfig.bloodSpacing"/> metres, which is a thin dotted line. Standing still
    /// past the grace period pools blood on the spot instead — bigger marks, laid on a timer, and
    /// lasting several times longer. Hiding while wounded therefore paints a sign over the hiding
    /// place, which costs the Runner more than the running trail ever does.
    ///
    /// Added and driven by <see cref="PlayerAgent"/>; nothing else should touch it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BloodTrail : MonoBehaviour
    {
        private static Material _sharedMaterial;
        private static UnityEngine.Mesh _quad;

        private readonly List<Mark> _marks = new List<Mark>();
        private PlayerAgent _agent;
        private GameConfig _config;
        private bool _running;
        private int _markLayer = MatchLayers.SeekerVision;

        private Vector3 _lastMark;
        private float _stillTime;
        private float _poolTimer;

        private struct Mark
        {
            public Transform transform;
            public Material material;
            public float age;
            public float lifetime;
            public float size;
        }

        public void Begin(PlayerAgent agent)
        {
            _agent = agent;
            _config = MatchManager.Instance != null ? MatchManager.Instance.Config : null;

            // Resolved once, here, rather than per mark: the body this trail belongs to does not
            // change, and a mark that picked its own layer could disagree with the one before it.
            _markLayer = MatchLayers.BloodLayer(agent != null && agent.isLocalPlayer);

            _running = true;
            _lastMark = transform.position;
            _stillTime = 0f;
            _poolTimer = 0f;
        }

        /// <summary>Stops bleeding and clears what has already been laid — the Stop Bleeding device.</summary>
        public void Stop()
        {
            _running = false;
            for (int i = 0; i < _marks.Count; i++)
                if (_marks[i].transform != null) Destroy(_marks[i].transform.gameObject);
            _marks.Clear();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            FadeMarks(dt);

            if (!_running || _config == null || _agent == null || !_agent.InPlay) return;

            Vector3 here = transform.position;
            float moved = Vector3.Distance(here, _lastMark);

            if (moved >= _config.bloodSpacing)
            {
                Drop(here, _config.bloodLifetime, 0.28f, 0.75f);
                _lastMark = here;
                _stillTime = 0f;
                _poolTimer = 0f;
                return;
            }

            // Standing still. The grace period is what keeps a Runner from being punished for a
            // corner peek; past it, the pool grows and outlives the running trail by a wide margin.
            bool moving = _agent.controller != null
                ? _agent.controller.PlanarSpeed > 0.4f
                : moved > 0.02f;

            if (moving) { _stillTime = 0f; return; }

            _stillTime += dt;
            if (_stillTime < _config.bleedStillGrace) return;

            _poolTimer -= dt;
            if (_poolTimer > 0f) return;

            _poolTimer = _config.bleedPoolInterval;
            float grown = Mathf.Min(1.1f, 0.34f + (_stillTime - _config.bleedStillGrace) * 0.06f);
            Drop(here, _config.bloodLifetime * _config.bleedPoolLifetimeScale, grown, 1f);
        }

        private void FadeMarks(float dt)
        {
            for (int i = _marks.Count - 1; i >= 0; i--)
            {
                Mark mark = _marks[i];
                if (mark.transform == null) { _marks.RemoveAt(i); continue; }

                mark.age += dt;
                if (mark.age >= mark.lifetime)
                {
                    Destroy(mark.transform.gameObject);
                    _marks.RemoveAt(i);
                    continue;
                }

                // Flat for most of its life, then goes in the last quarter. A mark that starts
                // fading immediately reads as a rendering glitch rather than as evidence.
                float remaining = 1f - mark.age / mark.lifetime;
                float alpha = Mathf.Clamp01(remaining * 4f);
                Color c = mark.material.color;
                c.a = alpha * 0.85f;
                mark.material.color = c;
                if (mark.material.HasProperty("_BaseColor")) mark.material.SetColor("_BaseColor", c);

                _marks[i] = mark;
            }
        }

        private void Drop(Vector3 position, float lifetime, float size, float strength)
        {
            // A mark on the floor, not at the hip: cast down so it lands on stairs and on the
            // upper storey rather than hanging in the air above the floor below.
            Vector3 origin = position + Vector3.up * 0.6f;
            float y = position.y + 0.02f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 3f,
                                ~0, QueryTriggerInteraction.Ignore))
                y = hit.point.y + 0.02f;

            var go = new GameObject("Blood");
            go.layer = _markLayer;
            go.transform.position = new Vector3(position.x, y, position.z);
            go.transform.rotation = Quaternion.Euler(90f, Random.value * 360f, 0f);
            go.transform.localScale = Vector3.one * size;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = QuadMesh();

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // One material per mark: they fade independently, and sharing would fade the lot at
            // the rate of whichever was laid last.
            var material = new Material(SharedMaterial());
            var colour = new Color(0.42f * strength, 0.03f, 0.04f, 0.85f);
            material.color = colour;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
            renderer.material = material;

            _marks.Add(new Mark
            {
                transform = go.transform,
                material = material,
                age = 0f,
                lifetime = lifetime,
                size = size,
            });
        }

        private static UnityEngine.Mesh QuadMesh()
        {
            if (_quad != null) return _quad;
            var temp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quad = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);
            return _quad;
        }

        private static Material SharedMaterial()
        {
            if (_sharedMaterial != null) return _sharedMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            _sharedMaterial = new Material(shader) { name = "Blood Mark" };

            // Transparent, and it must not write depth or the marks z-fight with the carpet.
            _sharedMaterial.SetFloat("_Surface", 1f);
            _sharedMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _sharedMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _sharedMaterial.SetInt("_ZWrite", 0);
            _sharedMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _sharedMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 10;
            return _sharedMaterial;
        }
    }
}
