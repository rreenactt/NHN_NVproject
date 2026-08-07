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
    /// The marks are particles in one ParticleSystem per trail, not GameObjects. A bleeding
    /// Runner lays ~3.6 marks a second for 25 s each, so a GameObject per mark was ~90 objects,
    /// ~90 one-off materials and ~90 draw calls per Runner at steady state — and the materials
    /// outlived their marks, since destroying a GameObject does not destroy a runtime Material.
    /// One system per trail keeps Stop() meaning "this Runner's marks only" (<c>Clear()</c>),
    /// per-mark lifetimes ride <c>EmitParams.startLifetime</c>, and the flat-then-last-quarter
    /// fade is the Color over Lifetime curve, which the system normalises per particle — so the
    /// 25 s running mark and the 62 s pool fade on their own clocks with no per-frame code here.
    ///
    /// Added and driven by <see cref="PlayerAgent"/>; nothing else should touch it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BloodTrail : MonoBehaviour
    {
        /// <summary>
        /// Hard cap per trail. Steady state is ~90 marks moving and ~90 pooling, so this is
        /// headroom, not a number play should ever reach; past it Emit drops the new mark.
        /// </summary>
        private const int MaxMarks = 256;

        private static Material _sharedMaterial;
        private static UnityEngine.Mesh _quad;

        private PlayerAgent _agent;
        private GameConfig _config;
        private bool _running;
        private int _markLayer = MatchLayers.SeekerVision;

        private ParticleSystem _system;

        private Vector3 _lastMark;
        private float _stillTime;
        private float _poolTimer;

        public void Begin(PlayerAgent agent)
        {
            _agent = agent;
            _config = MatchManager.Instance != null ? MatchManager.Instance.Config : null;

            // Resolved once, here, rather than per mark: the body this trail belongs to does not
            // change, and a mark that picked its own layer could disagree with the one before it.
            _markLayer = MatchLayers.BloodLayer(agent != null && agent.isLocalPlayer);

            EnsureSystem();

            _running = true;
            _lastMark = transform.position;
            _stillTime = 0f;
            _poolTimer = 0f;
        }

        /// <summary>Stops bleeding and clears what has already been laid — the Stop Bleeding device.</summary>
        public void Stop()
        {
            _running = false;
            if (_system != null) _system.Clear();
        }

        private void Update()
        {
            if (!_running || _config == null || _agent == null || !_agent.InPlay) return;

            // The system is a UnityEngine.Object field, so it survives a domain reload; this
            // guard is for its child GameObject being destroyed out from under us.
            if (_system == null) EnsureSystem();

            float dt = Time.deltaTime;
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

        private void Drop(Vector3 position, float lifetime, float size, float strength)
        {
            // A mark on the floor, not at the hip: cast down so it lands on stairs and on the
            // upper storey rather than hanging in the air above the floor below.
            Vector3 origin = position + Vector3.up * 0.6f;
            float y = position.y + 0.02f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 3f,
                                ~0, QueryTriggerInteraction.Ignore))
                y = hit.point.y + 0.02f;

            var emit = new ParticleSystem.EmitParams
            {
                position = new Vector3(position.x, y, position.z),
                // The quad is authored flat, so only yaw is needed — one axis, immune to
                // whatever order the particle system applies Euler angles in.
                rotation3D = new Vector3(0f, Random.value * 360f, 0f),
                startSize = size,
                startLifetime = lifetime,
                startColor = new Color(0.42f * strength, 0.03f, 0.04f, 0.85f),
                velocity = Vector3.zero,
            };
            _system.Emit(emit, 1);
        }

        private void EnsureSystem()
        {
            if (_system != null)
            {
                _system.gameObject.layer = _markLayer;
                return;
            }

            var go = new GameObject("Blood Marks") { layer = _markLayer };
            go.transform.SetParent(transform, false);

            _system = go.AddComponent<ParticleSystem>();
            _system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = _system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.startRotation3D = true;
            main.maxParticles = MaxMarks;
            // World space: the marks stay where they were bled, however the body moves on.
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // Emission is manual (Drop → Emit); the default module would spray from birth.
            ParticleSystem.EmissionModule emission = _system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = _system.shape;
            shape.enabled = false;

            // Flat for most of its life, then goes in the last quarter. A mark that starts
            // fading immediately reads as a rendering glitch rather than as evidence. The curve
            // is normalised per particle, so running marks and pools fade on their own clocks.
            ParticleSystem.ColorOverLifetimeModule fade = _system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f) },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.75f),
                    new GradientAlphaKey(0f, 1f),
                });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = QuadMesh();
            renderer.sharedMaterial = SharedMaterial();
            renderer.alignment = ParticleSystemRenderSpace.World;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _system.Play();
        }

        /// <summary>A 1×1 quad lying flat (XZ plane, facing up) — built here rather than taken
        /// from the Quad primitive, which stands upright and would need a second rotation axis.</summary>
        private static UnityEngine.Mesh QuadMesh()
        {
            if (_quad != null) return _quad;
            _quad = new UnityEngine.Mesh { name = "Blood Mark Quad" };
            _quad.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, -0.5f),
            };
            _quad.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            return _quad;
        }

        private static Material SharedMaterial()
        {
            if (_sharedMaterial != null) return _sharedMaterial;

            // NV/Blood Mark lives under Resources/ so a build cannot strip it: the particle's
            // colour and fade arrive as vertex colour, which the stock URP Unlit ignores and
            // the URP particle shaders — referenced by no material here — would not survive
            // a build's shader stripping to read.
            Shader shader = Shader.Find("NV/Blood Mark");
            _sharedMaterial = new Material(shader) { name = "Blood Mark" };
            return _sharedMaterial;
        }
    }
}
