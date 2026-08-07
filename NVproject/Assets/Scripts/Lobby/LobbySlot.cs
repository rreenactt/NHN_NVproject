using UnityEngine;


// 캐릭터 표는 새 로비(NV.Client.Lobby)로 옮겼다. 이 파일들은 서버 연동 대기방으로
// 대체되며 함께 지워진다 — NVserver/docs/game-lobby-plan.md 7.1 절.
using NV.Client.Lobby;

namespace NV.Lobby
{
    /// <summary>
    /// One stand position in the row: the floor plate and the figure on it. **No text at all.**
    ///
    /// **The floor plate is the readout.** Empty, occupied and ready are three colours of the same
    /// object, so the state of the whole room is one glance along the row rather than six labels to
    /// read. An earlier pass stamped a number on every plate and a floating "READY" over every
    /// head; both were noise standing between the player and six figures they were supposed to be
    /// looking at.
    ///
    /// Nothing floats above the row any more — not a number, not a READY stamp, not a name, not a
    /// character label. Six labels hanging over six people is text you read *instead of* looking at
    /// the figures, and every one of those things has a better home: names and ready state in the
    /// roster panel, the character list in the picker. What is left on the floor is a colour, and
    /// the one thing that colour cannot say — which stand is yours — is a small marker in front of
    /// it instead of a word.
    ///
    /// The stand also carries the click target for everything on it — a tall box on the plate —
    /// which is why the figure's own blocks have no colliders. Clicking a person and clicking the
    /// space they occupy have to be the same gesture.
    /// </summary>
    public sealed class LobbySlot : MonoBehaviour
    {
        private static Material _emptyMaterial, _filledMaterial, _readyMaterial, _mineMaterial;
        private static Material _holeMaterial, _rimMaterial;

        /// 구멍이 다 열리는 시간(초). 인형이 허우적거리기 시작하는 것보다 먼저 끝나야
        /// **구멍이 생겨서 그리로 들어가는 것**으로 읽힌다 — 동시에 일어나면 바닥이 꺼진
        /// 것인지 사람이 내려간 것인지 알 수 없다.
        private const float HoleOpenSeconds = 0.28f;

        /// 구멍의 한 변(m). 판(1.1)보다 조금 작다 — 판이 있던 자리가 뚫린 것으로 보여야 한다.
        private const float HoleSize = 1.02f;

        /// 테두리의 한 변(m). 구멍보다 커서 깨진 바닥의 가장자리가 된다.
        private const float RimSize = 1.2f;

        public int Index { get; private set; }
        public LobbyPlayer Player { get; private set; }

        private LobbyMannequin _mannequin;
        private MeshRenderer _plateRenderer;
        private Transform _mine;          // the "this one is you" marker

        /// 지금 인형이 입고 있는 캐릭터. -1 은 아직 아무것도 입지 않았다는 뜻이다.
        ///
        /// `ApplyCharacter` 가 멱등이 아니기 때문에 필요하다 — 자세한 이유는 `Bind` 에 있다.
        private int _appliedCharacter = -1;

        /// 퇴장이 시작됐는가. 시작되면 이 스탠드는 명단 갱신을 더 받지 않는다.
        private bool _departing;

        private Transform _hole, _rim;

        /// 구멍이 열린 시간(초). 음수면 아직 열리지 않았다.
        private float _holeTime = -1f;

        public static LobbySlot Spawn(int index, Vector3 position, Transform parent)
        {
            var go = new GameObject("Slot " + (index + 1));
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            var slot = go.AddComponent<LobbySlot>();
            slot.Index = index;
            slot.Build();
            return slot;
        }

        private void Build()
        {
            EnsureShared();

            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Plate";
            plate.transform.SetParent(transform, false);
            plate.transform.localPosition = new Vector3(0f, 0.015f, 0f);
            plate.transform.localScale = new Vector3(1.1f, 0.03f, 1.1f);
            _plateRenderer = plate.GetComponent<MeshRenderer>();
            _plateRenderer.sharedMaterial = _emptyMaterial;

            // One collider for the whole stand, tall enough to catch a click on the figure as well.
            plate.GetComponent<BoxCollider>().size = new Vector3(1f, 70f, 1f);

            BuildMineMarker();
            BuildHole();
        }

        /// <summary>
        /// A small bar on the floor at the front of your own stand. With no names over the row this
        /// is the only thing that says which figure is you, so it pulses — a static marker at this
        /// size disappears into the carpet.
        /// </summary>
        private void BuildMineMarker()
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "Yours";
            Destroy(marker.GetComponent<Collider>());
            marker.transform.SetParent(transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.03f, -0.62f);
            marker.transform.localScale = new Vector3(0.42f, 0.03f, 0.1f);
            marker.GetComponent<MeshRenderer>().sharedMaterial = _mineMaterial;

