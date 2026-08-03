using System;
using System.Collections.Generic;
using UnityEngine;

namespace NV.Game
{
    /// <summary>
    /// The referee. Every rule in <c>.claude/skills/game-rules/references/ruleset.md</c> that has
    /// to be decided — who is on which side, whether a hit kills, whether a key may go in, whether
    /// the match is over — is decided here and nowhere else.
    ///
    /// **This is written as the single authority on purpose.** The game is asymmetric multiplayer,
    /// and in an asymmetric game every rule is also an information rule: a client that can decide
    /// its own hits can decide it was never hit, and a client that can see the door's state can
    /// see the door. So the objects in the level (keys, door, devices, agents) hold state and
    /// raise intentions; this class resolves them. Dropping a network layer in later means
    /// running this on the host and replicating its events — not rewriting the rules.
    ///
    /// It currently runs locally, so "host" is this machine and there is one human agent. Nothing
    /// below assumes that.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public sealed class MatchManager : MonoBehaviour
    {
        private static MatchManager _instance;

        /// <summary>
        /// The referee, found again if the static has been wiped. Editing a script during play
        /// triggers a domain reload that clears every static but does *not* re-run Awake, so a
        /// plain static instance comes back null and every system that reaches for it throws for
        /// the rest of the session. The scene object survived; this finds it again.
        /// </summary>
        public static MatchManager Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<MatchManager>();
                return _instance;
            }
            private set => _instance = value;
        }

        // --- events the HUD and the loadout listen to. Anything a client would need to be told
        //     about is an event here, which is exactly the list a replication layer has to carry.
        public event Action<MatchPhase> PhaseChanged;
        public event Action<int, int> KeysChanged;              // inserted, required
        public event Action<int, int> EscapesChanged;           // escaped, needed
        public event Action<MatchOutcome> MatchEnded;
        public event Action<PlayerAgent, bool> AgentHit;        // victim, fatal
        public event Action RolesAssigned;
        public event Action<string> Notified;

        [SerializeField] private GameConfig config;
        [SerializeField] private BackroomsMapGenerator map;

        private readonly List<PlayerAgent> _agents = new List<PlayerAgent>();
        private readonly List<KeyPickup> _keys = new List<KeyPickup>();

        private Transform _objectiveRoot;
        private EscapeDoor _door;
        private System.Random _random;

        /// <summary>
        /// Placement randomness. Lazily rebuilt: a plain <see cref="System.Random"/> is managed
        /// state that a domain reload during play wipes without re-running Awake or BeginMatch,
        /// and every level query taking one would then throw for the rest of the session.
        /// </summary>
        private System.Random Rng => _random ??= new System.Random();
        private float _phaseTimer;
        private bool _globalFreeze;
        private int _runnersAtStart;

        public GameConfig Config => config;
        public BackroomsMapGenerator Map => map;
        public IReadOnlyList<PlayerAgent> Agents => _agents;
        public EscapeDoor Door => _door;

        /// <summary>
        /// Placement seed handed down by the server, or 0 to use <see cref="GameConfig"/>'s.
        ///
        /// In a networked match every client has to place the door, the keys and the devices in the
        /// same spots, and those spots come out of one seeded <see cref="System.Random"/>. Left to
        /// the config, a seed of 0 falls back to this machine's clock — so each player would get a
        /// different door. The symptom reads as "somebody is inserting keys into a door that isn't
        /// there", which does not look like a networking fault at all.
        ///
        /// Set instead of writing to the config asset: mutating a ScriptableObject at runtime
        /// persists it in the editor, and the next offline session would silently reuse the
        /// last match's seed.
        /// </summary>
        public int PlacementSeedOverride { get; set; }

        /// <summary>
        /// The server put everyone at their spawn, so the manager must not move them.
        ///
        /// Movement is server-authoritative. Teleporting agents here would be undone by the next
        /// snapshot anyway, and in between the local player visibly snaps twice.
        /// </summary>
        public bool ServerPlacesAgents { get; set; }

        /// <summary>
        /// Does this client decide when the match is over?
        ///
        /// With the rules still resolved client-side, letting every client end the match on its own
        /// count means they end it at different moments and disagree about the result. One client —
        /// the room's host — decides and reports; the rest take the answer. That is the seam the
        /// rules will move across when they become server-side.
        /// </summary>
        public bool ResolvesOutcome { get; set; } = true;

        public MatchPhase Phase { get; private set; } = MatchPhase.Lobby;
        public MatchOutcome Outcome { get; private set; } = MatchOutcome.None;
        public float TimeRemaining { get; private set; }
        public int KeysInserted { get; private set; }
        public int Escapes { get; private set; }

        /// <summary>Seconds left of the role reveal, or 0 once play has started.</summary>
        public float RevealRemaining => Phase == MatchPhase.RoleReveal ? Mathf.Max(0f, _phaseTimer) : 0f;

        public PlayerAgent LocalAgent
        {
            get
            {
                for (int i = 0; i < _agents.Count; i++)
                    if (_agents[i] != null && _agents[i].isLocalPlayer) return _agents[i];
                return null;
            }
        }

        public PlayerAgent Seeker
        {
            get
            {
                for (int i = 0; i < _agents.Count; i++)
                    if (_agents[i] != null && _agents[i].Role == Role.Seeker) return _agents[i];
                return null;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Configure(GameConfig gameConfig, BackroomsMapGenerator level)
        {
            if (gameConfig != null) config = gameConfig;
            if (level != null) map = level;
        }

        // ============================================================ roster

        public void Register(PlayerAgent agent)
        {
            if (agent != null && !_agents.Contains(agent)) _agents.Add(agent);
        }

        /// <summary>
        /// A disconnect, in the end, is just an agent that stopped existing — so the roster shrinks
        /// and the standing win conditions are re-evaluated in <see cref="Update"/>, not here.
        ///
        /// That indirection is load-bearing. Both leaving play mode and a domain reload disable
        /// every component in the scene one at a time, so evaluating on removal declares the
        /// Seeker to have wiped out the Runners every time either happens. Update does not run
        /// during either, and by the time it does the roster has re-registered itself.
        /// </summary>
        public void Unregister(PlayerAgent agent)
        {
            _agents.Remove(agent);
        }

        // ============================================================ match flow

        /// <summary>
        /// Starts a match: roles, placement, clock. Safe to call again — it tears the previous
        /// objective set down first, which is what the restart key uses.
        /// </summary>
        public void BeginMatch(PlayerAgent seeker)
        {
            if (config == null) config = ScriptableObject.CreateInstance<GameConfig>();

            // The server's seed wins. Everything placed below comes out of this one Random, so this
            // line is what makes two clients see the same door.
            int seed = PlacementSeedOverride != 0
                ? PlacementSeedOverride
                : config.placementSeed != 0
                    ? config.placementSeed
                    : Environment.TickCount;

            _random = new System.Random(seed);

            // The level's grid is plain managed memory and does not survive a domain reload, while
            // its geometry does. Ask for it back before anything is placed on it.
            map?.EnsureGrid();

            Outcome = MatchOutcome.None;
            KeysInserted = 0;
            Escapes = 0;
            TimeRemaining = config.matchDuration;
            _globalFreeze = false;

            for (int i = 0; i < _agents.Count; i++)
            {
                PlayerAgent agent = _agents[i];
                if (agent == null) continue;
                agent.ResetForMatch();
                agent.SetRole(agent == seeker ? Role.Seeker : Role.Runner);
            }

            _runnersAtStart = 0;
            for (int i = 0; i < _agents.Count; i++)
                if (_agents[i] != null && _agents[i].Role == Role.Runner) _runnersAtStart++;

            // Everyone lands back at the spawn room. Starting a Seeker on top of a Runner is a
            // coin-flip match, so the Runners get scattered instead.
            PlaceAgentsAtStart(seeker);

            PlaceObjectives();

            RolesAssigned?.Invoke();
            KeysChanged?.Invoke(KeysInserted, config.keysRequired);
            EscapesChanged?.Invoke(Escapes, config.escapesToWin);

            SetPhase(MatchPhase.RoleReveal);
            _phaseTimer = config.roleRevealDuration;
        }

        private void PlaceAgentsAtStart(PlayerAgent seeker)
        {
            if (ServerPlacesAgents || map == null || !map.HasGrid) return;

            for (int i = 0; i < _agents.Count; i++)
            {
                PlayerAgent agent = _agents[i];
                if (agent == null) continue;

                if (agent == seeker)
                {
                    agent.TeleportTo(map.SpawnCentre);
                    continue;
                }
                if (map.TryRandomPoint(Rng, out Vector3 point)) agent.TeleportTo(point);
            }
        }

        private void SetPhase(MatchPhase phase)
        {
            if (Phase == phase) return;
            Phase = phase;
            ApplyMovementLocks();
            PhaseChanged?.Invoke(phase);
        }

        private void Update()
        {
            switch (Phase)
            {
                case MatchPhase.RoleReveal:
                    _phaseTimer -= Time.deltaTime;
                    if (_phaseTimer <= 0f) SetPhase(MatchPhase.Playing);
                    break;

                case MatchPhase.Playing:
                    TimeRemaining -= Time.deltaTime;
                    if (TimeRemaining <= 0f)
                    {
                        TimeRemaining = 0f;

                        // Time attack: the clock running out is a Seeker win by itself, with no
                        // reference to how close the Runners were.
                        //
                        // The clock still runs on a client that does not resolve the outcome — the
                        // HUD needs it — but the ending comes from whoever does.
                        if (ResolvesOutcome)
                        {
                            EndMatch(MatchOutcome.SeekerTimeout);
                            return;
                        }
                    }
                    TickEscapes();
                    EvaluateWinConditions();
                    break;
            }
        }

        /// <summary>
        /// Escaping is standing in the open doorway, not touching it. A hold turns the last step
        /// of the objective into a moment the Seeker can still interrupt.
        /// </summary>
        private void TickEscapes()
        {
            if (_door == null || !_door.IsOpen) return;

            float radius = config.doorUseRadius;

            for (int i = 0; i < _agents.Count; i++)
            {
                PlayerAgent agent = _agents[i];
                if (agent == null || agent.Role != Role.Runner || !agent.InPlay) continue;

                Vector3 delta = agent.FeetPosition - _door.Position;
                if (Mathf.Abs(delta.y) > 2f) { agent.EscapeHold = 0f; continue; }
                delta.y = 0f;

                if (delta.sqrMagnitude > radius * radius) { agent.EscapeHold = 0f; continue; }

                agent.EscapeHold += Time.deltaTime;
                if (agent.EscapeHold >= config.escapeHoldTime) ReportEscape(agent);
            }
        }

        private void ReportEscape(PlayerAgent agent)
        {
            if (agent == null || agent.Escaped) return;

            agent.MarkEscaped();
            Escapes++;
            EscapesChanged?.Invoke(Escapes, config.escapesToWin);
            Notify($"{agent.displayName} ESCAPED  ({Escapes}/{config.escapesToWin})");

            EvaluateWinConditions();
        }

        private void EvaluateWinConditions()
        {
            if (Phase != MatchPhase.Playing || !ResolvesOutcome) return;

            if (Escapes >= config.escapesToWin) { EndMatch(MatchOutcome.RunnersEscaped); return; }

            // A match that never had a Runner in it cannot have had them wiped out. This also
            // fails safe after a domain reload, which empties the roster for a moment: the count
            // it is compared against is gone too, so the wipe check simply does not fire.
            if (!config.seekerWinsOnWipe || _runnersAtStart <= 0) return;

            int runnersLeft = 0;
            for (int i = 0; i < _agents.Count; i++)
            {
                PlayerAgent agent = _agents[i];
                if (agent != null && agent.Role == Role.Runner && agent.InPlay) runnersLeft++;
            }
            if (runnersLeft == 0) EndMatch(MatchOutcome.SeekerWipedRunners);
        }

        /// <summary>
        /// Ends the match on a result decided elsewhere — the room's host, relayed by the server.
        ///
        /// This exists because the rules are still resolved client-side. When they move onto the
        /// server this becomes the *only* way a match ends, and <see cref="EvaluateWinConditions"/>
        /// goes away.
        /// </summary>
        public void AcceptOutcome(MatchOutcome outcome)
        {
            if (Phase == MatchPhase.Ended) return;
            EndMatch(outcome);
        }

        /// <summary>
        /// Back to waiting, between matches. The objective set is left standing; the next
        /// <see cref="BeginMatch"/> tears it down, which is the same path the restart key uses.
        /// </summary>
        public void ReturnToLobby()
        {
            Outcome = MatchOutcome.None;
            SetPhase(MatchPhase.Lobby);
        }

        private void EndMatch(MatchOutcome outcome)
        {
            Outcome = outcome;
            SetPhase(MatchPhase.Ended);
            MatchEnded?.Invoke(outcome);
            Debug.Log($"[Match] Ended: {outcome}. Keys {KeysInserted}/{config.keysRequired}, escapes {Escapes}.");
        }

        // ============================================================ combat

        /// <summary>
        /// A round landed on someone. This is the only place the hit rules exist: first hit means
        /// bleeding, second means death, and any survivable hit throws the victim across the map.
        ///
        /// The immunity window matters more than it looks. The Seeker's three rounds can be in the
        /// air together, so without it a single burst kills through the teleport — the victim is
        /// hit, moved, and hit again by rounds two and three that were already fired at the place
        /// they used to be.
        /// </summary>
        public void ReportHit(PlayerAgent victim)
        {
            if (Phase != MatchPhase.Playing || victim == null || !victim.InPlay) return;
            if (victim.Role != Role.Runner) return;   // the Seeker cannot be shot; nobody else is armed
            if (Time.time - victim.LastHitTime < config.hitImmunity) return;

            victim.RegisterHit();
            bool fatal = victim.Hits >= config.runnerHitsToDie;

            if (fatal)
            {
                int dropped = victim.CarriedKeys;
                Vector3 deathPoint = victim.FeetPosition;

                victim.Kill();
                if (dropped > 0) ScatterKeys(dropped, deathPoint);

                Notify($"{victim.displayName} DOWN");
                AgentHit?.Invoke(victim, true);
                EvaluateWinConditions();
                return;
            }

            // Survivable hit: bleeding starts, and the victim is thrown somewhere else. Being
            // teleported is the punishment that stings — whatever the Runner was doing is over,
            // and they no longer know where they are.
            victim.SetBleeding(true);
            if (config.teleportOnHit) TeleportToRandomPoint(victim);

            Notify(victim.isLocalPlayer ? "HIT — BLEEDING. KEEP MOVING" : $"{victim.displayName} HIT");
            AgentHit?.Invoke(victim, false);
        }

        public void ClearBleeding(PlayerAgent agent)
        {
            if (agent == null) return;
            agent.SetBleeding(false);
        }

        /// <summary>
        /// Teleport-on-hit and the teleport device both land here. A point drawn from the grid is
        /// standable by construction; if the draw somehow fails, the fallback walks outward to the
        /// nearest standable cell rather than dropping the player inside a wall.
        /// </summary>
        public void TeleportToRandomPoint(PlayerAgent agent)
        {
            if (agent == null || map == null || !map.HasGrid) return;

            if (map.TryRandomPoint(Rng, out Vector3 point))
            {
                agent.TeleportTo(point);
                return;
            }
            if (map.TryNearestStandablePoint(agent.FeetPosition, out Vector3 fallback))
                agent.TeleportTo(fallback);
        }

        // ============================================================ objective

        /// <summary>
        /// A Runner walked over a key. Carrying is capped only if the config says so — the default
        /// is uncapped, which makes one Runner hoarding keys a real (and punishable) strategy.
        /// </summary>
        public bool TryPickUpKey(PlayerAgent agent, KeyPickup key)
        {
            if (Phase != MatchPhase.Playing || agent == null || key == null || key.Collected) return false;
            if (agent.Role != Role.Runner || !agent.InPlay || !agent.collectsKeys) return false;
            if (config.carryLimit > 0 && agent.CarriedKeys >= config.carryLimit) return false;

            agent.AddKeys(1);
            _keys.Remove(key);
            key.Collect();

            if (agent.isLocalPlayer) Notify($"KEY  ({agent.CarriedKeys} carried)");
            KeysChanged?.Invoke(KeysInserted, config.keysRequired);
            return true;
        }

        /// <summary>
        /// One key into the door. Inserts are serialised through this method, which is what makes
        /// the "two Runners insert the tenth key at the same instant" case a non-event: whichever
        /// call arrives first crosses the threshold, and the second finds the door already open.
        /// </summary>
        public bool TryInsertKey(PlayerAgent agent, EscapeDoor door)
        {
            if (Phase != MatchPhase.Playing || agent == null || door == null) return false;
            if (agent.Role != Role.Runner || !agent.InPlay) return false;
            if (door.IsOpen) return false;
            if (agent.CarriedKeys <= 0) { Notify("NO KEYS IN HAND"); return false; }
            if (Time.time < agent.NextInsertTime) return false;

            Vector3 delta = agent.FeetPosition - door.Position;
            delta.y = 0f;
            if (delta.sqrMagnitude > config.doorUseRadius * config.doorUseRadius) return false;

            agent.AddKeys(-1);
            agent.NextInsertTime = Time.time + config.keyInsertInterval;
            KeysInserted++;

            KeysChanged?.Invoke(KeysInserted, config.keysRequired);

            if (KeysInserted >= config.keysRequired)
            {
                door.Open();
                Notify("THE DOOR IS OPEN");
            }
            else
            {
                Notify($"KEY IN  {KeysInserted}/{config.keysRequired}");
            }
            return true;
        }

        /// <summary>Device 1. Stacks additively onto whatever is left, per the ruleset's edge case.</summary>
        public void AddTime(float seconds)
        {
            if (Phase != MatchPhase.Playing) return;
            TimeRemaining += seconds;
        }

        // ============================================================ freeze

        public void SetGlobalFreeze(bool frozen)
        {
            _globalFreeze = frozen;
            ApplyMovementLocks();
        }

        /// <summary>
        /// One place decides who may move. The reveal holds everyone still, the freeze device
        /// holds everyone still, and the end of a match holds everyone still; without a single
        /// recomputation the freeze device's expiry would hand movement back to a player the role
        /// reveal was supposed to be holding.
        /// </summary>
        private void ApplyMovementLocks()
        {
            bool locked = _globalFreeze || Phase == MatchPhase.RoleReveal || Phase == MatchPhase.Ended;
            for (int i = 0; i < _agents.Count; i++)
                if (_agents[i] != null) _agents[i].SetFrozen(locked);
        }

        // ============================================================ placement

        private void PlaceObjectives()
        {
            ClearObjectives();

            if (map == null || !map.HasGrid)
            {
                Debug.LogWarning("[Match] No level to place the objective in.");
                return;
            }

            _objectiveRoot = new GameObject("__Objectives").transform;
            _objectiveRoot.SetParent(transform, false);

            // The altar first: it is the only fixed thing here, so everything random has to work
            // around it rather than the other way round.
            PlaceChainAltar();

            // The door next, and everything else keeps its distance, so a key never spawns inside
            // the doorway and the objective is never trivially short.
            if (map.TryRandomPoint(Rng, out Vector3 doorPoint))
            {
                float yaw = (float)Rng.NextDouble() * 360f;
                _door = EscapeDoor.Spawn(doorPoint, Quaternion.Euler(0f, yaw, 0f), _objectiveRoot);
            }

            int keyCount = Mathf.Max(config.keysRequired, config.keysPlaced);
            for (int i = 0; i < keyCount; i++)
                if (TryFindSpacedPoint(4f, out Vector3 point))
                    _keys.Add(KeyPickup.Spawn(point, _objectiveRoot));

            PlaceDevices();
        }

        /// <summary>
        /// The chain's altar, at the middle of the ground floor. Unlike everything else here it
        /// does *not* move between matches — the Seeker is meant to learn exactly where the third
        /// shot sends them, and a punishment you cannot predict is only a nuisance.
        ///
        /// The literal centre of the grid is inside the stairwell, so this walks outward until it
        /// finds a standable cell that is clear of it and has a neighbour to stand on.
        /// </summary>
        private void PlaceChainAltar()
        {
            int centre = map.GridSize / 2;

            for (int radius = 0; radius < map.GridSize; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != radius) continue;   // ring only

                    int x = centre + dx, z = centre + dz;
                    if (!IsFreeFloor(x, z)) continue;
                    if (!TryFindLandingSpot(x, z, out Vector3 dragPoint)) continue;

                    ChainAltar.Spawn(map.CellToWorld(0, x, z), dragPoint, _objectiveRoot);
                    return;
                }
            }

            Debug.LogWarning("[Match] No room for the chain altar; the chain will fall back to walls.");
        }

        private static readonly Vector2Int[] Around =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
        };

        /// <summary>Somewhere next door a body can actually stand, for the chain to drop the Seeker on.</summary>
        private bool TryFindLandingSpot(int x, int z, out Vector3 point)
        {
            foreach (Vector2Int offset in Around)
            {
                int nx = x + offset.x, nz = z + offset.y;
                if (!IsFreeFloor(nx, nz)) continue;

                point = map.CellToWorld(0, nx, nz);
                return true;
            }

            point = Vector3.zero;
            return false;
        }

        /// <summary>
        /// Standable *and* empty. The grid alone is not enough to answer this: the stairwell's
        /// cells are walkable in the grid while being full of staircase, and the first version of
        /// the altar dropped the Seeker onto a step every single time — they landed 3.8 m from
        /// where the chain aimed, because the CharacterController pushed itself out of the geometry
        /// the moment collision came back on. A capsule the size of a player answers it properly.
        /// </summary>
        private bool IsFreeFloor(int x, int z)
        {
            if (!map.IsStandable(0, x, z)) return false;
            if (map.stairwell.Contains(new Vector2Int(x, z))) return false;

            Vector3 feet = map.CellToWorld(0, x, z);
            return !Physics.CheckCapsule(feet + Vector3.up * 0.35f, feet + Vector3.up * 1.5f, 0.32f,
                                         ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// The device mix. It is a level-design choice by the ruleset, so it is spelled out rather
        /// than randomised: one of each effect covers the table, and the spare slots go to the
        /// repeatable ones — a second Add Time would double a one-shot swing, while a second map
        /// view only saves a walk.
        /// </summary>
        private void PlaceDevices()
        {
            var mix = new List<DeviceType>
            {
                DeviceType.AddTime,
                DeviceType.FullMapView,
                DeviceType.StopBleeding,
                DeviceType.FreezeAndXray,
                DeviceType.SeekerCameraView,
                DeviceType.Teleport,
                DeviceType.Teleport,
                DeviceType.FullMapView,
                DeviceType.StopBleeding,
            };

            int count = Mathf.Clamp(config.deviceCount, 1, mix.Count);
            for (int i = 0; i < count; i++)
            {
                if (!TryFindSpacedPoint(5f, out Vector3 point)) continue;

                float yaw = (float)Rng.NextDouble() * 360f;
                MapDevice device = MapDevice.Spawn(mix[i], point, yaw, _objectiveRoot);
                DeviceSystem.Instance?.Register(device);
            }
        }

        /// <summary>A random standable point that is not on top of something already placed.</summary>
        private bool TryFindSpacedPoint(float minSpacing, out Vector3 point)
        {
            for (int attempt = 0; attempt < 64; attempt++)
            {
                if (!map.TryRandomPoint(Rng, out point)) break;
                if (IsClearOfPlacements(point, minSpacing)) return true;
            }
            return map.TryRandomPoint(Rng, out point);
        }

        private bool IsClearOfPlacements(Vector3 point, float spacing)
        {
            float sqr = spacing * spacing;

            if (_door != null && (point - _door.Position).sqrMagnitude < sqr) return false;

            ChainAltar altar = ChainAltar.Instance;
            if (altar != null && (point - altar.transform.position).sqrMagnitude < sqr) return false;

            for (int i = 0; i < _keys.Count; i++)
                if (_keys[i] != null && (point - _keys[i].transform.position).sqrMagnitude < sqr) return false;

            IReadOnlyList<MapDevice> devices = DeviceSystem.Instance != null
                ? DeviceSystem.Instance.Devices : null;
            if (devices != null)
                for (int i = 0; i < devices.Count; i++)
                    if (devices[i] != null && (point - devices[i].transform.position).sqrMagnitude < sqr) return false;

            return true;
        }

        /// <summary>Keys a dead Runner was carrying, dropped where they fell so they stay findable.</summary>
        private void ScatterKeys(int count, Vector3 around)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 point = around;

                if (config.dropKeysOnDeath)
                {
                    float angle = (float)Rng.NextDouble() * Mathf.PI * 2f;
                    var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.7f;
                    if (!map.TryNearestStandablePoint(around + offset, out point)) point = around;
                }
                else if (!map.TryRandomPoint(Rng, out point))
                {
                    continue;
                }

                _keys.Add(KeyPickup.Spawn(point, _objectiveRoot));
            }
        }

        private void ClearObjectives()
        {
            _keys.Clear();
            _door = null;
            DeviceSystem.Instance?.ClearAll();

            if (_objectiveRoot != null)
            {
                // Deactivate before destroying. Destroy is deferred to the end of the frame, so the
                // old altar and devices are still solid to a physics query for the rest of *this*
                // one — which made the altar's "is this cell free" test reject its own previous
                // position and walk the altar somewhere new on every restart.
                _objectiveRoot.gameObject.SetActive(false);
                Destroy(_objectiveRoot.gameObject);
            }
            _objectiveRoot = null;

            map?.SetWallTransparency(1f);
        }

        public void Notify(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Notified?.Invoke(message);
        }
    }
}
