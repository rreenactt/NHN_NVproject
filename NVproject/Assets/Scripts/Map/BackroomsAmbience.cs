using System.Collections.Generic;
using UnityEngine;

namespace NV.Client.Map
{
    /// <summary>
    /// Everything about the Backrooms that could not be baked.
    ///
    /// Three kinds of thing end up here, and they are here for three different reasons:
    ///
    /// - **Scene-global state.** Fog and ambient light live on <see cref="RenderSettings"/>, which
    ///   belongs to the scene and not to any object in it, so a prefab cannot carry them.
    /// - **Things generated in code.** The hum is a four-second <see cref="AudioClip"/> built from
    ///   sine waves because this project ships no audio assets.
    /// - **Things that are not geometry.** A <see cref="Light"/> costs draw work rather than
    ///   triangles, and which lamps flicker is drawn from its own random — which must stay well
    ///   away from the level seed, or changing the flicker would reshape the walls.
    ///
    /// The lamp *positions* are baked; this only decides what to put there. The emissive panels are
    /// baked geometry and are not this component's business.
    ///
    /// The fog does most of the dread — not being able to see how far the room goes.
    /// </summary>
    [RequireComponent(typeof(BakedMapSource))]
    public sealed class BackroomsAmbience : MonoBehaviour
    {
        [Header("Lights")]
        public Color lightColor = new Color32(0xFF, 0xF6, 0xD6, 0xFF);

        public float lightIntensity = 1.6f;

        [Tooltip("Range as a multiple of the cell size.")]
        public float lightRangeCells = 2.5f;

        [Tooltip("Fraction of lights that buzz and flicker. Occasional is eerier than constant.")]
        [Range(0f, 0.4f)] public float flickerFraction = 0.18f;

        [Tooltip("Flicker draw. Separate from the level seed on purpose — this must never be able " +
                 "to move a wall.")]
        public int flickerSeed = 12345;

        [Header("Atmosphere")]
        public Color fogColor = new Color32(0xB7, 0xAC, 0x7E, 0xFF);

        public float fogDensity = 0.022f;

        public Color ambientLight = new Color(0.19f, 0.17f, 0.12f);

        [Header("Sound")]
        [Tooltip("Low fluorescent/HVAC drone, generated in code. This one sound does a lot of work.")]
        public bool ambientHum = true;

        [Range(0f, 1f)] public float humVolume = 0.35f;

        private const string LightRootName = "__Lights";

        /// <summary>
        /// The flickering lamps and their phases.
        ///
        /// Plain lists, and a script edit during play wipes them while the <see cref="Light"/>s
        /// themselves survive — so <see cref="Update"/> rebuilds rather than throwing once a frame
        /// for the rest of the session.
        /// </summary>
        private readonly List<Light> _flicker = new List<Light>();

        private readonly List<float> _phase = new List<float>();

        private bool _built;

        private void Awake()
        {
            ApplyAtmosphere();
            BuildLights();

            if (ambientHum) BuildHum();
        }

        private void ApplyAtmosphere()
        {
            RenderSettings.fog = true;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = fogDensity;

            // No sun indoors, and no skybox to leak blue into a yellow room.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambientLight;
            RenderSettings.skybox = null;
        }

        private void BuildLights()
        {
            var source = GetComponent<BakedMapSource>();
            if (source == null || source.asset == null) return;

            var positions = source.asset.Lights;
            if (positions == null || positions.Count == 0) return;

            var existing = transform.Find(LightRootName);
            if (existing != null) Destroy(existing.gameObject);

            var root = new GameObject(LightRootName).transform;
            root.SetParent(transform, false);

            var random = new System.Random(flickerSeed);
            var range = Mathf.Max(0.1f, lightRangeCells * CellSize(source));

            _flicker.Clear();
            _phase.Clear();

            for (var index = 0; index < positions.Count; index++)
            {
                var go = new GameObject("Fluorescent");
                go.transform.SetParent(root, false);
                go.transform.localPosition = positions[index];

                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = lightColor;
                light.range = range;
                light.intensity = lightIntensity;

                // Shadows off: a hundred shadow-casting point lights is ruinous, and the reference
                // look is flat diffuse fluorescent light with barely a shadow in it anyway.
                light.shadows = LightShadows.None;

                if (random.NextDouble() >= flickerFraction) continue;

                _flicker.Add(light);
                _phase.Add((float)random.NextDouble() * 10f);
            }

            _built = true;
        }

        /// <summary>
        /// Cell size, for the lamp range. Read off the grid rather than stored again — two copies
        /// of a number that has to agree is one copy too many.
        /// </summary>
        private static float CellSize(BakedMapSource source)
        {
            var grid = source.asset.BuildGrid();
            return grid == null || grid.CellSize <= 0f ? 3f : grid.CellSize;
        }

        private void Update()
        {
            // A domain reload during play wipes these lists without re-running Awake, and the old
            // code's symptom for that shape of bug was a NullReference every frame.
            if (!_built || (_flicker.Count == 0 && transform.Find(LightRootName) == null))
            {
                BuildLights();
                return;
            }

            for (var index = 0; index < _flicker.Count; index++)
            {
                var light = _flicker[index];
                if (light == null) continue;

                var t = Time.time * 9f + _phase[index];
                var buzz = 0.84f + 0.16f * Mathf.Sin(t) * Mathf.Sin(t * 0.37f);
                if (Mathf.Sin(t * 0.11f) > 0.985f) buzz *= 0.2f;

                light.intensity = lightIntensity * buzz;
            }
        }

        /// <summary>
        /// A looping fluorescent/HVAC drone built in code, since this project ships no audio assets.
        /// Two low sines slightly detuned beat against each other, plus a little filtered noise for
        /// air. 2D, so it sits at a constant level wherever the player is.
        /// </summary>
        private void BuildHum()
        {
            const int sampleRate = 44100;
            const int seconds = 4;

            var sampleCount = sampleRate * seconds;
            var samples = new float[sampleCount];

            var noise = new System.Random(12345);
            var smoothed = 0f;

            for (var i = 0; i < sampleCount; i++)
            {
                var t = i / (float)sampleRate;
                var mains = Mathf.Sin(2f * Mathf.PI * 60f * t) * 0.5f;
                var harmonic = Mathf.Sin(2f * Mathf.PI * 121.5f * t) * 0.22f;
                var sub = Mathf.Sin(2f * Mathf.PI * 39f * t) * 0.18f;

                var white = (float)(noise.NextDouble() * 2.0 - 1.0);
                smoothed = Mathf.Lerp(smoothed, white, 0.02f);   // cheap low-pass = air, not hiss

                samples[i] = (mains + harmonic + sub + smoothed * 0.6f) * 0.22f;
            }

            // Cross-fade the seam so the loop does not click every four seconds.
            var fade = sampleRate / 20;
            for (var i = 0; i < fade; i++)
            {
                var k = i / (float)fade;
                samples[i] = Mathf.Lerp(samples[sampleCount - fade + i], samples[i], k);
            }

            var clip = AudioClip.Create("Backrooms Hum", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);

            var go = new GameObject("Ambient Hum");
            go.transform.SetParent(transform, false);

            var audio = go.AddComponent<AudioSource>();
            audio.clip = clip;
            audio.loop = true;
            audio.spatialBlend = 0f;
            audio.volume = humVolume;
            audio.playOnAwake = true;
            audio.Play();
        }
    }
}
