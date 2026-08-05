using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;


// 캐릭터 표는 새 로비(NV.Client.Lobby)로 옮겼다. 이 파일들은 서버 연동 대기방으로
// 대체되며 함께 지워진다 — NVserver/docs/game-lobby-plan.md 7.1 절.
using NV.Client.Lobby;

namespace NV.Lobby
{
    /// <summary>
    /// All of the lobby's logic and none of its networking.
    ///
    /// The split that matters: a method named <c>Request*</c> is what a *client* calls — it only
    /// ever posts to <see cref="ILobbyTransport"/>. A method named <c>Handle*</c> runs on the
    /// **authority** and is the only place a decision is made. A method named <c>Apply*</c> runs on
    /// every client and only writes down what the authority already decided. Nothing in this class
    /// knows whether the transport underneath it is a network or a loopback.
    ///
    /// Read <c>.claude/skills/lobby-builder/references/netcode-integration.md</c> before the server
    /// pass. Every integration point below is tagged <c>// NETCODE:</c> and that tag set is the
    /// authoritative checklist — do not delete one, move it with the code.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public sealed class LobbyManager : MonoBehaviour
    {
        private static LobbyManager _instance;

        /// <summary>Lazily re-found: a domain reload during play wipes statics without re-running Awake.</summary>
        public static LobbyManager Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<LobbyManager>();
                return _instance;
            }
            private set => _instance = value;
        }

        // --- events the view layer listens to. This list is also what a replication layer carries.
        public event Action<LobbyState> StateChanged;
        public event Action RosterChanged;                 // join, leave, slot move, ready, dress-up
        public event Action<float> CountdownChanged;
        public event Action<string> Notified;
        public event Action<int, int> SwapRequestReceived; // requestId, from playerId
        public event Action MatchStarting;

        private ILobbyTransport _transport;
        private LobbyConfig _config;

        private readonly List<LobbyPlayer> _players = new List<LobbyPlayer>();
        private readonly List<PendingSwap> _swaps = new List<PendingSwap>();

        private LobbyState _state = LobbyState.Waiting;
        private float _countdown;
        private int _nextSwapId = 1;
        private int _nextLocalId = 1;

        public LobbyConfig Config => _config;
        public LobbyState State => _state;
        public float Countdown => _countdown;
        public IReadOnlyList<LobbyPlayer> Players => _players;
        public int LocalPlayerId => _transport?.LocalPlayerId ?? 1;

        /// <summary>Input is refused past the lock — the one rule the whole lobby hangs off.</summary>
        public bool InputLocked => _state == LobbyState.Locked || _state == LobbyState.Starting;

        /// <summary>
        /// Has anyone handed this manager a transport yet? A domain reload during play wipes both
        /// the transport and the config while leaving the component running, so every entry point
        /// below checks rather than throwing a NullReference once per click for the rest of the
        /// session.
        /// </summary>
        public bool IsConfigured => _transport != null && _config != null;

        private struct PendingSwap
        {
            public int id;
            public int fromPlayerId;
            public int toPlayerId;
            public float expiresAt;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_transport == null) return;

            _transport.RequestReceived -= HandleRequest;
            _transport.EventReceived -= ApplyEvent;
        }

        public void Configure(LobbyConfig config, ILobbyTransport transport)
        {
            _config = config;
            _transport = transport;

            _transport.RequestReceived += HandleRequest;
            _transport.EventReceived += ApplyEvent;
        }

        /// <summary>Walks the local player into the room. Everyone else arrives through the transport.</summary>
        public void JoinLocalPlayer(string displayName)
        {
            if (!IsConfigured) return;
            _transport.Submit(new LobbyRequest
            {
                kind = LobbyRequestKind.Join,
                playerId = _transport.LocalPlayerId,
                stringA = displayName,
                boolValue = false,
            });
        }

        private void Update()
        {
            if (_transport == null) return;

            _transport.Tick(Time.deltaTime);
            if (_transport is OfflineLobbyTransport offline)
                offline.TickBotDecisions(id => Find(id)?.isReady ?? false);

            // NETCODE: server only. On a client this whole block must not run — the countdown is
            // replicated, not simulated locally. A client-run countdown is the classic source of
            // "the match started at different times for different players".
            if (!_transport.IsAuthority) return;

            TickSwapTimeouts();
            EvaluateCountdown();
            TickCountdown();
        }

        // ============================================================ client → authority

        /// <summary>NETCODE: becomes a client→server RPC. The server sets the state and broadcasts it.</summary>
        public void RequestReady(bool ready)
        {
            if (!IsConfigured) return;
            if (InputLocked) { Notify("LOCKED IN"); return; }
            _transport.Submit(new LobbyRequest
            {
                kind = LobbyRequestKind.SetReady,
                playerId = LocalPlayerId,
                boolValue = ready,
            });
        }

        /// <summary>NETCODE: the server must resolve two clients claiming one slot in the same tick.</summary>
        public void RequestMoveToSlot(int slotIndex)
        {
            if (!IsConfigured) return;
            if (InputLocked) { Notify("LOCKED IN"); return; }
            _transport.Submit(new LobbyRequest
            {
                kind = LobbyRequestKind.MoveToSlot,
                playerId = LocalPlayerId,
                intValue = slotIndex,
            });
        }

        /// <summary>NETCODE: send only to the two players involved, not to the whole room.</summary>
        public void RequestSwapWith(int targetPlayerId)
        {
            if (!IsConfigured) return;
            if (InputLocked) { Notify("LOCKED IN"); return; }
            _transport.Submit(new LobbyRequest
            {
                kind = LobbyRequestKind.RequestSwap,
                playerId = LocalPlayerId,
                intValue = targetPlayerId,
            });
        }

        public void RespondToSwap(int requestId, bool accept)
        {
            if (!IsConfigured) return;
            _transport.Submit(new LobbyRequest
            {
                kind = LobbyRequestKind.RespondToSwap,
                playerId = LocalPlayerId,
                intValue = requestId,
                boolValue = accept,
            });
        }

        /// <summary>
        /// NETCODE: the server validates the id against the catalog, checks the lock, **and checks
        /// that nobody else is already wearing it**. Two clients can pick the same character in the
        /// same tick and only one of them can have it.
        /// </summary>
        public void RequestCharacter(string characterId)
        {
            if (!IsConfigured) return;
            if (InputLocked) { Notify("LOCKED IN"); return; }
            _transport.Submit(new LobbyRequest
            {
                kind = LobbyRequestKind.SetCharacter,
                playerId = LocalPlayerId,
                stringA = characterId,
            });
        }

        // ============================================================ authority

        /// <summary>
        /// The only place a lobby decision is made. Everything here assumes it is running on the
        /// server; the offline transport just happens to be the server as well.
        /// </summary>
        private void HandleRequest(LobbyRequest request)
        {
            if (!_transport.IsAuthority) return;

            switch (request.kind)
            {
                case LobbyRequestKind.Join: HandleJoin(request); break;
                case LobbyRequestKind.Leave: HandleLeave(request.playerId); break;
                case LobbyRequestKind.SetReady: HandleReady(request); break;
                case LobbyRequestKind.MoveToSlot: HandleMove(request); break;
                case LobbyRequestKind.RequestSwap: HandleSwapRequest(request); break;
                case LobbyRequestKind.RespondToSwap: HandleSwapResponse(request); break;
                case LobbyRequestKind.SetCharacter: HandleCharacter(request); break;
            }
        }

        private void HandleJoin(LobbyRequest request)
        {
            // NETCODE: reject joins once locked; before the lock, a join cancels the countdown
            // because "everyone is ready" has just stopped being true.
            if (InputLocked) { Reject(request.playerId, "LOBBY LOCKED"); return; }
            if (Find(request.playerId) != null) return;

            int slot = FirstFreeSlot();
            if (slot < 0) { Reject(request.playerId, "LOBBY FULL"); return; }

            // Everyone arrives already wearing something nobody else has. With eight characters and
            // at most eight stands there is always one free.
            string character = FirstFreeCharacter();

            Broadcast(new LobbyEvent
            {
                kind = LobbyEventKind.PlayerJoined,
                playerId = request.playerId,
                intValue = slot,
                boolValue = request.boolValue,
                stringA = request.stringA,
                stringB = character,
            });
        }

        private void HandleLeave(int playerId)
        {
            if (Find(playerId) == null) return;
            CancelSwapsInvolving(playerId);
            Broadcast(new LobbyEvent { kind = LobbyEventKind.PlayerLeft, playerId = playerId });
        }

        private void HandleReady(LobbyRequest request)
        {
            if (InputLocked) { Reject(request.playerId, "LOCKED IN"); return; }

            LobbyPlayer player = Find(request.playerId);
            if (player == null || player.isReady == request.boolValue) return;

            Broadcast(new LobbyEvent
            {
                kind = LobbyEventKind.ReadyChanged,
                playerId = request.playerId,
                boolValue = request.boolValue,
            });
        }

        private void HandleMove(LobbyRequest request)
        {
            if (InputLocked) { Reject(request.playerId, "LOCKED IN"); return; }

            LobbyPlayer player = Find(request.playerId);
            if (player == null) return;

            int slot = request.intValue;
            if (slot < 0 || slot >= _config.maxPlayers) return;
            if (slot == player.slotIndex) return;

            // NETCODE: this is the conflict the server exists to resolve. Two clients can ask for
            // the same empty slot in the same tick; whichever is processed first wins and the other
            // gets an explicit rejection so its UI can revert rather than quietly disagreeing.
            if (Occupant(slot) != null) { Reject(request.playerId, "SLOT TAKEN"); return; }

            Broadcast(new LobbyEvent
            {
                kind = LobbyEventKind.SlotChanged,
                playerId = request.playerId,
                intValue = slot,
            });
        }

        private void HandleSwapRequest(LobbyRequest request)
        {
            if (InputLocked) { Reject(request.playerId, "LOCKED IN"); return; }
            if (_config.swapMode != SlotSwapMode.SwapRequest) return;

            LobbyPlayer from = Find(request.playerId);
            LobbyPlayer to = Find(request.intValue);
            if (from == null || to == null || from == to) return;

            foreach (PendingSwap existing in _swaps)
                if (existing.fromPlayerId == from.id || existing.toPlayerId == from.id) return;

            var swap = new PendingSwap
            {
                id = _nextSwapId++,
                fromPlayerId = from.id,
                toPlayerId = to.id,
                expiresAt = Time.time + _config.swapRequestTimeout,
            };
            _swaps.Add(swap);

            // NETCODE: send this to the two players involved only. Broadcasting a private
            // negotiation to the whole room is both noise and an information leak.
            Broadcast(new LobbyEvent
            {
                kind = LobbyEventKind.SwapRequested,
                playerId = to.id,
                intValue = swap.id,
                stringA = from.displayName,
                floatValue = from.id,
            });
        }

        private void HandleSwapResponse(LobbyRequest request)
        {
            int index = _swaps.FindIndex(s => s.id == request.intValue);
            if (index < 0) return;

            PendingSwap swap = _swaps[index];
            _swaps.RemoveAt(index);

            // Only the player who was asked may answer.
            if (swap.toPlayerId != request.playerId) return;

            bool accepted = request.boolValue && !InputLocked;
            LobbyPlayer from = Find(swap.fromPlayerId);
            LobbyPlayer to = Find(swap.toPlayerId);

            if (accepted && from != null && to != null)
            {
                int fromSlot = from.slotIndex;
                Broadcast(new LobbyEvent
                {
                    kind = LobbyEventKind.SlotChanged, playerId = from.id, intValue = to.slotIndex,
                });
                Broadcast(new LobbyEvent
                {
                    kind = LobbyEventKind.SlotChanged, playerId = to.id, intValue = fromSlot,
                });
            }

            Broadcast(new LobbyEvent
            {
                kind = LobbyEventKind.SwapResolved,
                playerId = swap.fromPlayerId,
                intValue = swap.toPlayerId,
                boolValue = accepted,
            });
        }

        private void HandleCharacter(LobbyRequest request)
        {
            if (InputLocked) { Reject(request.playerId, "LOCKED IN"); return; }

            LobbyPlayer player = Find(request.playerId);
            if (player == null) return;

            // NETCODE: never trust the client for either of these. An invalid id must be rejected,
            // and so must one that is already taken — this is the same class of conflict as two
            // players claiming one stand, and it is resolved the same way: first processed wins.
            if (!LobbyCharacterCatalog.IsValid(request.stringA)) return;
            if (player.characterId == request.stringA) return;

            LobbyPlayer owner = WearerOf(request.stringA);
            if (owner != null && owner.id != player.id)
            {
                Reject(request.playerId, "TAKEN BY " + owner.displayName);
                return;
            }

            Broadcast(new LobbyEvent
            {
                kind = LobbyEventKind.CharacterChanged,
                playerId = request.playerId,
                stringA = request.stringA,
            });
        }

        /// <summary>NETCODE: server only. Clients must never evaluate "everyone is ready" themselves.</summary>
        private void EvaluateCountdown()
        {
            bool everyoneReady = _players.Count >= _config.minPlayers;
            for (int i = 0; i < _players.Count && everyoneReady; i++)
                if (!_players[i].isReady) everyoneReady = false;

            if (everyoneReady && _state == LobbyState.Waiting)
            {
                _countdown = _config.countdownSeconds;
                Broadcast(new LobbyEvent { kind = LobbyEventKind.StateChanged, intValue = (int)LobbyState.CountingDown });
                Broadcast(new LobbyEvent { kind = LobbyEventKind.CountdownChanged, floatValue = _countdown });
                return;
            }

            // Anyone un-readying cancels it immediately — but only before the lock.
            if (!everyoneReady && _state == LobbyState.CountingDown)
            {
                _countdown = 0f;
                CancelAllSwaps();
                Broadcast(new LobbyEvent { kind = LobbyEventKind.StateChanged, intValue = (int)LobbyState.Waiting });
                Broadcast(new LobbyEvent { kind = LobbyEventKind.CountdownChanged, floatValue = 0f });
            }
        }

        /// <summary>
        /// NETCODE: the server ticks this; clients display a replicated value. Replicate a
        /// **start timestamp + duration** rather than this float — a per-frame float is wasteful
        /// and jitters, and clients can derive the remainder exactly.
        /// </summary>
        private void TickCountdown()
        {
            if (_state != LobbyState.CountingDown && _state != LobbyState.Locked) return;

            _countdown -= Time.deltaTime;
            Broadcast(new LobbyEvent { kind = LobbyEventKind.CountdownChanged, floatValue = Mathf.Max(0f, _countdown) });

            if (_state == LobbyState.CountingDown && _countdown <= _config.lockAtSeconds)
            {
                CancelAllSwaps();
                Broadcast(new LobbyEvent { kind = LobbyEventKind.StateChanged, intValue = (int)LobbyState.Locked });
                return;
            }

            if (_countdown > 0f) return;

            Broadcast(new LobbyEvent { kind = LobbyEventKind.StateChanged, intValue = (int)LobbyState.Starting });
        }

        private void TickSwapTimeouts()
        {
            for (int i = _swaps.Count - 1; i >= 0; i--)
            {
                if (Time.time < _swaps[i].expiresAt) continue;

                PendingSwap lapsed = _swaps[i];
                _swaps.RemoveAt(i);
                Broadcast(new LobbyEvent
                {
                    kind = LobbyEventKind.SwapResolved,
                    playerId = lapsed.fromPlayerId,
                    intValue = lapsed.toPlayerId,
                    boolValue = false,
                });
            }
        }

        private void CancelAllSwaps()
        {
            for (int i = 0; i < _swaps.Count; i++)
                Broadcast(new LobbyEvent
                {
                    kind = LobbyEventKind.SwapResolved,
                    playerId = _swaps[i].fromPlayerId,
                    intValue = _swaps[i].toPlayerId,
                    boolValue = false,
                });
            _swaps.Clear();
        }

        private void CancelSwapsInvolving(int playerId)
        {
            for (int i = _swaps.Count - 1; i >= 0; i--)
                if (_swaps[i].fromPlayerId == playerId || _swaps[i].toPlayerId == playerId)
                    _swaps.RemoveAt(i);
        }

        private void Reject(int playerId, string reason)
        {
            Broadcast(new LobbyEvent
            {
                kind = LobbyEventKind.Rejected,
                playerId = playerId,
                stringA = reason,
            });
        }

        private void Broadcast(LobbyEvent lobbyEvent) => _transport.Broadcast(lobbyEvent);

        // ============================================================ every client

        /// <summary>
        /// Writes down what the authority decided. Nothing here questions it, and nothing here
        /// decides anything — that asymmetry is what makes the networked version safe.
        /// </summary>
        private void ApplyEvent(LobbyEvent e)
        {
            switch (e.kind)
            {
                case LobbyEventKind.PlayerJoined:
                {
                    if (Find(e.playerId) != null) return;

                    var player = new LobbyPlayer
                    {
                        id = e.playerId,
                        displayName = string.IsNullOrEmpty(e.stringA) ? "PLAYER " + e.playerId : e.stringA,
                        slotIndex = e.intValue,
                        isBot = e.boolValue,
                        isLocal = e.playerId == _transport.LocalPlayerId,
                    };
                    player.characterId = e.stringB;
                    _players.Add(player);

                    Notify(player.displayName + " ARRIVED");
                    RosterChanged?.Invoke();
                    break;
                }

                case LobbyEventKind.PlayerLeft:
                {
                    int index = _players.FindIndex(p => p.id == e.playerId);
                    if (index < 0) return;
                    Notify(_players[index].displayName + " LEFT");
                    _players.RemoveAt(index);
                    RosterChanged?.Invoke();
                    break;
                }

                case LobbyEventKind.ReadyChanged:
                {
                    LobbyPlayer player = Find(e.playerId);
                    if (player == null) return;
                    player.isReady = e.boolValue;
                    RosterChanged?.Invoke();
                    break;
                }

                case LobbyEventKind.SlotChanged:
                {
                    LobbyPlayer player = Find(e.playerId);
                    if (player == null) return;
                    player.slotIndex = e.intValue;
                    RosterChanged?.Invoke();
                    break;
                }

                case LobbyEventKind.CharacterChanged:
                {
                    LobbyPlayer player = Find(e.playerId);
                    if (player == null) return;
                    player.characterId = e.stringA;
                    RosterChanged?.Invoke();
                    break;
                }

                case LobbyEventKind.SwapRequested:
                    if (e.playerId == _transport.LocalPlayerId)
                    {
                        Notify(e.stringA + " WANTS YOUR SPOT");
                        SwapRequestReceived?.Invoke(e.intValue, Mathf.RoundToInt(e.floatValue));
                    }
                    break;

                case LobbyEventKind.SwapResolved:
                    if (e.playerId == _transport.LocalPlayerId || e.intValue == _transport.LocalPlayerId)
                        Notify(e.boolValue ? "SWAPPED" : "SWAP DECLINED");
                    break;

                case LobbyEventKind.StateChanged:
                    SetState((LobbyState)e.intValue);
                    break;

                case LobbyEventKind.CountdownChanged:
                    _countdown = e.floatValue;
                    CountdownChanged?.Invoke(_countdown);
                    break;

                case LobbyEventKind.Rejected:
                    if (e.playerId == _transport.LocalPlayerId) Notify(e.stringA);
                    break;
            }
        }

        private void SetState(LobbyState state)
        {
            if (_state == state) return;
            _state = state;
            StateChanged?.Invoke(state);

            if (state == LobbyState.Locked) Notify("LOCKED IN");
            if (state == LobbyState.Starting) StartMatch();
        }

        /// <summary>
        /// The lobby's last act. It hands off and stops — role assignment, keys, the door and every
        /// other rule belong to the match layer, and duplicating any of that here is how the two
        /// disagree later.
        ///
        /// NETCODE: server-driven for everyone. The server transitions the whole room; a client
        /// must never load the match scene off its own countdown.
        /// </summary>
        private void StartMatch()
        {
            MatchStarting?.Invoke();

            if (_config == null || !_config.loadMatchSceneOnStart)
            {
                Debug.Log("[Lobby] Countdown finished. Scene load is off; the lobby stops here.");
                return;
            }

            Debug.Log("[Lobby] Starting match — loading " + _config.matchScene);
            SceneManager.LoadScene(_config.matchScene);
        }

        // ============================================================ queries

        public LobbyPlayer Find(int playerId)
        {
            for (int i = 0; i < _players.Count; i++)
                if (_players[i].id == playerId) return _players[i];
            return null;
        }

        public LobbyPlayer Local => Find(LocalPlayerId);

        public LobbyPlayer Occupant(int slotIndex)
        {
            for (int i = 0; i < _players.Count; i++)
                if (_players[i].slotIndex == slotIndex) return _players[i];
            return null;
        }

        public int ReadyCount()
        {
            int count = 0;
            for (int i = 0; i < _players.Count; i++) if (_players[i].isReady) count++;
            return count;
        }

        /// <summary>Who, if anyone, is already wearing this character.</summary>
        public LobbyPlayer WearerOf(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return null;
            for (int i = 0; i < _players.Count; i++)
                if (_players[i].characterId == characterId) return _players[i];
            return null;
        }

        private string FirstFreeCharacter()
        {
            foreach (LobbyCharacterCatalog.Character character in LobbyCharacterCatalog.All)
                if (WearerOf(character.id) == null) return character.id;

            return LobbyCharacterCatalog.All[0].id;   // more players than characters; should not happen
        }

        private int FirstFreeSlot()
        {
            for (int slot = 0; slot < _config.maxPlayers; slot++)
                if (Occupant(slot) == null) return slot;
            return -1;
        }

        private void Notify(string message) => Notified?.Invoke(message);
    }
}
