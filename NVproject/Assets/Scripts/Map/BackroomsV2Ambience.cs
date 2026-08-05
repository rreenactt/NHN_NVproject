using UnityEngine;

namespace NV.Client.Map
{
    /// <summary>
    /// What Backrooms V2 feels like — everything a prefab cannot carry.
    ///
    /// Three categories only: scene-global <see cref="RenderSettings"/> (fog, flat ambient, no
    /// skybox), the point lights built from <see cref="MapBakedAsset.Lights"/> (a
    /// <see cref="Light"/> costs draw work, not triangles, so it is a runtime object, not a baked
    /// box), and a synthesised ventilation rumble. The geometry, the emissive panels and the
    /// palette are the prefab's business.
    ///
    /// Written fresh for V2 — a cold, flooded-utility-floor mood, deliberately not the original's
    /// mono-yellow. Shares no code or numbers with the original's ambience, by requirement
    /// (<c>NVserver/docs/backrooms-v2-plan.md</c> §1).
    /// </summary>
    [RequireComponent(typeof(BakedMapSource))]
    public sealed class BackroomsV2Ambience : MonoBehaviour
    {
        [Header("Fog — the far end of a hall should be a guess, not a fact")]
        [Tooltip("Cold grey-green. The fog does most of the dread.")]
        public Color fogColor = new Color(0.41f, 0.47f, 0.45f);

        [Tooltip("Exponential-squared density. Sets how far a Seeker can see down a hall.")]
        public float fogDensity = 0.03f;

        [Tooltip("Flat ambient — dim and cool, so the light pools around the strips.")]
        public Color ambientColor = new Color(0.12f, 0.15f, 0.14f);

        [Header("Lights — built from the baked asset's lamp positions")]
        [Tooltip("Copied from the level palette at bake time by the generator's decorator.")]
        public Color lightColor = new Color(0.82f, 1.00f, 0.94f);

        public float lightIntensity = 1.5f;

        [Tooltip("Range in grid cells; multiplied by the grid's cell size at build time.")]
        public float lightRangeCells = 2.2f;

        [Tooltip("Fraction of lamps that flicker. Drawn from flickerSeed — its own random, " +
                 "deliberately nowhere near the level seed: presentation must not be able to " +
                 "shift the terrain.")]
        [Range(0f, 1f)]
        public float flickerFraction = 0.12f;

        [Tooltip("Seed for which lamps flicker and how. Isolated from the level seed on purpose.")]
        public int flickerSeed = 60301;

        [Header("Sound")]
        [Tooltip("Volume of the ventilation rumble.")]
        [Range(0f, 1f)]
        public float rumbleVolume = 0.35f;

        private BakedMapSource _source;
        private Transform _lightRoot;
        private AudioSource _rumble;

        private void Start()
        {
            Apply();
        }

        /// <summary>
        /// A domain reload mid-play wipes the built objects' backing lists but not the objects —
        /// and can also wipe the objects while this component survives. Rebuild when the root has
        /// gone missing rather than trusting a flag; a bool survives the reload and lies.
        /// </summary>
        private void Update()
        {
            if (_lightRoot == null) Apply();
        }

        private void Apply()
        {
            _source = GetComponent<BakedMapSource>();

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambientColor;
            RenderSettings.skybox = null;

            BuildLights();
            BuildRumble();
        }

        private void BuildLights()
        {
            var existing = transform.Find("__Lights V2");
            if (existing != null) Destroy(existing.gameObject);

            _lightRoot = new GameObject("__Lights V2").transform;
            _lightRoot.SetParent(transform, false);

            var asset = _source.asset;
            if (asset == null) return;

            var cellSize = LevelCellSize();
            var flicker = new System.Random(flickerSeed);

            for (var index = 0; index < asset.Lights.Count; index++)
            {
                var lamp = new GameObject($"Lamp {index}");
                lamp.transform.SetParent(_lightRoot, false);
                lamp.transform.position = asset.Lights[index];

                var light = lamp.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = lightColor;
                light.intensity = lightIntensity;
                light.range = lightRangeCells * cellSize;
                light.shadows = LightShadows.None;   // flat, diffuse utility light — and cheap

                // Every lamp draws, chosen or not, so the flicker set is stable per seed.
                var flickers = flicker.NextDouble() < flickerFraction;
                if (flickers)
                {
                    var driver = lamp.AddComponent<LampFlicker>();
                    driver.baseIntensity = lightIntensity;
                    driver.phase = (float)(flicker.NextDouble() * 10.0);
                }
            }
        }

        private float LevelCellSize()
        {
            // ILevelQuery has no cell-size accessor; the grid span over the grid size recovers it
            // without touching a Shared type from a context that may not reference the assembly.
            var grid = _source.BuildGrid();
            return grid == null ? 2.5f : grid.CellSize;
        }

        /// <summary>
        /// A low ventilation rumble, synthesised — low-passed noise swelling slowly, nothing
        /// tonal. The original's hum is a chord of mains sines; this is air in a duct, which is
        /// both the different mood and the different implementation.
        /// </summary>
        private void BuildRumble()
        {
            if (_rumble != null) return;

            const int sampleRate = 44100;
            const float seconds = 5f;
            var samples = (int)(sampleRate * seconds);
            var data = new float[samples];

            // Deterministic noise — presentation randomness stays off the level seed.
            var noise = new System.Random(flickerSeed ^ 0x5EA5);

            // One-pole low-pass over white noise, twice, leaves a deep rumble; the slow sweep of
            // the swell keeps it from reading as a constant test tone.
            var lp1 = 0f;
            var lp2 = 0f;

            for (var index = 0; index < samples; index++)
            {
                var white = (float)(noise.NextDouble() * 2.0 - 1.0);
                lp1 += 0.02f * (white - lp1);
                lp2 += 0.05f * (lp1 - lp2);

                var swell = 0.75f + 0.25f * Mathf.Sin(index * (2f * Mathf.PI * 0.13f / sampleRate));
                data[index] = lp2 * swell * 6f;
            }

            // Cross-fade the seam so the loop has no click.
            var fade = sampleRate / 10;
            for (var index = 0; index < fade; index++)
            {
                var t = index / (float)fade;
                data[index] = data[index] * t + data[samples - fade + index] * (1f - t);
            }

            var clip = AudioClip.Create("V2 Vent Rumble", samples - fade, 1, sampleRate, false);
            clip.SetData(data, 0);

            _rumble = gameObject.AddComponent<AudioSource>();
            _rumble.clip = clip;
            _rumble.loop = true;
            _rumble.volume = rumbleVolume;
            _rumble.spatialBlend = 0f;   // the building's sound, not a point's
            _rumble.Play();
        }
    }

    /// <summary>
    /// One failing tube. Perlin-driven so it stutters rather than strobes; a sine flicker reads
    /// as an alarm.
    /// </summary>
    public sealed class LampFlicker : MonoBehaviour
    {
        public float baseIntensity = 1.5f;

        public float phase;

        private Light _light;

        private void Awake()
        {
            _light = GetComponent<Light>();
        }

        private void Update()
        {
            if (_light == null) return;

            var noise = Mathf.PerlinNoise(phase, Time.time * 7f);
            _light.intensity = noise < 0.35f ? baseIntensity * 0.15f : baseIntensity;
        }
    }
}
