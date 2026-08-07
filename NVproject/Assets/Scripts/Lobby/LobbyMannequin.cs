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

        // ============================================================ 퇴장
        //
        // 매치가 시작될 때 줄에 선 사람들이 사라지는 대신 **끌려 내려간다.** 발밑의 판이
        // 꺼지고, 잠깐 허공에서 허우적거리다가, 바닥 아래로 빨려 들어간다.
        //
        // 시간은 전부 여기 있다. 스탠드도 부트스트랩도 "얼마나 걸리는가" 를 다시 적지 않고
        // `DepartureFinished` 만 본다 — 두 곳에 적히면 한쪽만 고쳐져 컷이 애니메이션보다
        // 먼저 나거나 한참 뒤에 난다.

        /// 허우적거리는 시간(초).
        private const float FlailSeconds = 1f;

        /// 빨려 들어가는 시간(초). 짧아야 "쏙" 으로 읽힌다 — 길면 그냥 가라앉는 것이다.
        private const float DropSeconds = 0.45f;

        /// 들어 올려지는 높이(m). 발이 판에서 떨어져야 허우적거림이 뜻을 갖는다.
        private const float HoverHeight = 0.32f;

        /// 내려가는 거리(m). 카메라 화각 밖으로 확실히 빠지는 깊이다.
        private const float DropDistance = 3.4f;

        /// 허우적거릴 때 팔이 머무는 각도(도). **0 은 아래로 늘어뜨린 자세이고, 90 이 앞으로
        /// 수평, 180 이 머리 위다** — 어깨 관절이 X 로 도는 규약이 그렇다.
        ///
        /// 0 을 중심으로 흔들면 팔이 도는 구간의 절반을 몸 아래에서 쓴다. 그것은 허우적거림이
        /// 아니라 풍차이고, 무엇보다 아래쪽 절반은 몸통에 가려 보이지도 않는다. 중심을 머리
        /// 쪽으로 올리면 같은 진폭이 전부 화면에 남는다.
        private const float ArmRaise = 115f;

        /// 팔이 그 각도 둘레로 흔들리는 폭(도).
        private const float ArmSwing = 88f;

        /// 다리·머리·상체의 흔들림 폭(도).
        private const float LegSwing = 46f;
        private const float HeadSwing = 12f;

        /// 떨어지는 시각이 인형마다 어긋나는 최대 폭(초).
        ///
        /// **아주 작아야 한다.** 여섯이 정확히 같은 프레임에 떨어지면 줄이 아니라 한 덩어리로
        /// 보이고, 그렇다고 크게 벌리면 순서를 기다리는 줄이 된다. 0.16초는 눈에 "동시에
        /// 떨어졌다" 로 남으면서 칼같이 맞지는 않는 폭이다.
        private const float StaggerMax = 0.16f;

        /// 이 연출이 걸릴 수 있는 **가장 긴** 시간(초). 어긋남까지 포함한 상한이며,
        /// 전환을 붙잡는 쪽(`GameLobbyBootstrap`)이 이 값으로 벽시계 상한을 잡는다.
        public const float DepartureSeconds = FlailSeconds + StaggerMax + DropSeconds;

        private bool _departing;
        private float _departureTime;

        // 이 인형만의 값. `BeginDeparture` 에서 한 번 정한다.
        private float _dropAt;
        private float _departureLength;
        private float _flailRate;
        private float _hoverHeight;
        private float _limbPhase;
        private float _armRaise;

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

        // ============================================================ 퇴장

        /// <summary>
        /// 매치가 시작됐다. 허우적거리다 바닥으로 빨려 들어간다.
        ///
        /// 두 번 불러도 처음 것이 이어진다 — 명단은 전문으로 폴링되므로 시작 전문이 여러 번
        /// 올 수 있고, 그때마다 다시 시작하면 아무도 내려가지 않는다.
        /// </summary>
        public void BeginDeparture()
        {
            if (_departing) return;

            _departing = true;
            _departureTime = 0f;

            // 인형마다 조금씩 다르게. **`_phase` 에서 뽑는다** — 스탠드 번호에서 나온 값이라
            // 모든 클라이언트가 같은 것을 보고 매치마다 달라지지도 않는다. `Random` 을 쓰면
            // 같은 방을 보는 두 사람이 서로 다른 연출을 보게 되고, 그것은 이 방에서 유일하게
            // 어긋나는 것이 된다.
            _dropAt = FlailSeconds + Vary(1.7f) * StaggerMax;
            _departureLength = _dropAt + DropSeconds;

            // 허우적거리는 속도와 높이도 조금. 속도만 바꾸면 같은 동작을 배속으로 돌린 것처럼
            // 보이므로 팔다리의 위상도 함께 어긋낸다.
            _flailRate = 19f + Vary(3.1f) * 4f;
            _hoverHeight = HoverHeight * (0.88f + Vary(5.3f) * 0.24f);
            _limbPhase = Vary(7.9f) * Mathf.PI * 2f;

            // 팔을 올리는 각도도 조금씩 다르다. 여섯이 똑같은 높이로 만세하면 한 사람의
            // 동작을 복사한 것으로 보인다.
            _armRaise = ArmRaise + (Vary(11.3f) - 0.5f) * 16f;
        }

        /// <summary>
        /// 이 인형만의 0..1 값. 같은 `salt` 에는 늘 같은 답이고, 다른 `salt` 끼리는 무관해 보인다.
        /// </summary>
        private float Vary(float salt)
        {
            float v = Mathf.Sin((_phase + salt) * 12.9898f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        public bool Departing => _departing;

        public bool DepartureFinished => _departing && _departureTime >= _departureLength;

        /// <summary>
        /// 퇴장 한 프레임. idle 을 **대신한다** — 섞으면 숨쉬기와 흔들림이 허우적거림 위에
        /// 겹쳐 보이고, 그 둘은 같은 관절을 쓴다.
        /// </summary>
        private void StepDeparture(float deltaTime)
        {
            // **시계가 먼저다.** 아래에서 리그가 없다고 돌아가더라도 이 값은 진행해야 하고,
            // 그래야 `DepartureFinished` 가 언젠가 참이 된다.
            _departureTime += deltaTime;

            if (_root == null) return;

            float t = _departureTime;

            // 들어 올려진다. 발이 판에서 떨어지는 것이 먼저다.
            float height = Mathf.SmoothStep(0f, _hoverHeight, Mathf.Clamp01(t / 0.22f));

            if (t > _dropAt)
            {
                // **가속해서 떨어진다.** 등속으로 내리면 가라앉는 것으로 보인다.
                float fall = Mathf.Clamp01((t - _dropAt) / DropSeconds);
                height -= fall * fall * DropDistance;
            }

            // 떨어지기 시작하면 허우적거림이 잦아든다. 끝까지 팔을 젓고 있으면 빨려 들어가는
            // 것이 아니라 스스로 내려가는 것처럼 보인다.
            float energy = t <= _dropAt
                ? Mathf.Clamp01(t / 0.15f)
                : Mathf.Clamp01(1f - (t - _dropAt) / (DropSeconds * 0.8f));

            // 관절마다 주기를 어긋나게 둔다. 같은 주기로 흔들면 헤엄치는 것으로 보인다.
            // 시작 위상도 인형마다 다르다 — 속도만 다르면 같은 동작의 배속으로 보인다.
            float fast = _limbPhase + t * _flailRate;

            _root.localPosition = new Vector3(Mathf.Sin(fast * 0.7f) * 0.022f * energy, height, 0f);
            _root.localRotation = Quaternion.Euler(
                Mathf.Sin(fast * 0.53f) * 6f * energy,
                Mathf.Sin(fast * 0.31f) * 8f * energy,
                Mathf.Sin(fast * 0.87f) * 5f * energy);

            _head.localRotation = Quaternion.Euler(Mathf.Sin(fast * 1.13f) * HeadSwing * energy, 0f, 0f);

            // 팔은 머리 언저리에서 흔들린다. **올리는 각도까지 `energy` 를 곱한다** — 곱하지
            // 않으면 다 내려가는 순간에도 팔만 만세를 한 채로 굳는다. 잦아들면서 자연히
            // 늘어뜨린 자세로 돌아와야 한다.
            _armR.localRotation = Quaternion.Euler(
                (_armRaise + Mathf.Sin(fast) * ArmSwing) * energy, 0f, 3f + 20f * energy);
            _armL.localRotation = Quaternion.Euler(
                (_armRaise + Mathf.Sin(fast + 2.2f) * ArmSwing) * energy, 0f, -(3f + 20f * energy));

            _legR.localRotation = Quaternion.Euler(Mathf.Sin(fast * 1.27f + 0.8f) * LegSwing * energy, 0f, 0f);
            _legL.localRotation = Quaternion.Euler(Mathf.Sin(fast * 1.27f + 3.4f) * LegSwing * energy, 0f, 0f);
        }

        // ============================================================ the idles

        private void Update()
        {
            // **퇴장이 리그 검사보다 앞이다.** 아래의 `_root == null` 로 먼저 걸러 내면 리그가
            // 사라진 인형은 퇴장 시계가 멈추고, `DepartureFinished` 가 영영 참이 되지 않는다 —
            // 그것을 기다리는 씬 전환이 함께 멈춰 매치는 시작됐는데 플레이어만 대기방에 남는다.
            //
            // 리그가 사라지는 길은 실제로 있다. 이 필드들은 `[SerializeField]` 가 아니라
            // **도메인 리로드를 넘기지 못하고**, 플레이 중 스크립트 편집이 그것을 부른다.
            if (_departing)
            {
                StepDeparture(Time.deltaTime);
                return;
            }

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
