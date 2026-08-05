using UnityEngine;

namespace NV.Lobby
{
    /// <summary>
    /// The waiting room itself, built in code from the level's own palette so the lobby and the
    /// maze read as the same building. It is not a menu background — it is a room you are standing
    /// in, one door away from the level, and slightly too quiet.
    ///
    /// The camera is **square-on to the row**. This is a line you pick a character out of, so every
    /// figure has to be the same size and the same distance away; an earlier pass angled it off to
    /// one side for the look of it and simply made the far end small and hard to read.
    /// </summary>
    public sealed class LobbyRoom : MonoBehaviour
    {
        // The map's palette, from BackroomsMapGenerator's aesthetic values.
        private static readonly Color WallColour = new Color32(0xC9, 0xB3, 0x6B, 0xFF);
        private static readonly Color TrimColour = new Color32(0xA8, 0x92, 0x4E, 0xFF);
        private static readonly Color CarpetColour = new Color32(0x8A, 0x7F, 0x52, 0xFF);
        private static readonly Color CeilingColour = new Color32(0xD8, 0xCF, 0xA8, 0xFF);
        private static readonly Color LightColour = new Color32(0xFF, 0xF6, 0xD6, 0xFF);
        // The level's fog colour taken well down. Same hue, a quarter of the value — the room reads
        // as the same building after dark rather than as a different palette.
        private static readonly Color FogColour = new Color32(0x3E, 0x39, 0x2A, 0xFF);

        private const float Height = 3.0f;
        private const float Depth = 13.0f;

        private Material _wall, _carpet, _ceiling, _panel;

        public Camera Camera { get; private set; }

        public static LobbyRoom Build(Transform parent, float rowWidth)
        {
            var go = new GameObject("Lobby Room");
            go.transform.SetParent(parent, false);

            var room = go.AddComponent<LobbyRoom>();
            room.Construct(Mathf.Max(8f, rowWidth + 5f));
            return room;
        }

        private void Construct(float width)
        {
            EnsureMaterials();

            // Shell. The row stands against the far wall (+Z) and the camera sits at -Z.
            Box("Carpet", new Vector3(0f, -0.1f, 0f), new Vector3(width, 0.2f, Depth), _carpet);
            Box("Ceiling", new Vector3(0f, Height, 0f), new Vector3(width, 0.2f, Depth), _ceiling);
            Box("Wall Back", new Vector3(0f, Height * 0.5f, Depth * 0.5f), new Vector3(width, Height, 0.25f), _wall);
            Box("Wall Front", new Vector3(0f, Height * 0.5f, -Depth * 0.5f), new Vector3(width, Height, 0.25f), _wall);
            Box("Wall Left", new Vector3(-width * 0.5f, Height * 0.5f, 0f), new Vector3(0.25f, Height, Depth), _wall);
            Box("Wall Right", new Vector3(width * 0.5f, Height * 0.5f, 0f), new Vector3(0.25f, Height, Depth), _wall);

            // Skirting, because a flat wall meeting a flat floor is the one thing that reads as
            // "untextured box" rather than "room".
            Box("Skirting Back", new Vector3(0f, 0.08f, Depth * 0.5f - 0.16f), new Vector3(width, 0.16f, 0.06f), MakeMaterial("Trim", TrimColour, 0.1f));

            BuildLights(width);
            BuildCamera(width);
            ApplyAtmosphere();
        }

