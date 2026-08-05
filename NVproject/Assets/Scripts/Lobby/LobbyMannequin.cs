using UnityEngine;

namespace NV.Client.Lobby
{
    /// <summary>
    /// The figure standing in a stand. Blocks, built in code, in the same proportions as the
    /// player's rig — there is no character model in this project and the lobby is not the place to
    /// introduce one.
    ///
    /// **The idle is the character.** An earlier pass gave every figure the same breathing sway,
    /// and six identical people politely rocking in unison read as broken rather than as calm. Each
    /// of the eight characters now has its own <see cref="IdleStyle"/> — one bounces, one checks a
    /// wrist, one barely moves and then snaps its head round — so the row reads as a group of
    /// people rather than a row of props.
    ///
    /// All of it is procedural. There is no AnimationClip anywhere in this project and adding one
    /// here would make this the only exception.
    /// </summary>
    public sealed class LobbyMannequin : MonoBehaviour
    {
        private static Shader _shader;

        private Transform _root, _head, _torso, _armL, _armR, _legL, _legR, _hat;
        private Material _suit, _trim, _accent;
        private LobbyCharacterCatalog.Character _character;

        private float _phase;
        private bool _ready;

        // Periodic gestures (the watch check, the stretch) run on their own clock so they do not
        // all fire on the same frame across the row.
        private float _gestureTimer;
        private float _gestureLength;
        private bool _gestureActive;

        public static LobbyMannequin Spawn(Transform parent, int seedIndex)
        {
            var go = new GameObject("Mannequin");
            go.transform.SetParent(parent, false);

            var mannequin = go.AddComponent<LobbyMannequin>();
            mannequin._phase = seedIndex * 2.3f;
            mannequin.Build();
            return mannequin;
        }

        private void Build()
        {
            _shader = _shader != null ? _shader
                : Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            _suit = NewMaterial("Lobby Suit");
            _trim = NewMaterial("Lobby Trim");
            _accent = NewMaterial("Lobby Accent");

            var root = new GameObject("Body");
            root.transform.SetParent(transform, false);
            _root = root.transform;

            // Pivots sit at the joint, not the centre of the block — the same rule the player's rig
            // follows, and the reason limbs rotate instead of orbiting their own middle.
            _torso = Block("Torso", _root, new Vector3(0f, 1.05f, 0f), new Vector3(0.45f, 0.68f, 0.23f), _suit);
            _head = Joint("Head", _root, new Vector3(0f, 1.42f, 0f));
            Block("Skull", _head, new Vector3(0f, 0.17f, 0f), new Vector3(0.34f, 0.34f, 0.34f), _accent);

            _armR = Joint("Arm R", _root, new Vector3(-0.33f, 1.36f, 0f));
            Block("Upper R", _armR, new Vector3(0f, -0.33f, 0f), new Vector3(0.2f, 0.66f, 0.2f), _suit);
            _armL = Joint("Arm L", _root, new Vector3(0.33f, 1.36f, 0f));
            Block("Upper L", _armL, new Vector3(0f, -0.33f, 0f), new Vector3(0.2f, 0.66f, 0.2f), _suit);

            _legR = Joint("Leg R", _root, new Vector3(-0.12f, 0.7f, 0f));
            Block("Shin R", _legR, new Vector3(0f, -0.35f, 0f), new Vector3(0.21f, 0.7f, 0.22f), _trim);
            _legL = Joint("Leg L", _root, new Vector3(0.12f, 0.7f, 0f));
            Block("Shin L", _legL, new Vector3(0f, -0.35f, 0f), new Vector3(0.21f, 0.7f, 0.22f), _trim);

            Block("Belt", _root, new Vector3(0f, 0.73f, 0f), new Vector3(0.47f, 0.1f, 0.25f), _trim);

            _hat = Joint("Head Gear", _head, Vector3.zero);
        }

        private Material NewMaterial(string name)
        {
            var material = new Material(_shader) { name = name, color = Color.grey };
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.05f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            return material;
        }

        private static Transform Joint(string name, Transform parent, Vector3 offset)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            return go.transform;
        }

