using NV.Client.Lobby;
using UnityEngine;

namespace NV.Game
{
    /// <summary>
    /// Paints a <see cref="BlockRig"/> as one of the eight lobby characters, and puts the right
    /// thing on its head.
    ///
    /// **The point is that the person you picked in the lobby is the person who walks into the
    /// maze.** Before this, the character was lobby-only decoration: the id travelled on the wire
    /// in the roster and was drawn on the waiting-room mannequins, and then everybody spawned as
    /// the same white block figure. Picking a character that evaporates at the door is worse than
    /// not offering the choice.
    ///
    /// It reads the same <see cref="LobbyCharacterCatalog"/> the lobby does, so the figure in the
    /// row and the figure in the level cannot drift apart — there is one table and both sides look
    /// it up by the same id.
    ///
    /// Applied to remote bodies *and* to your own. Your own body is on the PlayerBody layer and
    /// culled from your camera, but it is what the mirror shows, what casts your shadow, and what
    /// everyone else is looking at.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterAppearance : MonoBehaviour
    {
        /// <summary>No character assigned. Matches <c>RoomPlayerEntry.NoCharacter</c> on the wire.</summary>
        public const int None = 0xFF;

        private BlockRig _rig;
        private Transform _headGear;

        /// <summary>Which character this body is currently wearing, or <see cref="None"/>.</summary>
        public int CharacterId { get; private set; } = None;

        /// <summary>
        /// The rig was torn down and rebuilt (a role reveal swaps monster and humanoid).
        /// The painted blocks and the head gear died with the old body, so forget both —
        /// the roster poll calls <see cref="Apply"/> every frame and re-dresses a humanoid
        /// on the next one. Without this reset the applied id still matches the roster and
        /// the fresh white body never gets its character back.
        /// </summary>
        public void OnRigRebuilt()
        {
            _headGear = null;
            CharacterId = None;
        }

        /// <summary>
        /// Dresses the body. Cheap to call repeatedly — the roster is polled, so this is asked the
        /// same question every frame and only does work when the answer changes.
        /// </summary>
        public void Apply(int characterId)
        {
            if (characterId == CharacterId) return;

            if (_rig == null) _rig = GetComponent<BlockRig>();
            if (_rig == null || !_rig.IsBuilt) return;      // the rig builds in Awake; wait for it

            // A monster owns its colours — the plan painted it at build time, and the lobby
            // character this player picked stays parked until the body is human again.
            if (_rig.IsMonster) return;

            LobbyCharacterCatalog.Character character = characterId >= 0
                    && characterId < LobbyCharacterCatalog.Count
                ? LobbyCharacterCatalog.All[characterId]
                : null;

            if (character == null) return;

            CharacterId = characterId;

            Material suit = Palette.Suit(characterId);
            Material trim = Palette.Trim(characterId);
            Material accent = Palette.Accent(characterId);

            // Torso and arms are the overalls; legs and the belt are the trim; the head is the
            // skin tone. The same split the lobby mannequin uses.
            Paint(_rig.Torso, suit);
            Paint(_rig.ArmL, suit);
            Paint(_rig.ArmR, suit);
            Paint(_rig.LegL, trim);
            Paint(_rig.LegR, trim);
            Paint(_rig.Neck, accent);

            // Your own sleeves, in first person. Skipping these would leave you looking down at
            // white arms while everyone else sees your character.
            Paint(_rig.ViewArmL, suit);
            Paint(_rig.ViewArmR, suit);

            BuildHeadGear(character);
        }

        /// <summary>Repaints the blocks directly under a joint, and no deeper.</summary>
        private static void Paint(Transform joint, Material material)
        {
            if (joint == null) return;

            for (int i = 0; i < joint.childCount; i++)
            {
                Transform child = joint.GetChild(i);

                // Only the blocks. Descending further would repaint the pistol in the hand and the
                // hat on the head, both of which own their colour.
                var renderer = child.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.sharedMaterial = material;
            }
        }

        /// <summary>
        /// The silhouette. At any distance in this fog the outline is what tells two figures apart
        /// long before the colour does, which is why the lobby gives each character its own and why
        /// it has to come along.
        ///
        /// Sized from the rig's own head block rather than from constants, so it still fits if the
        /// figure's proportions are ever retuned.
        /// </summary>
        private void BuildHeadGear(LobbyCharacterCatalog.Character character)
        {
            if (_headGear != null) Destroy(_headGear.gameObject);
            _headGear = null;

            if (character.head == HeadGear.None || _rig.Neck == null) return;

            float head = _rig.HeadSize;
            int layer = _rig.bodyLayer;

            var root = new GameObject("Head Gear").transform;
            root.SetParent(_rig.Neck, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            _headGear = root;

            Material material = character.head == HeadGear.HardHat
                ? Palette.HardHat
                : Palette.Suit(CharacterId);

            switch (character.head)
            {
                case HeadGear.Cap:
                    Block(root, "Crown", new Vector3(0f, head * 1.06f, 0f),
                        new Vector3(head * 1.04f, head * 0.22f, head * 1.04f), material, layer);
                    Block(root, "Peak", new Vector3(0f, head * 0.98f, head * 0.62f),
                        new Vector3(head * 0.9f, head * 0.1f, head * 0.46f), material, layer);
                    break;

                case HeadGear.HardHat:
                    Block(root, "Shell", new Vector3(0f, head * 1.1f, 0f),
                        new Vector3(head * 1.1f, head * 0.4f, head * 1.1f), material, layer);
                    Block(root, "Brim", new Vector3(0f, head * 0.92f, 0f),
                        new Vector3(head * 1.35f, head * 0.1f, head * 1.35f), material, layer);
                    break;

                case HeadGear.Band:
                    Block(root, "Band", new Vector3(0f, head * 0.86f, 0f),
                        new Vector3(head * 1.06f, head * 0.2f, head * 1.06f), material, layer);
                    break;

                case HeadGear.Hood:
                    Block(root, "Hood", new Vector3(0f, head * 0.52f, -head * 0.09f),
                        new Vector3(head * 1.28f, head * 1.28f, head * 1.2f), material, layer);
                    Block(root, "Shade", new Vector3(0f, head * 0.52f, head * 0.56f),
                        new Vector3(head * 0.86f, head * 0.62f, head * 0.18f), Palette.Shade, layer);
                    break;

                case HeadGear.Visor:
                    Block(root, "Visor", new Vector3(0f, head * 0.62f, head * 0.5f),
                        new Vector3(head * 1.02f, head * 0.28f, head * 0.22f), material, layer);
                    Block(root, "Strap", new Vector3(0f, head * 0.62f, -head * 0.5f),
                        new Vector3(head * 0.86f, head * 0.18f, head * 0.18f), material, layer);
                    break;
            }
        }

        private static void Block(Transform parent, string name, Vector3 centre, Vector3 size,
            Material material, int layer)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.layer = layer;

            // Nothing worn on a character may stop a bullet. The body blocks carry no colliders for
            // the same reason and this has to match, or a hat becomes cover.
            Collider collider = cube.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            cube.GetComponent<MeshRenderer>().sharedMaterial = material;

            Transform t = cube.transform;
            t.SetParent(parent, false);
            t.localPosition = centre;
            t.localRotation = Quaternion.identity;
            t.localScale = size;
        }

        /// <summary>
        /// One material per colour per character, made once and shared by every body wearing it.
        /// Eight characters is twenty-four materials for the whole game rather than three per
        /// player, and they are constant — nothing tints them per body.
        /// </summary>
        private static class Palette
        {
            private static Material[] _suit, _trim, _accent;
            private static Material _hardHat, _shade;

            public static Material Suit(int id) => Get(ref _suit, id, c => c.suit, "Suit");
            public static Material Trim(int id) => Get(ref _trim, id, c => c.trim, "Trim");
            public static Material Accent(int id) => Get(ref _accent, id, c => c.accent, "Accent");

            public static Material HardHat =>
                _hardHat != null ? _hardHat : _hardHat = Make("Hard Hat", new Color(0.95f, 0.72f, 0.15f));

            public static Material Shade =>
                _shade != null ? _shade : _shade = Make("Hood Shade", new Color(0.05f, 0.05f, 0.06f));

            private static Material Get(ref Material[] cache, int id,
                System.Func<LobbyCharacterCatalog.Character, Color> pick, string label)
            {
                // Rebuilt if the array has gone: a domain reload during play wipes statics, and a
                // body repainted after one would otherwise get a null material and render pink.
                if (cache == null || cache.Length != LobbyCharacterCatalog.Count)
                    cache = new Material[LobbyCharacterCatalog.Count];

                if (id < 0 || id >= cache.Length) return null;
                if (cache[id] != null) return cache[id];

                LobbyCharacterCatalog.Character character = LobbyCharacterCatalog.All[id];
                return cache[id] = Make(character.label + " " + label, pick(character));
            }

            private static Material Make(string name, Color colour)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var material = new Material(shader) { name = name, color = colour };
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.08f);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
                material.enableInstancing = true;
                return material;
            }
        }
    }
}