        /// <summary>
        /// The tubes. Half of this room's lighting is on its way out, and that is the mood: a
        /// working fluorescent grid reads as an office, a failing one reads as somewhere you are
        /// waiting to be let out of.
        ///
        /// Each tube owns **its own panel material** so the panel blinks with its own light. Sharing
        /// one material — which is what the first pass did — left every panel evenly lit while the
        /// cast light stuttered, and the eye reads that as a rendering fault rather than a bad tube.
        /// </summary>
        private void BuildLights(float width)
        {
            int count = Mathf.Max(3, Mathf.RoundToInt(width / 3.0f));

            for (int i = 0; i < count; i++)
            {
                float x = Mathf.Lerp(-width * 0.5f + 1.6f, width * 0.5f - 1.6f,
                                     count == 1 ? 0.5f : i / (float)(count - 1));

                // One tube is gone entirely, one is nearly gone, one stutters; the rest just buzz.
                FlickerStyle style =
                    i == 0 ? FlickerStyle.Dead :
                    i == count - 1 ? FlickerStyle.Dying :
                    i == count / 2 ? FlickerStyle.Stutter : FlickerStyle.Buzz;

                var panelMaterial = new Material(_panel) { name = "Lobby Panel " + i };

                var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                panel.name = "Light Panel " + i;
                Destroy(panel.GetComponent<Collider>());
                panel.transform.SetParent(transform, false);
                panel.transform.localPosition = new Vector3(x, Height - 0.12f, 0.4f);
                panel.transform.localScale = new Vector3(1.5f, 0.08f, 1.0f);
                panel.GetComponent<MeshRenderer>().sharedMaterial = panelMaterial;

                var lightGo = new GameObject("Light " + i);
                lightGo.transform.SetParent(transform, false);
                lightGo.transform.localPosition = new Vector3(x, Height - 0.35f, 0.4f);

                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = LightColour;
                light.range = 8f;

                // Dim. This used to be lit like a shop floor; now it is one tired tube per bay and
                // the corners are left to the fog.
                light.intensity = 0.85f;
                light.shadows = LightShadows.None;

                lightGo.AddComponent<LobbyFlicker>()
                       .Configure(light, panelMaterial, LightColour, style, i * 1.7f);
            }
        }

        /// <summary>
        /// The lobby camera. Dead centre, level, far enough back that the whole row fits:
        /// <list type="bullet">
        /// <item><b>on the centre line, no yaw</b> — every figure is the same distance away and the
        /// same size, which is the whole point of a lineup you pick from;</item>
        /// <item><b>1.30 m, level</b> — just under eye height, so the figures are met rather than
        /// looked down on;</item>
        /// <item><b>40° lens at ~6 m</b> — the row is about 7 m wide with six stands, and this is
        /// what makes it fit with room to spare. Widening the lens instead would bow the figures at
        /// the ends outward.</item>
        /// </list>
        /// </summary>
        private void BuildCamera(float width)
        {
            var go = new GameObject("Lobby Camera");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.30f, -Depth * 0.5f + 1.5f);
            go.transform.localRotation = Quaternion.identity;

            Camera = go.AddComponent<Camera>();
            Camera.fieldOfView = 40f;
            Camera.nearClipPlane = 0.05f;
            Camera.farClipPlane = 60f;

            go.AddComponent<AudioListener>();
        }

        private void ApplyAtmosphere()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = FogColour;

            // Thicker than before, so the ends of the row fall away and the dead tube's bay reads as
            // genuinely unlit rather than merely dimmer.
            RenderSettings.fogDensity = 0.055f;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

            // Most of the darkness lives on this line. Ambient light is what stops a room having
            // shadows at all; dropping it to a quarter is what hands the lighting back to the tubes
            // — and to the gaps between them.
            RenderSettings.ambientLight = new Color(0.085f, 0.080f, 0.062f);
            RenderSettings.skybox = null;