        private static Transform Block(string name, Transform parent, Vector3 offset, Vector3 size, Material material)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            Destroy(block.GetComponent<Collider>());   // the stand owns the click target, not the body
            block.transform.SetParent(parent, false);
            block.transform.localPosition = offset;
            block.transform.localScale = size;
            block.GetComponent<MeshRenderer>().sharedMaterial = material;
            return block.transform;
        }

        /// <summary>
        /// Dresses the figure as one of the eight. Called on every roster change, so a character
        /// picked by *anyone* appears on their figure in the row rather than only for the person who
        /// picked it.
        /// </summary>
        public void ApplyCharacter(LobbyCharacterCatalog.Character character)
        {
            if (character == null) return;
            _character = character;

            _suit.color = character.suit;
            _trim.color = character.trim;
            _accent.color = character.accent;

            BuildHeadGear(character);
            ResetGesture();
        }

        /// <summary>The silhouette. At six metres this is what tells two figures apart, not the colour.</summary>
        private void BuildHeadGear(LobbyCharacterCatalog.Character character)
        {
            for (int i = _hat.childCount - 1; i >= 0; i--) Destroy(_hat.GetChild(i).gameObject);

            Color colour = character.head == HeadGear.HardHat
                ? new Color(0.95f, 0.72f, 0.15f)
                : character.suit;

            var material = new Material(_shader) { name = "Head Gear", color = colour };
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.08f);

            switch (character.head)
            {
                case HeadGear.Cap:
                    Block("Crown", _hat, new Vector3(0f, 0.36f, 0f), new Vector3(0.36f, 0.1f, 0.36f), material);
                    Block("Peak", _hat, new Vector3(0f, 0.32f, 0.22f), new Vector3(0.34f, 0.04f, 0.16f), material);
                    break;

                case HeadGear.HardHat:
                    Block("Shell", _hat, new Vector3(0f, 0.38f, 0f), new Vector3(0.40f, 0.16f, 0.40f), material);
                    Block("Brim", _hat, new Vector3(0f, 0.31f, 0f), new Vector3(0.46f, 0.04f, 0.46f), material);
                    break;

                case HeadGear.Band:
                    Block("Band", _hat, new Vector3(0f, 0.30f, 0f), new Vector3(0.36f, 0.07f, 0.36f), material);
                    break;

                case HeadGear.Hood:
                    Block("Hood", _hat, new Vector3(0f, 0.19f, -0.03f), new Vector3(0.44f, 0.44f, 0.42f), material);
                    Block("Shade", _hat, new Vector3(0f, 0.19f, 0.19f), new Vector3(0.30f, 0.22f, 0.06f),
                        NewMaterial("Hood Shade"));
                    break;

                case HeadGear.Visor:
                    Block("Visor", _hat, new Vector3(0f, 0.21f, 0.16f), new Vector3(0.36f, 0.1f, 0.08f), material);
                    Block("Strap", _hat, new Vector3(0f, 0.21f, -0.16f), new Vector3(0.30f, 0.06f, 0.06f), material);
                    break;
            }
        }

        public void SetReady(bool ready)
        {
            if (_ready == ready) return;
            _ready = ready;
            ResetGesture();
        }

        private void ResetGesture()
        {
            _gestureActive = false;
            _gestureTimer = 2f + Mathf.Abs(Mathf.Sin(_phase)) * 4f;
        }

        // ============================================================ the idles

        private void Update()
        {
            if (_root == null) return;

            float t = Time.time + _phase;
            float dt = Time.deltaTime;

            // Everyone breathes; the rest is personal.
            float breath = Mathf.Sin(t * 1.05f);

            // Readied up, everybody settles: the fidgeting drops away and they face front. It is
            // the clearest possible read on who is waiting for whom.
            float energy = _ready ? 0.3f : 1f;

            Vector3 rootPos = new Vector3(0f, breath * 0.01f, 0f);
            Vector3 rootRot = Vector3.zero;
            Vector3 headRot = new Vector3(breath * 1.5f * energy, 0f, 0f);
            float armR = 0f, armL = 0f, armSpread = 3f;
            float legR = 0f, legL = 0f;

            IdleStyle style = _character?.idle ?? IdleStyle.Rock;

            switch (style)
            {
                case IdleStyle.Rock:
                {
                    // Heel to toe, slow, hands loose. The whole body leans as one.
                    float rock = Mathf.Sin(t * 0.9f) * energy;
                    rootRot.x = rock * 2.4f;
                    rootPos.z = rock * 0.03f;
                    armR = rock * 7f;
                    armL = rock * 7f;
                    headRot.x += rock * 1.5f;
                    break;
                }

                case IdleStyle.Fidget:
                {
                    // Small, fast, never settled. Two frequencies that do not divide into each
                    // other, so it never quite repeats.
                    float a = Mathf.Sin(t * 3.1f), b = Mathf.Sin(t * 1.7f + 1.2f);
                    rootRot.y = (a * 2f + b * 3f) * energy;
                    rootPos.x = b * 0.02f * energy;
                    headRot.y = a * 9f * energy;
                    headRot.z = b * 2f * energy;
                    armR = a * 5f * energy;
                    armL = -b * 6f * energy;
                    armSpread = 4.5f;
                    break;
                }

                case IdleStyle.Nod:
                {
                    // Arms folded across the chest, slow agreeing nod. Folded arms are the pose;
                    // the nod is the tic.
                    float nod = Mathf.Sin(t * 1.35f);
                    headRot.x += nod * 5.5f * energy;
                    armR = -72f;
                    armL = -72f;
                    armSpread = 26f;
                    rootRot.y = Mathf.Sin(t * 0.4f) * 1.5f * energy;
                    break;
                }

                case IdleStyle.Scan:
                {
                    // Sweeps the room. Slow turn out, quick snap back — the timing is what makes it
                    // read as watching rather than as a metronome.
                    float sweep = Mathf.Sin(t * 0.55f);
                    float sharpened = Mathf.Sign(sweep) * Mathf.Pow(Mathf.Abs(sweep), 0.6f);
                    headRot.y = sharpened * 38f * energy;
                    rootRot.y = sharpened * 7f * energy;
                    armR = 2f;
                    armL = 2f;
                    break;
                }

                case IdleStyle.WatchCheck:
                {
                    // Every few seconds, up comes the wrist. Between times, mild weight shifting.
                    float shift = Mathf.Sin(t * 0.75f) * energy;
                    rootRot.z = shift * 1.2f;
                    armR = shift * 4f;

                    float g = Gesture(dt, 1.3f);
                    if (g > 0f)
                    {
                        float raise = Mathf.Sin(g * Mathf.PI);      // up and back down
                        armL = -raise * 95f;
                        headRot.x += raise * 16f;
                        headRot.y += raise * 12f;
                    }
                    else armL = shift * 4f;
                    break;
                }

                case IdleStyle.Bounce:
                {
                    // On the balls of the feet. The knees have to bend or it reads as an elevator.
                    float bounce = Mathf.Abs(Mathf.Sin(t * 2.2f)) * energy;
                    rootPos.y += bounce * 0.055f;
                    legR = -bounce * 9f;
                    legL = -bounce * 9f;
                    armR = -bounce * 13f;
                    armL = -bounce * 13f;
                    headRot.x -= bounce * 3f;
                    break;
                }

                case IdleStyle.Stillness:
                {
                    // Almost nothing — then, occasionally, a single fast head turn that stops dead.
                    // The stillness is what makes the turn land.
                    rootPos.y = breath * 0.004f;

                    float g = Gesture(dt, 0.85f);
                    if (g > 0f)
                    {
                        float turn = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, g * 3f))
                                   * (1f - Mathf.SmoothStep(0.7f, 1f, g));
                        headRot.y = turn * 62f;
                    }
                    armR = 1f;
                    armL = 1f;
                    break;
                }

                case IdleStyle.Stretch:
                {
                    // Hands behind the back, and every so often both arms go overhead.
                    float g = Gesture(dt, 1.8f);
                    if (g > 0f)
                    {
                        float raise = Mathf.Sin(g * Mathf.PI);
                        armR = -raise * 168f;
                        armL = -raise * 168f;
                        rootRot.x = -raise * 6f;
                        headRot.x -= raise * 10f;
                        rootPos.y += raise * 0.03f;
                    }
                    else
                    {
                        armR = 16f;
                        armL = 16f;
                        armSpread = -6f;              // tucked in behind
                        rootRot.y = Mathf.Sin(t * 0.5f) * 2f * energy;
                    }
                    break;
                }
            }

            _root.localPosition = rootPos;
            _root.localRotation = Quaternion.Euler(rootRot);
            _head.localRotation = Quaternion.Euler(headRot);
            _armR.localRotation = Quaternion.Euler(armR, 0f, armSpread);
            _armL.localRotation = Quaternion.Euler(armL, 0f, -armSpread);
            _legR.localRotation = Quaternion.Euler(legR, 0f, 0f);
            _legL.localRotation = Quaternion.Euler(legL, 0f, 0f);

            if (_torso != null) _torso.localScale = new Vector3(0.45f, 0.68f + breath * 0.005f, 0.23f);
        }

        /// <summary>
        /// Drives the occasional one-off gestures. Returns 0..1 while one is playing and 0 the rest
        /// of the time; the gap between them is randomised per figure so a row of six never fires
        /// together.
        /// </summary>
        private float Gesture(float deltaTime, float length)
        {
            _gestureTimer -= deltaTime;

            if (!_gestureActive)
            {
                if (_gestureTimer > 0f) return 0f;
                _gestureActive = true;
                _gestureLength = length;
                _gestureTimer = length;
            }

            float progress = 1f - Mathf.Clamp01(_gestureTimer / _gestureLength);
            if (_gestureTimer > 0f) return progress;

            _gestureActive = false;
            _gestureTimer = 4f + Random.value * 5f;
            return 0f;
        }

        private void OnDestroy()
        {
            if (_suit != null) Destroy(_suit);
            if (_trim != null) Destroy(_trim);
            if (_accent != null) Destroy(_accent);
        }
    }
}
