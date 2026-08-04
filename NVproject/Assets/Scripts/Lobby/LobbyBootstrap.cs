using System.Collections.Generic;
using UnityEngine;

namespace NV.Lobby
{
    /// <summary>
    /// The one thing that has to exist in the lobby scene. It builds the room, the row of stands,
    /// the manager, the transport and the interface at runtime — the same rule the rest of this
    /// project follows, so there is no prefab to keep in sync and no scene to hand-author.
    ///
    /// Put it on an empty GameObject called <c>Lobby</c>, or use
    /// **Tools ▸ Backrooms ▸ Create Lobby Scene**.
    /// </summary>
    [DefaultExecutionOrder(-70)]
    public sealed class LobbyBootstrap : MonoBehaviour
    {
        [Tooltip("Tunables. Left empty, a default instance is built at runtime.")]
        public LobbyConfig config;

        [Tooltip("Name the local player stands under. Empty falls back to a generated one.")]
        public string displayName = "YOU";

        private LobbyManager _lobby;
        private LobbyRoom _room;
        private readonly List<LobbySlot> _slots = new List<LobbySlot>();

        private void Awake()
        {
            if (config == null) config = ScriptableObject.CreateInstance<LobbyConfig>();

            float rowWidth = (config.maxPlayers - 1) * config.slotSpacing;

            _room = LobbyRoom.Build(transform, rowWidth);
            BuildSlots(rowWidth);

            // Manager first, then the interface: the HUD subscribes in OnEnable.
            _lobby = Create<LobbyManager>("Lobby Manager");

            int seed = config.seed != 0 ? config.seed : System.Environment.TickCount;
            var transport = new OfflineLobbyTransport(
                config.practiceLobbyBots, config.botJoinInterval, config.botReadyDelay, seed);

            // NETCODE: this is the single line that changes when the server pass happens. Swap the
            // offline transport for the networked one and nothing else in the lobby moves.
            _lobby.Configure(config, transport);

            _lobby.RosterChanged += BindSlots;

            var hud = Create<LobbyHud>("Lobby HUD");

            var picker = gameObject.AddComponent<LobbySlotPicker>();
            picker.lobbyCamera = _room.Camera;
            picker.hud = hud;
        }

        private void Start()
        {
            // The lobby is a menu you stand in, so the pointer belongs to the player.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _lobby.JoinLocalPlayer(string.IsNullOrEmpty(displayName) ? "YOU" : displayName);
            BindSlots();
        }

        private void BuildSlots(float rowWidth)
        {
            var root = new GameObject("__Slots").transform;
            root.SetParent(transform, false);

            // A straight row against the back wall, centred, all facing the camera. Straight and
            // evenly spaced is the whole read: a lineup you can count at a glance.
            for (int i = 0; i < config.maxPlayers; i++)
            {
                float x = -rowWidth * 0.5f + i * config.slotSpacing;
                var position = new Vector3(x, 0f, 2.2f);

                LobbySlot slot = LobbySlot.Spawn(i, position, root);
                slot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // face -Z, the camera
                _slots.Add(slot);
            }
        }

        /// <summary>Re-seats the row from the manager's roster. Cheap, and correctness beats cleverness here.</summary>
        private void BindSlots()
        {
            for (int i = 0; i < _slots.Count; i++)
                _slots[i].Bind(_lobby.Occupant(i));
        }

        private T Create<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }
    }
}
