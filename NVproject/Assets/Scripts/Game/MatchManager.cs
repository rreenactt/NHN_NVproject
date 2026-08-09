using System;
using System.Collections.Generic;
using NV.Client.Map;
using NV.Shared.Simulation;
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

        /// <summary>
        /// The level, held as the Unity object rather than as <see cref="ILevelQuery"/>.
        ///
        /// **Unity cannot serialize an interface-typed field**, and this one has to survive a domain
        /// reload — a plain managed reference would come back null mid-play and every rule that asks
        /// the level a question would throw for the rest of the session.
        ///
        /// Keeping the Unity type here also keeps Unity's own null behaviour: a destroyed component
        /// compares equal to <c>null</c> through <c>MonoBehaviour</c>, and does not through an
        /// interface reference. <see cref="Map"/> is the view onto it and does that check itself.
        /// </summary>
        [SerializeField] private MonoBehaviour map;

        private readonly List<PlayerAgent> _agents = new List<PlayerAgent>();
        private readonly List<KeyPickup> _keys = new List<KeyPickup>();

        private Transform _objectiveRoot;
        private EscapeDoor _door;
        private System.Random _random;

        /// <summary>
        /// Offline placement scratch. Reused so a restart does not allocate a fresh one, and
        /// <c>Reset</c> inside the shared placement clears it.
        /// </summary>
        private readonly NV.Shared.Simulation.Objectives _placement = new NV.Shared.Simulation.Objectives();

        /// <summary>
        /// Placement randomness. Lazily rebuilt: a plain <see cref="System.Random"/> is managed
        /// state that a domain reload during play wipes without re-running Awake or BeginMatch,
        /// and every level query taking one would then throw for the rest of the session.
        /// </summary>
        private System.Random Rng => _random ??= new System.Random();
        private float _phaseTimer;
        private bool _globalFreeze;
        private int _runnersAtStart;

        /// <summary>
        /// How many escapes actually win this match — the ruleset's number, capped at how many
        /// Runners there were to begin with.
        ///
        /// A two-player match has one Runner, and asking two of them to leave is asking for
        /// something the room cannot produce. Everything that shows or judges the target reads
        /// this rather than the constant, so the HUD, the escape notice and the win check cannot
        /// disagree about what the goal is.
        /// </summary>
        public int EscapesNeeded =>
            NV.Shared.Simulation.MatchConstants.EscapesToWinWith(_runnersAtStart);

        public GameConfig Config => config;
        /// <summary>
        /// The level, or <c>null</c> if there is none — including the case where the component was
        /// destroyed, which the <c>map == null</c> here catches through Unity's operator and a bare
        /// <c>as</c> would not.
        /// </summary>
        public ILevelQuery Map => map == null ? null : map as ILevelQuery;
        public IReadOnlyList<PlayerAgent> Agents => _agents;
        public EscapeDoor Door => _door;

        /// <summary>
        /// Placement seed for **offline** play, or 0 to use <see cref="GameConfig"/>'s.
        ///
        /// **The server no longer sends one, and that is the point.** It used to: every client
        /// received the same seed and computed the door's position from it, which put those
        /// coordinates in the Seeker's process — a culling layer hides the door on screen but the
        /// WebGL build is decompilable, so that was never a defence. The server now places the
        /// objective and sends coordinates filtered per role, so the seed has no reason to travel
        /// and <see cref="RoomStateHeader"/> no longer carries it.
        ///
        /// What is left here is the offline path. Nothing sets it today — offline seeding falls to
        /// <c>GameConfig.placementSeed</c>, and 0 there means this machine's clock, which is correct
        /// when there is nobody to agree with. Set it to reproduce a specific layout while testing.
        ///
        /// Set this rather than writing to the config asset: mutating a ScriptableObject at runtime
        /// persists it in the editor, and the next session would silently reuse the last seed.
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
        /// The server owns the objective layer — where it goes, and every judgement made against it.
        ///
        /// **This is what closes the door leak.** Placement used to be computed from a shared seed
        /// on every client, which put the door's coordinates in the Seeker's process memory — a
        /// culling layer hides it on screen but the WebGL build is decompilable, so that was never
        /// a defence. With the server placing it and filtering per role, the Seeker's copy of the
        /// bulletin simply has no door block in it.
        ///
        /// It gates the *judgements* too, and that is why it is not called `ServerPlacesObjectives`
        /// any more: with it on, `KeyPickup` stops polling (IG-012a) and <see cref="TryInsertKey"/>
        /// refuses (IG-012b3). The server does the same tests against the authoritative positions,
        /// and this client is told the answer.
        ///
        /// Off (no session), this client places the objective itself by calling the same shared
        /// code the server uses (<c>ObjectivePlacement</c>, ADR 0002) and judges it locally. That
        /// keeps the offline practice path alive without a second copy of either.
        /// </summary>
        public bool ServerOwnsObjectives { get; set; }

        /// <summary>
        /// The server owns combat — where the round goes, who it hits, and what that costs.
        ///
        /// Separate from <see cref="ServerOwnsObjectives"/> even though a session sets both, because
        /// the two crossed over in different tasks (IG-012 and IG-014) and for several iterations the
        /// server judged one and not the other. A flag per domain lets that migration happen a piece
        /// at a time; one flag covering both would have had to lie during the gap.
        ///
        /// With it on, <see cref="ReportHit"/> refuses and hits arrive through
        /// <see cref="AcceptCombatState"/> instead. Off (no session), the offline practice path still
        /// resolves its own hits — that is what makes hunting practice runners work solo.
        /// </summary>
        public bool ServerOwnsCombat { get; set; }

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

        /// <summary>
        /// Hands over the balance values and the level. <c>MatchBootstrap</c> is the only caller.
        ///
        /// The level arrives as <see cref="ILevelQuery"/> — what the rules actually need — and is
        /// stored as the Unity object behind it. An implementation that is not a
        /// <see cref="MonoBehaviour"/> cannot be held across a domain reload, so it is refused
        /// loudly rather than dropped.
        /// </summary>
        public void Configure(GameConfig gameConfig, ILevelQuery level)
        {
            if (gameConfig != null) config = gameConfig;
            if (level == null) return;

            if (level is MonoBehaviour behaviour)
            {
                map = behaviour;
                return;
            }

            Debug.LogError($"[Match] {level.GetType().Name} is not a MonoBehaviour, so it cannot be " +
                           "held as the level. ILevelQuery implementations have to be components.");
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
        /// <summary>
        /// 이 매치가 요구하는 열쇠 수. 시작 인원이 정한다
        /// (<c>MatchConstants.KeysRequiredWith</c>).
        ///
        /// **`GameConfig.keysRequired`(상수 10)는 상한이지 요구량이 아니다.** 둘을 섞으면
        /// HUD 가 "2/10" 을 그리는 동안 문은 3개에서 열린다 — 판정은 서버의 것이므로 규칙이
        /// 갈리지는 않지만, 화면이 거짓말을 한다.
        public int KeysRequired { get; private set; } = MatchConstants.KeysRequired;

        /// 이 매치의 요구량을 이미 받아 두었다. 매치 중에 다시 잡지 않기 위한 값이다 —
        /// 사람이 빠질 때마다 다시 구하면 팀이 무너질수록 문이 쉬워진다.
        private bool _keysLatched;

        public void BeginMatch(PlayerAgent seeker)
        {
            if (config == null) config = ScriptableObject.CreateInstance<GameConfig>();

            // **Resolved once and passed down.** It used to be computed here and again inside
            // `PlaceObjectives`, and with the default config (`placementSeed` 0) both fell through to
            // `Environment.TickCount` — which advances between the two reads, so the Random driving
            // teleports and the sequence placing the objective were seeded from *different* numbers.
            // Nothing visibly broke because they feed different things, but "one match, one seed" was
            // not true, and reproducing a layout with `PlacementSeedOverride` only half worked.
            int seed = ResolvePlacementSeed();

            _random = new System.Random(seed);

            // The level's grid is plain managed memory and does not survive a domain reload, while
            // its geometry does. Ask for it back before anything is placed on it.
            Map?.EnsureGrid();

            Outcome = MatchOutcome.None;
            KeysInserted = 0;

            // 오프라인 연습 경로의 요구량. 네트워크 매치에서는 전문이 도착하며 덮는다
            // (`AcceptMatchState`) — 서버가 시작 인원으로 정한 값이 옳고, 이쪽은 그 전까지의
            // 근사다.
            KeysRequired = MatchConstants.KeysRequiredWith(_agents.Count);
            _keysLatched = false;
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

            PlaceObjectives(seed);

            RolesAssigned?.Invoke();
            KeysChanged?.Invoke(KeysInserted, KeysRequired);
            EscapesChanged?.Invoke(Escapes, EscapesNeeded);

            SetPhase(MatchPhase.RoleReveal);
            _phaseTimer = config.roleRevealDuration;
        }

        private void PlaceAgentsAtStart(PlayerAgent seeker)
        {
            ILevelQuery level = Map;
            if (ServerPlacesAgents || level == null || !level.HasGrid) return;

            for (int i = 0; i < _agents.Count; i++)
            {
                PlayerAgent agent = _agents[i];
                if (agent == null) continue;

                if (agent == seeker)
                {
                    agent.TeleportTo(level.SpawnCentre);
                    continue;
                }
                if (level.TryRandomPoint(Rng, out Vector3 point)) agent.TeleportTo(point);
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
            // Networked, the server holds the doorway timer in fixed ticks and tells everyone who
            // got out (IG-012c1/c2). Two clients counting their own hold disagree about the exact
            // moment, and the moment is the whole point — it is what the Seeker can interrupt.
            if (ServerOwnsObjectives) return;

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
            EscapesChanged?.Invoke(Escapes, EscapesNeeded);
            Notify($"{agent.displayName} ESCAPED  ({Escapes}/{EscapesNeeded})");

            EvaluateWinConditions();
        }

        private void EvaluateWinConditions()
        {
            if (Phase != MatchPhase.Playing || !ResolvesOutcome) return;

            if (Escapes >= EscapesNeeded) { EndMatch(MatchOutcome.RunnersEscaped); return; }

            // A match that never had a Runner in it cannot have had them wiped out. This also
            // fails safe after a domain reload, which empties the roster for a moment: the count
            // it is compared against is gone too, so the wipe check simply does not fire.
            if (!config.seekerWinsOnWipe || _runnersAtStart <= 0) return;

            // **탈출은 전멸이 아니다.** `InPlay` 는 죽은 것과 나간 것을 함께 거짓으로 만드는데,
            // 그것으로 세면 마지막 Runner 가 **문으로 걸어 나간 순간** 남은 수가 0 이 되어
            // "술래가 전멸시켰다" 가 뜬다. 2인 매치에서는 그것이 유일한 결말이 된다 —
            // 나간 사람이 진 것으로 적히고, 그것도 나가자마자 즉시.
            //
            // 전멸은 **전원이 쓰러진 것**이다. 한 명이라도 나갔으면 그 판은 다른 이야기다.
            int standing = 0;
            int escaped = 0;

            for (int i = 0; i < _agents.Count; i++)
            {
                PlayerAgent agent = _agents[i];
                if (agent == null || agent.Role != Role.Runner) continue;

                if (agent.Escaped) escaped++;
                else if (agent.InPlay) standing++;
            }

            if (standing == 0 && escaped == 0) EndMatch(MatchOutcome.SeekerWipedRunners);
        }

        /// <summary>
        /// Takes the match phase and clock from the server's bulletin.
        ///
        /// **This is the seam the rules move across.** The server owns the phase transitions and the
        /// clock (it counts in fixed ticks, so two clients no longer disagree about when the reveal
        /// ends), and it owns the objective layer — the keys are applied through
        /// <see cref="AcceptObjectiveProgress"/> and <see cref="AcceptCarriedKeys"/>. What is still
        /// resolved locally is combat and the outcome. Called every frame from <c>MatchSync</c> —
        /// the bulletin arrives at 2 Hz and carries the clock, so there is no "changed" signal worth
        /// subscribing to.
        ///
        /// **Escapes and the outcome are deliberately not applied.** The server carries fields for
        /// them but does not count them yet, so it sends zeros — writing those in would reset a HUD
        /// that this client had correctly counted, and the symptom would read as the objective
        /// resetting itself. They start being applied when the server actually judges them.
        ///
        /// **<c>Ended</c> is not applied here either.** Ending a match means publishing an outcome,
        /// and that path already exists (<see cref="AcceptOutcome"/>, driven by the room bulletin).
        /// Taking the phase alone would move the HUD to a result screen with no result in it.
        /// </summary>
        public void AcceptMatchState(
            NV.Shared.Contracts.Enums.MatchPhase serverPhase,
            float secondsRemaining,
            int participantCount)
        {
            if (Phase == MatchPhase.Lobby || Phase == MatchPhase.Ended) return;

            // **한 매치에 한 번만 잡는다.** 명단은 사람이 빠지면 줄어드는데, 그것을 따라가면
            // 팀이 무너질수록 문이 쉬워진다. 서버도 시작 인원으로 한 번 정하고 고정한다.
            //
            // 이 값은 **화면에 그리기 위한 것**이다. 문이 열리는 판정은 서버의 것이고
            // (`ObjectiveState` 의 doorOpen), 여기서 어긋나도 규칙이 갈리지는 않는다 —
            // 다만 HUD 가 잘못된 분모를 그린다.
            if (!_keysLatched && participantCount > 0)
            {
                KeysRequired = MatchConstants.KeysRequiredWith(participantCount);
                _keysLatched = true;
                KeysChanged?.Invoke(KeysInserted, KeysRequired);
            }

            // The clock is the server's. The local countdown in Update still runs between
            // bulletins — at 2 Hz the HUD would tick visibly otherwise — but every bulletin
            // overwrites whatever it drifted to.
            TimeRemaining = Mathf.Max(0f, secondsRemaining);

            switch (serverPhase)
            {
                case NV.Shared.Contracts.Enums.MatchPhase.RoleReveal:
                    SetPhase(MatchPhase.RoleReveal);
                    break;

                case NV.Shared.Contracts.Enums.MatchPhase.Playing:
                    // The reveal ends on the server's tick, not on this machine's frame rate.
                    SetPhase(MatchPhase.Playing);
                    break;
            }
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
            _keysLatched = false;
            Outcome = MatchOutcome.None;
            SetPhase(MatchPhase.Lobby);
        }

        private void EndMatch(MatchOutcome outcome)
        {
            Outcome = outcome;
            SetPhase(MatchPhase.Ended);
            MatchEnded?.Invoke(outcome);
            Debug.Log($"[Match] Ended: {outcome}. Keys {KeysInserted}/{KeysRequired}, escapes {Escapes}.");
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
            // **This is the seam R-3.1 was about.** `Bullet` flies on the shooter's machine only,
            // so `SendMessageUpwards("OnHit")` lands here having been decided by the one client with
            // an interest in the answer. Networked, the server flies the round against the
            // authoritative positions and tells everyone (IG-014a/b); this refuses so that a client
            // saying "I hit you" is a client saying nothing at all.
            //
            // Refusing here rather than in `PlayerAgent.OnHit` keeps the rule in the one place that
            // decides rules, and leaves `Bullet` as pure presentation without touching it.
            if (ServerOwnsCombat) return;

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

        /// <summary>
        /// The server's verdict on one body: how many hits it has taken, whether it is bleeding, and
        /// whether it is down. Polled every frame from <c>MatchSync</c>.
        ///
        /// **The notifications survive here, unlike the key inserts.** The bulletin says *who* was
        /// hit, so "X HIT" is information this client legitimately has — what it cannot say is when,
        /// which is why the messages fire on a *transition* and not on every poll.
        ///
        /// Bleeding is applied only on change. <see cref="PlayerAgent.SetBleeding"/> starts a
        /// <c>BloodTrail</c>, and calling it every frame restarts the trail every frame — the symptom
        /// is a wounded Runner leaving no trail at all.
        /// </summary>
        public void AcceptCombatState(PlayerAgent agent, int hits, bool bleeding, bool downed)
        {
            if (agent == null) return;

            bool wounded = hits > agent.Hits;
            agent.SetHits(hits);

            if (bleeding != agent.Bleeding) agent.SetBleeding(bleeding);

            if (downed && agent.Alive)
            {
                agent.Kill();
                Notify($"{agent.displayName} DOWN");
                AgentHit?.Invoke(agent, true);

                // 쓰러뜨린 탄에도 마커를 띄운다. 이 갈래가 먼저 돌아가므로 아래의 부상
                // 갈래에만 두면 **마지막 한 발만 아무 반응이 없다** — 가장 중요한 한 발이다.
                MarkHitForSeeker(agent);
                return;
            }

            if (!wounded) return;

            Notify(agent.isLocalPlayer ? "HIT — BLEEDING. KEEP MOVING" : $"{agent.displayName} HIT");
            AgentHit?.Invoke(agent, false);

            MarkHitForSeeker(agent);
        }

        /// <summary>
        /// Tells the Seeker their round connected, from the server's count rather than from their
        /// own bullet.
        ///
        /// **The local round cannot answer this.** It flies on the shooter's machine and remote
        /// bodies carry nothing for it to hit — the `CharacterController` is disabled so it does not
        /// shove the local player, and the blocks never had colliders. So a shot that hits somebody
        /// actually stops on the wall behind them, and the marker fired either way. A marker that
        /// fires on every shot is not feedback.
        ///
        /// The cost is the bulletin's half second. That is the right trade here: with three rounds
        /// and a chain waiting at the end of them, *whether* you hit is worth much more than knowing
        /// it 300 ms sooner. It goes away on its own once the server's own bullets are drawn.
        /// </summary>
        private void MarkHitForSeeker(PlayerAgent victim)
        {
            if (!ServerOwnsCombat) return;

            PlayerAgent local = LocalAgent;

            // 맞은 사람에게는 띄우지 않는다. 자기가 맞은 것은 상처 비네트가 이미 말한다.
            if (local == null || local == victim || local.Role != Role.Seeker) return;

            var crosshair = local.GetComponent<Crosshair>();
            if (crosshair != null) crosshair.ShowHitMarker();
        }

        /// <summary>
        /// The server's magazine count for this agent (IG-028).
        ///
        /// **Only the Seeker's own copy carries a number.** The codec zeroes ammo for every other
        /// recipient, so applying what arrives is also what keeps a Runner from reading the Seeker's
        /// magazine — the gunshot is how this game tells them a round was spent.
        ///
        /// Silent when the agent has no weapon: a Runner does not carry one, and a zero would
        /// otherwise be indistinguishable from "empty".
        /// </summary>
        public void AcceptAmmo(PlayerAgent agent, int rounds)
        {
            if (agent == null || agent.Role != Role.Seeker) return;

            var weapon = agent.GetComponent<WeaponController>();
            if (weapon == null) return;

            weapon.AcceptAmmo(rounds);
        }

        /// <summary>
        /// The server has this body on the chain, or has let it go (IG-028).
        ///
        /// **The empty magazine cannot be the signal on a client.** Networked, the magazine is the
        /// server's and <see cref="AcceptAmmo"/> overwrites the local count every frame from a 2 Hz
        /// bulletin, so the local <c>Fire</c> that used to raise the chain never sees zero — the
        /// Seeker was dragged to the altar by the server with no chain drawn anywhere. The snapshot's
        /// <c>Frozen</c> bit is what actually says it, and it says it about every body, so a remote
        /// player's haul is visible too.
        ///
        /// **The role gate is not decoration.** The server folds the chain and the match-wide
        /// movement lock into that one bit, and this client learns the match phase half a second
        /// late; without it, the tick where the match ends would briefly chain everybody.
        /// </summary>
        public void AcceptChained(PlayerAgent agent, bool chained)
        {
            if (agent == null) return;

            var chain = agent.GetComponent<ChainDrag>();
            if (chain == null) return;

            if (chained && agent.Role != Role.Seeker) return;

            chain.SetServerChained(chained);
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
            ILevelQuery level = Map;
            if (agent == null || level == null || !level.HasGrid) return;

            if (level.TryRandomPoint(Rng, out Vector3 point))
            {
                agent.TeleportTo(point);
                return;
            }
            if (level.TryNearestStandablePoint(agent.FeetPosition, out Vector3 fallback))
                agent.TeleportTo(fallback);
        }

        // ============================================================ objective

        /// <summary>
        /// A Runner walked over a key. Carrying is capped only if the config says so — the default
        /// is uncapped, which makes one Runner hoarding keys a real (and punishable) strategy.
        /// </summary>
        public bool TryPickUpKey(PlayerAgent agent, KeyPickup key)
        {
            // Networked, the server polls the same distance test against the authoritative positions
            // and the key leaves the objective bulletin (IG-012a). `KeyPickup` already stops calling
            // this, so today there is no live path in — the refusal is here anyway, because every
            // other rule refuses in this class and a public method that only *happens* to be
            // unreachable is one new caller away from being a second judge.
            if (ServerOwnsObjectives) return false;

            if (Phase != MatchPhase.Playing || agent == null || key == null || key.Collected) return false;
            if (agent.Role != Role.Runner || !agent.InPlay || !agent.collectsKeys) return false;
            if (config.carryLimit > 0 && agent.CarriedKeys >= config.carryLimit) return false;

            agent.AddKeys(1);
            _keys.Remove(key);
            key.Collect();

            if (agent.isLocalPlayer) Notify($"KEY  ({agent.CarriedKeys} carried)");
            KeysChanged?.Invoke(KeysInserted, KeysRequired);
            return true;
        }

        /// <summary>
        /// The server's count of what this agent is carrying. Networked, `TryPickUpKey` never runs —
        /// `KeyPickup` stops polling and the server does the same distance test against the
        /// authoritative positions, so the count arrives instead of being reached.
        ///
        /// The notification is raised here rather than by the caller because it is the same feedback
        /// `TryPickUpKey` gives, and the local player should not be able to tell which side counted.
        /// It fires on an *increase* only: the match bulletin repeats at 2 Hz, so reacting to every
        /// message would print "KEY" twice a second for the rest of the match.
        ///
        /// A Seeker's copy of the bulletin carries zeroes for everyone, by design. Applying them is
        /// correct — a Seeker who knows who is holding nine keys knows who to hunt.
        /// </summary>
        public void AcceptCarriedKeys(PlayerAgent agent, int count)
        {
            if (agent == null) return;

            int before = agent.CarriedKeys;
            agent.SetCarriedKeys(count);

            if (agent.CarriedKeys > before && agent.isLocalPlayer)
                Notify($"KEY  ({agent.CarriedKeys} carried)");
        }

        /// <summary>
        /// The server's objective progress: how many keys are in, and whether the door is open.
        ///
        /// Both are *told*, not reached — the count comes from the match bulletin and the door's
        /// state from the objective one, and neither is something this client works out.
        ///
        /// **A Seeker receives zero and a closed door, by design.** The codec blanks the count and
        /// omits the door block entirely, so applying what arrives is what keeps the Seeker ignorant
        /// of the objective's progress. There is no door object on that client to open either.
        ///
        /// **No per-key notification.** `TryInsertKey` said "KEY IN 7/10" to whoever inserted, but
        /// the bulletin does not say *who* did — raising it for everyone would tell each Runner
        /// something the offline game never told them. The HUD's key slots already show progress
        /// through <see cref="KeysChanged"/>; the door opening is match-wide news and does notify.
        /// </summary>
        public void AcceptObjectiveProgress(int keysInserted, bool doorOpen)
        {
            int clamped = Mathf.Clamp(keysInserted, 0, KeysRequired);

            if (clamped != KeysInserted)
            {
                KeysInserted = clamped;
                KeysChanged?.Invoke(KeysInserted, KeysRequired);
            }

            if (!doorOpen || _door == null || _door.IsOpen) return;

            _door.Open();
            Notify("THE DOOR IS OPEN");
        }

        /// <summary>
        /// The server's escape count. **Both roles receive the real number** — unlike the key
        /// progress, this one is not filtered: it is what the Seeker has to stop.
        /// </summary>
        /// <summary>
        /// Somebody is standing in the doorway right now, and how far along they are (0..1).
        ///
        /// **Everyone gets this, the Seeker included** — the ruleset says so. The door's position
        /// stays hidden from the Seeker, but the fact that a Runner is leaving *this second* is the
        /// only thing that makes the hold interruptible; a hold nobody can see is not a rule, it is
        /// a delay. It rides the per-tick snapshot rather than the 2 Hz bulletin because the hold is
        /// 0.8 s long and two samples cannot draw a bar you are meant to react to.
        ///
        /// The highest of everyone's, because what matters is whether *anybody* is about to get out.
        /// </summary>
        public float EscapeProgress { get; private set; }

        public void AcceptEscapeProgress(float progress)
        {
            EscapeProgress = Mathf.Clamp01(progress);
        }

        public void AcceptEscapes(int escapes)
        {
            int clamped = Mathf.Max(0, escapes);
            if (clamped == Escapes) return;

            Escapes = clamped;
            EscapesChanged?.Invoke(Escapes, EscapesNeeded);
        }

        /// <summary>
        /// This agent got out. The server decided it; here it only takes effect on the body.
        ///
        /// **The notification carries no count.** It arrives from the snapshot, which runs at the
        /// tick rate, while <see cref="AcceptEscapes"/> arrives on the 2 Hz bulletin — so at this
        /// moment the count is up to half a second behind and would read "0/2" for a Runner who
        /// just left. The HUD's escape counter shows the authoritative number; a transient message
        /// does not need to duplicate it badly.
        /// </summary>
        public void AcceptEscaped(PlayerAgent agent)
        {
            if (agent == null || agent.Escaped) return;

            agent.MarkEscaped();
            Notify($"{agent.displayName} ESCAPED");
        }

        /// <summary>
        /// One key into the door. Inserts are serialised through this method, which is what makes
        /// the "two Runners insert the tenth key at the same instant" case a non-event: whichever
        /// call arrives first crosses the threshold, and the second finds the door already open.
        /// </summary>
        public bool TryInsertKey(PlayerAgent agent, EscapeDoor door)
        {
            // Networked, the server judges this from the `Interact` bit and tells everyone the
            // count (IG-012b2/b3). Refusing here rather than in the caller keeps the decision in
            // the one place that decides rules — and `EscapeDoor.Interact` stays a request, which
            // is what it always was.
            //
            // The prompt still appears. What the player may do has not changed; who decides it has.
            if (ServerOwnsObjectives) return false;

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

            KeysChanged?.Invoke(KeysInserted, KeysRequired);

            if (KeysInserted >= KeysRequired)
            {
                door.Open();
                Notify("THE DOOR IS OPEN");
            }
            else
            {
                Notify($"KEY IN  {KeysInserted}/{KeysRequired}");
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

        /// <summary>
        /// Puts the objective in the level.
        ///
        /// **Where the coordinates come from is the only thing that differs between online and
        /// offline.** With a session, the server has already placed everything and this waits for
        /// the bulletin (<see cref="AcceptObjectiveState"/>). Without one, it calls the same shared
        /// placement the server calls — one algorithm, two callers (ADR 0002). Building the objects
        /// is common to both, so a change to how a key looks cannot diverge between the two paths.
        /// </summary>
        /// <summary>
        /// Which seed this match's randomness comes from, in priority order: the test override, then
        /// the config asset, then this machine's clock.
        ///
        /// **Call it once per match.** The clock fallback returns a different number every time, so
        /// two calls in one `BeginMatch` produce two matches' worth of randomness — that is the bug
        /// this method exists to make unrepresentable.
        ///
        /// Networked, none of this reaches the objective: the server places it and sends coordinates
        /// (IG-011). This seeds the *offline* layout and, in both modes, the local Random behind the
        /// practice runners and the key scatter.
        /// </summary>
        private int ResolvePlacementSeed()
        {
            if (PlacementSeedOverride != 0) return PlacementSeedOverride;
            if (config != null && config.placementSeed != 0) return config.placementSeed;

            return Environment.TickCount;
        }

        private void PlaceObjectives(int seed)
        {
            ClearObjectives();

            if (ServerOwnsObjectives)
            {
                // The bulletin arrives on its own schedule (5 s + on change) and may already be
                // here. Nothing to do if it is not — AcceptObjectiveState builds when it lands.
                return;
            }

            if (Map == null || !Map.HasGrid)
            {
                Debug.LogWarning("[Match] No level to place the objective in.");
                return;
            }

            NV.Shared.Collision.MapGrid grid = OfflineGrid();
            if (grid == null || grid.FreeFloorCount == 0)
            {
                Debug.LogWarning("[Match] The level has no walkable grid; nothing can be placed.");
                return;
            }

            var sequence = new NV.Shared.Simulation.DeterministicSequence(seed);
            NV.Shared.Simulation.ObjectivePlacement.PlaceObjectives(_placement, grid, ref sequence);

            BuildObjectiveObjects(_placement, hasDoor: true);
        }

        /// <summary>
        /// Takes the objective's coordinates from the server's bulletin.
        ///
        /// <paramref name="hasDoor"/> is false on the Seeker's client — the server left the door
        /// block out of that copy entirely, so there is nothing to build and nothing to hide.
        /// </summary>
        public void AcceptObjectiveState(NV.Shared.Simulation.Objectives placement, bool hasDoor)
        {
            if (placement == null || !placement.Placed) return;

            // Rebuilt rather than diffed. The bulletin is idempotent and arrives rarely, and the
            // objects it makes are cheap — a diff would have to match keys by position, which is
            // exactly the comparison that breaks when two keys share a cell.
            ClearObjectives();
            BuildObjectiveObjects(placement, hasDoor);
        }

        /// <summary>
        /// Makes the GameObjects for a placement. Shared by the online and offline paths.
        /// </summary>
        private void BuildObjectiveObjects(NV.Shared.Simulation.Objectives placement, bool hasDoor)
        {
            _objectiveRoot = new GameObject("__Objectives").transform;
            _objectiveRoot.SetParent(transform, false);

            ChainAltar.Spawn(ToUnity(placement.AltarPosition), ToUnity(placement.AltarDragPoint), _objectiveRoot);

            if (hasDoor)
            {
                float yawDegrees = placement.DoorYaw * Mathf.Rad2Deg;
                _door = EscapeDoor.Spawn(
                    ToUnity(placement.DoorPosition),
                    Quaternion.Euler(0f, yawDegrees, 0f),
                    _objectiveRoot);
            }

            for (int i = 0; i < placement.Keys.Count; i++)
                _keys.Add(KeyPickup.Spawn(ToUnity(placement.Keys[i]), _objectiveRoot));

            for (int i = 0; i < placement.Devices.Count; i++)
            {
                var device = placement.Devices[i];

                MapDevice spawned = MapDevice.Spawn(
                    (DeviceType)device.Type,
                    ToUnity(device.Position),
                    device.Yaw * Mathf.Rad2Deg,
                    _objectiveRoot);

                DeviceSystem.Instance?.Register(spawned);
            }
        }

        /// <summary>
        /// The walkability grid for offline placement, built from the level the same way the export
        /// does — so the offline objective lands where the server would have put it.
        ///
        /// <c>MapExport.BuildMapData</c> already fills <c>FreeFloor</c> from the collision boxes,
        /// which is the one part of this that Unity physics used to answer.
        /// </summary>
        private NV.Shared.Collision.MapGrid OfflineGrid()
        {
            // Cached: this runs at every match start, and building it walks every grid cell against
            // every collision box. `BackroomsMapGenerator.Generate` invalidates the cache, so a
            // regenerated level is not served a stale grid.
            // The level answers the rules through ILevelQuery and the export through
            // INetworkMapSource. Both are on the same component; a level that offers only the
            // former has no grid to export and so has nothing to place against either.
            var data = NV.Client.Net.MapExport.BuildMapDataCached(map as NV.Client.Net.INetworkMapSource);
            return data != null && data.HasGrid ? new NV.Shared.Collision.MapGrid(data.Grid) : null;
        }

        private static Vector3 ToUnity(System.Numerics.Vector3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }

        // The placement helpers that used to live here — PlaceChainAltar, TryFindLandingSpot,
        // IsFreeFloor, PlaceDevices, TryFindSpacedPoint, IsClearOfPlacements — moved into
        // NV.Shared.Simulation.ObjectivePlacement (ADR 0002). The server calls the same code, so
        // there is no second copy of the algorithm to drift.
        //
        // IsFreeFloor is worth a note: it used Physics.CheckCapsule to reject stairwell cells that
        // the grid called walkable. The shared version answers the same question with the collision
        // boxes and the *server's* player box instead, which is both available without Unity physics
        // and the right size — the old 0.32 m capsule was narrower than the server's 0.4 m box, so
        // it passed cells the server would have pushed a player out of.

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
                    if (!Map.TryNearestStandablePoint(around + offset, out point)) point = around;
                }
                else if (!Map.TryRandomPoint(Rng, out point))
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

            Map?.SetWallTransparency(1f);
        }

        public void Notify(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Notified?.Invoke(message);
        }
    }
}