            BuildHum();
        }

        /// <summary>
        /// The room tone. Same trick as the level's hum — one low drone, generated, looped — and it
        /// is doing most of the work of "slightly too quiet".
        /// </summary>
        private void BuildHum()
        {
            const int sampleRate = 44100;
            const int seconds = 4;
            int count = sampleRate * seconds;
            var samples = new float[count];

            var random = new System.Random(4242);
            float smoothed = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)sampleRate;
                float mains = Mathf.Sin(2f * Mathf.PI * 60f * t) * 0.5f;
                float harmonic = Mathf.Sin(2f * Mathf.PI * 121.5f * t) * 0.2f;

                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                smoothed = Mathf.Lerp(smoothed, white, 0.02f);

                samples[i] = (mains + harmonic + smoothed * 0.55f) * 0.18f;
            }

            int fade = sampleRate / 20;
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                samples[i] = Mathf.Lerp(samples[count - fade + i], samples[i], k);
            }

            var clip = AudioClip.Create("Lobby Hum", count, 1, sampleRate, false);
            clip.SetData(samples, 0);

            var go = new GameObject("Room Tone");
            go.transform.SetParent(transform, false);

            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = 0.3f;
            source.Play();
        }

        private void Box(string name, Vector3 position, Vector3 size, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(transform, false);
            box.transform.localPosition = position;
            box.transform.localScale = size;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;

            // Nothing walks around in here; the colliders would only get in the way of slot picking.
            Destroy(box.GetComponent<Collider>());
        }

        private void EnsureMaterials()
        {
            _wall = MakeMaterial("Lobby Wall", WallColour, 0.12f);
            _carpet = MakeMaterial("Lobby Carpet", CarpetColour, 0.04f);
            _ceiling = MakeMaterial("Lobby Ceiling", CeilingColour, 0.08f);

            _panel = MakeMaterial("Lobby Panel", LightColour, 0.2f);
            _panel.EnableKeyword("_EMISSION");
            _panel.SetColor("_EmissionColor", LightColour * 2.4f);
            _panel.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        private static Material MakeMaterial(string name, Color colour, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name, color = colour };
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            return material;
        }
    }

    /// <summary>How badly one tube is failing.</summary>
    public enum FlickerStyle
    {
        /// <summary>Working, more or less — a faint mains buzz in the brightness.</summary>
        Buzz = 0,

        /// <summary>Mostly on, with occasional hard dropouts.</summary>
        Stutter = 1,

        /// <summary>Struggling: long dark spells broken by bursts of strobing.</summary>
        Dying = 2,

        /// <summary>Gone. Dark panel, no light at all; that bay is lit by its neighbours or not at all.</summary>
        Dead = 3,
    }

    /// <summary>
    /// One failing fluorescent tube. The cast light and the panel's own glow move together, because
    /// a panel that stays lit while its light stutters reads as a bug rather than as a bad tube.
    /// </summary>
    public sealed class LobbyFlicker : MonoBehaviour
    {
        private Light _light;
        private Material _panel;
        private Color _colour;
        private FlickerStyle _style;
        private float _phase;
        private float _baseIntensity;

        private float _stateTimer;
        private bool _blackout;

        public void Configure(Light light, Material panel, Color colour, FlickerStyle style, float phase)
        {
            _light = light;
            _panel = panel;
            _colour = colour;
            _style = style;
            _phase = phase;
            _baseIntensity = light != null ? light.intensity : 1f;

            if (_style != FlickerStyle.Dead) return;

            // A dead tube is not a dim one. No light, and a cold grey panel with no emission at all.
            if (_light != null) _light.enabled = false;
            if (_panel == null) return;

            _panel.SetColor("_EmissionColor", Color.black);
            _panel.color = new Color(0.17f, 0.16f, 0.14f);
        }

        private void Update()
        {
            if (_style == FlickerStyle.Dead || _light == null) return;

            float t = Time.time + _phase;
            float level;

            switch (_style)
            {
                case FlickerStyle.Stutter:
                {
                    // Steady, then a short hard drop. Two prime-ish rates so the gaps never settle
                    // into a rhythm the eye can predict.
                    float buzz = 0.88f + 0.12f * Mathf.Sin(t * 34f) * Mathf.Sin(t * 3.1f);
                    bool drop = Mathf.Sin(t * 0.37f) > 0.972f || Mathf.Sin(t * 1.13f + 2f) > 0.995f;
                    level = drop ? 0.05f : buzz;
                    break;
                }

                case FlickerStyle.Dying:
                {
                    // Dark spells alternating with bursts of strobing. The dark spells are the whole
                    // point: a tube that flickers non-stop becomes wallpaper in ten seconds, one
                    // that goes out for four makes you look when it comes back.
                    _stateTimer -= Time.deltaTime;
                    if (_stateTimer <= 0f)
                    {
                        _blackout = !_blackout;
                        _stateTimer = _blackout ? Random.Range(1.8f, 4.5f) : Random.Range(0.8f, 2.6f);
                    }

                    level = _blackout
                        ? (Random.value > 0.985f ? 0.5f : 0.02f)           // the odd twitch while out
                        : 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(t * 21f));   // strobing while alive
                    break;
                }

                default:
                    // Even a working tube is not steady. Small enough to be felt rather than watched.
                    level = 0.9f + 0.1f * Mathf.Sin(t * 27f) * Mathf.Sin(t * 0.9f);
                    break;
            }

            _light.intensity = _baseIntensity * level;
            if (_panel != null) _panel.SetColor("_EmissionColor", _colour * level * 2.2f);
        }
    }
}
