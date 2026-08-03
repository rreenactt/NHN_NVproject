using UnityEngine;
using UnityEngine.InputSystem;

namespace NV.Game
{
    /// <summary>
    /// The one thing that has to exist in the scene for the match to run. Everything else — the
    /// manager, the device system, the HUD, the objective, the practice Runners, the components
    /// bolted onto the player — is created here at runtime, which is the same rule the rest of
    /// this project follows: the scene holds almost nothing, and there is no prefab to keep in
    /// sync with the code.
    ///
    /// Put it on an empty GameObject called <c>Match</c>. It finds the level and the player by
    /// itself.
    /// </summary>
    [DefaultExecutionOrder(-70)]
    public sealed class MatchBootstrap : MonoBehaviour
    {
        [Tooltip("Balance values. Left empty, a default instance is built at runtime and the game " +
                 "still runs — it just cannot be tuned between sessions.")]
        public GameConfig config;

        [Tooltip("The level. Found by type if left empty.")]
        public BackroomsMapGenerator map;

        [Tooltip("The local player's root, the one carrying FirstPersonController.")]
        public FirstPersonController player;

        [Tooltip("Start the match as soon as the scene loads. Off, call BeginMatch yourself.")]
        public bool autoStart = true;

        [Header("Debug keys")]
        [Tooltip("F1 swaps the local player's side and restarts, F2 restarts, F5 takes a hit. " +
                 "Off in a real build.")]
        public bool debugKeys = true;

        private MatchManager _match;
        private DeviceSystem _devices;
        private PlayerAgent _localAgent;
        private Transform _runnerRoot;

        private void Awake()
        {
            if (config == null) config = ScriptableObject.CreateInstance<GameConfig>();
            if (map == null) map = FindFirstObjectByType<BackroomsMapGenerator>();
            if (player == null) player = FindFirstObjectByType<FirstPersonController>();

            // Order matters: the HUD subscribes to both systems in OnEnable, so both have to
            // exist first. Building them as children of this object keeps the hierarchy honest
            // about what created them.
            _match = Create<MatchManager>("Match Manager");
            _devices = Create<DeviceSystem>("Device System");
            _match.Configure(config, map);

            // UI Toolkit, from Assets/Resources/UI. The document builds its tree when the roles are
            // handed out, because which half of the HUD exists depends on which side you are on.
            Create<UI.GameHudController>("Match HUD");
        }

        private void Start()
        {
            // The level builds its grid and bakes a navmesh in its own Awake, so anything that
            // needs a place to stand has to wait until Start.
            SetUpLocalPlayer();
            SpawnPracticeRunners();

            if (autoStart) BeginMatch(config.localRole);
        }

        private T Create<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        private void SetUpLocalPlayer()
        {
            if (player == null)
            {
                Debug.LogWarning("[Match] No FirstPersonController in the scene; nothing to play as.");
                return;
            }

            GameObject go = player.gameObject;

            _localAgent = go.GetComponent<PlayerAgent>();
            if (_localAgent == null) _localAgent = go.AddComponent<PlayerAgent>();
            _localAgent.isLocalPlayer = true;
            _localAgent.displayName = "You";
            _localAgent.controller = player;
            _localAgent.head = player.cameraTransform;

            // Your own steps and your own gunshots are heard flat; everyone else's carry
            // positionally, which is the whole of how either side finds the other.
            var steps = go.GetComponent<FootstepAudio>();
            if (steps == null) steps = go.AddComponent<FootstepAudio>();
            steps.isLocalListener = true;

            var shots = go.GetComponent<WeaponAudio>();
            if (shots == null) shots = go.AddComponent<WeaponAudio>();
            shots.isLocalListener = true;

            if (go.GetComponent<PlayerInteractor>() == null) go.AddComponent<PlayerInteractor>();
            if (go.GetComponent<ChainDrag>() == null) go.AddComponent<ChainDrag>();
            if (go.GetComponent<PlayerRoleLoadout>() == null) go.AddComponent<PlayerRoleLoadout>();

            // Added after Awake, so OnEnable has already run for the components that subscribe.
            _match.Register(_localAgent);
        }

        private void SpawnPracticeRunners()
        {
            if (config.practiceRunners <= 0 || map == null || !map.HasGrid) return;

            _runnerRoot = new GameObject("__PracticeRunners").transform;
            _runnerRoot.SetParent(transform, false);

            var random = new System.Random(config.placementSeed != 0
                ? config.placementSeed + 977
                : System.Environment.TickCount);

            for (int i = 0; i < config.practiceRunners; i++)
            {
                if (!map.TryRandomPoint(random, out Vector3 point)) continue;
                PracticeRunner.Spawn($"Runner {i + 1}", point, config.practiceRunnerSpeed,
                    _runnerRoot, random.Next());
            }
        }

        /// <summary>
        /// Starts a match with the local player on the given side. The Seeker slot always goes to
        /// somebody: with the local player running, the first practice Runner takes it — not
        /// because it can hunt, but because half the ruleset (the Seeker camera feed, the win
        /// conditions, the blood the Seeker is supposed to see) is meaningless without one.
        /// </summary>
        public void BeginMatch(Role localRole)
        {
            PlayerAgent seeker = null;

            if (localRole == Role.Seeker)
            {
                seeker = _localAgent;
            }
            else
            {
                var agents = _match.Agents;
                for (int i = 0; i < agents.Count; i++)
                {
                    if (agents[i] == null || agents[i] == _localAgent) continue;
                    seeker = agents[i];
                    break;
                }
            }

            _match.BeginMatch(seeker);
            Debug.Log($"[Match] Started. You are the {(seeker == _localAgent ? "SEEKER" : "RUNNER")}. " +
                      $"{config.keysRequired} keys, {config.deviceCount} devices, " +
                      $"{Mathf.RoundToInt(config.matchDuration)}s on the clock.");
        }

        private void Update()
        {
            // Two gates, and both have to hold. `debugKeys` is the scene's own switch;
            // the environment's is what a shipped build turns off, so nobody has to
            // remember to clear the field before making one. MatchSync also clears it
            // whenever a session exists — a networked start owns the seed and the seeker.
            if (!debugKeys || !NV.Client.Config.NVEnvironment.Active.AllowDebugKeys) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.f1Key.wasPressedThisFrame)
            {
                config.localRole = config.localRole == Role.Seeker ? Role.Runner : Role.Seeker;
                BeginMatch(config.localRole);
            }
            else if (keyboard.f2Key.wasPressedThisFrame)
            {
                BeginMatch(config.localRole);
            }
            else if (keyboard.f5Key.wasPressedThisFrame && _localAgent != null)
            {
                // Offline there is nobody to shoot you, and the entire bleeding half of the
                // ruleset hangs off being hit. This is the only way to exercise it solo.
                _match.ReportHit(_localAgent);
            }
        }
    }
}