            _mine = marker.transform;
            _mine.gameObject.SetActive(false);
        }

        /// <summary>
        /// The opening the figure goes down. Two flat boxes, built dark and scaled up from nothing
        /// when the match starts.
        ///
        /// **The rim is what makes it read as a hole.** The lobby camera sits at 1.3 m looking level
        /// at a row seven metres away, so it meets the floor at about ten degrees — a black square on
        /// the carpet at that angle is a smear a couple of centimetres tall, and it reads as a shadow.
        /// A pale edge around it does not depend on the viewing angle: the eye takes a bright outline
        /// with black inside as an opening, and it survives the fog at the far end of the row.
        ///
        /// Depth is not modelled. A pit's inner walls are invisible from ten degrees anyway — the
        /// near lip hides them — so it would be geometry nobody can see.
        /// </summary>
        private void BuildHole()
        {
            _rim = Slab("Hole Rim", 0.012f, _rimMaterial);
            _hole = Slab("Hole", 0.02f, _holeMaterial);
        }

        private Transform Slab(string name, float height, Material material)
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = name;
            Destroy(slab.GetComponent<Collider>());
            slab.transform.SetParent(transform, false);
            slab.transform.localPosition = new Vector3(0f, height, 0f);
            slab.transform.localScale = Vector3.zero;
            slab.GetComponent<MeshRenderer>().sharedMaterial = material;
            slab.SetActive(false);
            return slab.transform;
        }

        /// <summary>
        /// The match has started: the plate goes dark and the figure is taken down through it.
        ///
        /// **The plate leaves first.** It is the ready readout, and ready stops meaning anything the
        /// instant the match begins — leaving it lit under a figure being hauled off says the room is
        /// still waiting for somebody.
        ///
        /// Once this is called the stand stops taking roster updates. The bulletin keeps arriving
        /// through the transition (somebody can still leave), and a refresh would relight the plate
        /// and re-dress a figure that is already half through the floor.
        /// </summary>
        public void BeginDeparture()
        {
            if (_departing) return;

            _departing = true;

            _plateRenderer.enabled = false;
            _mine.gameObject.SetActive(false);

            // 판이 꺼진 자리에서 구멍이 열린다.
            _holeTime = 0f;
            _hole.gameObject.SetActive(true);
            _rim.gameObject.SetActive(true);

            if (_mannequin != null) _mannequin.BeginDeparture();
        }

        /// <summary>Nothing left to wait for — an empty stand is finished before it starts.</summary>
        public bool DepartureFinished => _mannequin == null || _mannequin.DepartureFinished;

        /// <summary>Puts a player on this stand, or clears it. Idempotent — called on every roster change.</summary>
        public void Bind(LobbyPlayer player)
        {
            if (_departing) return;

            Player = player;

            if (player == null)
            {
                Clear();
                return;
            }

            Bind(LobbyCharacterCatalog.IndexOf(player.characterId), player.isReady, player.isLocal);
        }

        /// <summary>
        /// 같은 일을 원시 값으로 한다. **서버 연동 대기방이 쓰는 문이다.**
        ///
        /// `LobbyPlayer` 는 오프라인 프로토타입의 타입이고 그것과 함께 사라진다. 스탠드의
        /// 겉모습은 사라질 이유가 없으므로 — 판정이 아니라 표현이다 — 판정 타입에 묶여
        /// 있던 것을 풀어 둔다. 위의 오버로드는 프로토타입이 살아 있는 동안의 껍데기다.
        /// </summary>
        /// <param name="characterId">카탈로그 인덱스. 범위를 벗어나면 옷을 갈아입히지 않는다.</param>
        public void Bind(int characterId, bool isReady, bool isLocal)
        {
            if (_departing) return;

            if (_mannequin == null)
            {
                _mannequin = LobbyMannequin.Spawn(transform, Index);

                // 새 인형은 아무것도 입지 않았다. 아래의 멱등 판정이 통과해야 한다.
                _appliedCharacter = -1;
            }

            // **바뀌었을 때만 갈아입힌다.** `ApplyCharacter` 는 멱등이 아니다 — 머리 장식을
            // 지우고 다시 만들고, 머티리얼을 새로 할당하고, idle 제스처 타이머를 되돌린다.
            //
            // 이 함수는 서버 전문으로 폴링되므로(프레임마다 불릴 수 있다) 그대로 부르면
            // 제스처가 매 프레임 리셋되어 **인형이 굳은 채로 서 있고**, 프레임마다 GameObject
            // 와 머티리얼이 하나씩 생긴다. 증상은 "캐릭터를 골라도 아무 일도 없다" 로 보인다.
            //
            // 프로토타입은 명단 변경 이벤트로만 불렀기 때문에 이 함정을 밟지 않았다.
            // `SetReady` 는 스스로 같은 판정을 갖고 있다.
            if (characterId != _appliedCharacter
                && characterId >= 0
                && characterId < LobbyCharacterCatalog.Count)
            {
                _appliedCharacter = characterId;
                _mannequin.ApplyCharacter(LobbyCharacterCatalog.All[characterId]);
            }

            _mannequin.SetReady(isReady);

            // The plate carries the ready state, and nothing else has to.
            _plateRenderer.sharedMaterial = isReady ? _readyMaterial : _filledMaterial;

            _mine.gameObject.SetActive(isLocal);
        }

        /// <summary>이 스탠드를 비운다.</summary>
        public void Clear()
        {
            if (_departing) return;

            Player = null;

            if (_mannequin != null) Destroy(_mannequin.gameObject);
            _mannequin = null;

            // 인형과 함께 버린다. 남겨 두면 같은 캐릭터가 다시 들어왔을 때 새 인형이 옷을
            // 입지 못한다 — 판정이 "이미 그것을 입고 있다" 로 통과해 버린다.
            _appliedCharacter = -1;
            _plateRenderer.sharedMaterial = _emptyMaterial;
            _mine.gameObject.SetActive(false);
        }

        private void Update()
        {
            StepHole();

            if (_mine == null || !_mine.gameObject.activeSelf || _mineMaterial == null) return;

            float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.time * 2.4f));
            _mineMaterial.SetColor("_EmissionColor", new Color(1f, 0.72f, 0.28f) * pulse * 2.2f);
        }

        /// 구멍이 열리는 한 프레임. 다 열리면 스스로 멈춘다.
        private void StepHole()
        {
            if (_holeTime < 0f || _hole == null || _rim == null) return;

            _holeTime += Time.deltaTime;

            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_holeTime / HoleOpenSeconds));

            _hole.localScale = new Vector3(HoleSize * k, 0.02f, HoleSize * k);
            _rim.localScale = new Vector3(RimSize * k, 0.012f, RimSize * k);

            if (_holeTime >= HoleOpenSeconds) _holeTime = -1f;
        }

        private static void EnsureShared()
        {
            if (_emptyMaterial != null) return;

            Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            _emptyMaterial = new Material(lit) { name = "Stand Empty", color = new Color(0.26f, 0.24f, 0.18f) };
            _filledMaterial = new Material(lit) { name = "Stand Taken", color = new Color(0.50f, 0.45f, 0.28f) };

            // Ready glows. Colour alone is legible under the room's lights, but the emission is what
            // makes the state readable at the far end of the row through the fog.
            _readyMaterial = new Material(lit) { name = "Stand Ready", color = new Color(0.34f, 0.62f, 0.38f) };
            _readyMaterial.EnableKeyword("_EMISSION");
            _readyMaterial.SetColor("_EmissionColor", new Color(0.24f, 0.55f, 0.30f) * 1.6f);
            _readyMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

            _mineMaterial = new Material(lit) { name = "Stand Yours", color = new Color(1f, 0.72f, 0.28f) };
            _mineMaterial.EnableKeyword("_EMISSION");
            _mineMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

            // 구멍은 **빛을 받지 않는다.** Lit 로 두면 방의 형광등이 검은 면을 회색으로 만들어
            // 바닥에 놓인 판처럼 보인다. 구멍은 빛이 닿지 않는 곳이라는 것이 요점이다.
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            _holeMaterial = new Material(unlit) { name = "Stand Hole", color = Color.black };
            if (_holeMaterial.HasProperty("_BaseColor")) _holeMaterial.SetColor("_BaseColor", Color.black);

            // 가장자리는 깨진 바닥이다. 스스로 빛나야 얕은 각도에서도 윤곽이 남는다.
            _rimMaterial = new Material(lit) { name = "Stand Hole Rim", color = new Color(0.78f, 0.70f, 0.46f) };
            _rimMaterial.EnableKeyword("_EMISSION");
            _rimMaterial.SetColor("_EmissionColor", new Color(0.62f, 0.52f, 0.28f) * 1.4f);
            _rimMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

        }
    }
}
