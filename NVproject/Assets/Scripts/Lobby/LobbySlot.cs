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

        public int Index { get; private set; }
        public LobbyPlayer Player { get; private set; }

        private LobbyMannequin _mannequin;
        private MeshRenderer _plateRenderer;
        private Transform _mine;          // the "this one is you" marker

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

        /// <summary>Puts a player on this stand, or clears it. Idempotent — called on every roster change.</summary>
        public void Bind(LobbyPlayer player)
        {
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
            if (_mannequin == null) _mannequin = LobbyMannequin.Spawn(transform, Index);

            if (characterId >= 0 && characterId < LobbyCharacterCatalog.Count)
            {
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
            Player = null;

            if (_mannequin != null) Destroy(_mannequin.gameObject);
            _mannequin = null;
            _plateRenderer.sharedMaterial = _emptyMaterial;
            _mine.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_mine == null || !_mine.gameObject.activeSelf || _mineMaterial == null) return;

            float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.time * 2.4f));
            _mineMaterial.SetColor("_EmissionColor", new Color(1f, 0.72f, 0.28f) * pulse * 2.2f);
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

        }
    }
}
