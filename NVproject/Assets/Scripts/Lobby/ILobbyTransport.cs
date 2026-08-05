using System;
using System.Collections.Generic;

namespace NV.Lobby
{
    /// <summary>
    /// The seam between the lobby's logic and the network. Two channels, and only two:
    ///
    /// <list type="bullet">
    /// <item><b>Requests</b> travel client → authority. A client never applies its own request.</item>
    /// <item><b>Events</b> travel authority → every client. Clients apply these and nothing else.</item>
    /// </list>
    ///
    /// That shape is not decoration — it is exactly the shape every netcode stack already has
    /// (server RPC in, replicated state out), so the networked transport is a mapping job rather
    /// than a rewrite.
    ///
    /// NETCODE: implement a new <see cref="ILobbyTransport"/>. Do **not** move networking calls
    /// into <see cref="LobbyManager"/>. If a signature does not fit your stack, change the
    /// interface and update <see cref="OfflineLobbyTransport"/> too — never bypass it.
    /// Full handoff: <c>.claude/skills/lobby-builder/references/netcode-integration.md</c>.
    /// </summary>
    public interface ILobbyTransport
    {
        /// <summary>
        /// True on the machine allowed to decide. Offline this is always true; networked it is the
        /// server/host only, and <see cref="LobbyManager"/> uses it to gate every authoritative path.
        /// </summary>
        bool IsAuthority { get; }

        /// <summary>Id the local player is playing as.</summary>
        int LocalPlayerId { get; }

        /// <summary>Raised on the authority when a client asks for something. Never raised on a pure client.</summary>
        event Action<LobbyRequest> RequestReceived;

        /// <summary>Raised on every client when the authority has decided something.</summary>
        event Action<LobbyEvent> EventReceived;

        /// <summary>Client → authority.</summary>
        void Submit(LobbyRequest request);

        /// <summary>
        /// Authority → all clients.
        ///
        /// NETCODE: prefer replicated state over a literal broadcast where the stack offers it
        /// (a NetworkVariable change event carries the same information and survives late joins).
        /// </summary>
        void Broadcast(LobbyEvent lobbyEvent);

        /// <summary>Pumped by <see cref="LobbyManager"/> every frame; offline this is where fake players act.</summary>
        void Tick(float deltaTime);
    }

    /// <summary>
    /// The lobby with no network under it: requests loop straight back to the authority handler,
    /// broadcasts loop straight back to the client handler, and both happen on the same machine in
    /// the same frame.
    ///
    /// **Keep this working after networking lands.** It is how the lobby stays testable solo, and
    /// it is the reference implementation for what the real transport has to reproduce.
    ///
    /// It also invents the other people in the room. Without them the row is one figure and five
    /// empty slots, and nothing about the ready/countdown/swap flow can be exercised at all.
    /// </summary>
    public sealed class OfflineLobbyTransport : ILobbyTransport
    {
        private readonly List<LobbyRequest> _pending = new List<LobbyRequest>();
        private readonly List<int> _botIds = new List<int>();
        private readonly System.Random _random;

        private float _botTimer;
        private int _nextBotId = 100;
        private bool _botsSeeded;

        private readonly int _botCount;
        private readonly float _botJoinInterval;
        private readonly float _botReadyDelay;

        private float _elapsed;

        public bool IsAuthority => true;
        public int LocalPlayerId => 1;

        public event Action<LobbyRequest> RequestReceived;
        public event Action<LobbyEvent> EventReceived;

        public OfflineLobbyTransport(int botCount, float botJoinInterval, float botReadyDelay, int seed)
        {
            _botCount = Math.Max(0, botCount);
            _botJoinInterval = Math.Max(0.1f, botJoinInterval);
            _botReadyDelay = Math.Max(0.5f, botReadyDelay);
            _random = new System.Random(seed);
        }

        public void Submit(LobbyRequest request)
        {
            // Queued rather than invoked inline: a networked transport always has a frame of
            // latency here, and code that accidentally depends on same-frame application would
            // break the moment it is swapped in.
            _pending.Add(request);
        }

        public void Broadcast(LobbyEvent lobbyEvent)
        {
            // Fake players answer swap requests, otherwise the accept path cannot be exercised
            // without a second machine and every request just lapses on the timeout.
            if (lobbyEvent.kind == LobbyEventKind.SwapRequested && _botIds.Contains(lobbyEvent.playerId))
                _botSwapAnswers[lobbyEvent.intValue] = new BotAnswer
                {
                    playerId = lobbyEvent.playerId,
                    at = _elapsed + 1.2f,
                    accept = _random.NextDouble() < 0.7,   // mostly obliging, occasionally not
                };

            EventReceived?.Invoke(lobbyEvent);
        }

        private struct BotAnswer
        {
            public int playerId;
            public float at;
            public bool accept;
        }

        private readonly Dictionary<int, BotAnswer> _botSwapAnswers = new Dictionary<int, BotAnswer>();

        public void Tick(float deltaTime)
        {
            _elapsed += deltaTime;

            for (int i = 0; i < _pending.Count; i++) RequestReceived?.Invoke(_pending[i]);
            _pending.Clear();

            TickFakePlayers(deltaTime);
            TickBotSwapAnswers();
        }

        /// <summary>
        /// The other five. They wander in one at a time and then ready up after a while, which is
        /// enough to drive the whole countdown/cancel flow without a second machine.
        /// </summary>
        private void TickFakePlayers(float deltaTime)
        {
            if (!_botsSeeded)
            {
                _botsSeeded = true;
                _botTimer = _botJoinInterval;
            }

            if (_botIds.Count >= _botCount) return;

            _botTimer -= deltaTime;
            if (_botTimer > 0f) return;
            _botTimer = _botJoinInterval;

            int id = _nextBotId++;
            _botIds.Add(id);

            Submit(new LobbyRequest
            {
                kind = LobbyRequestKind.Join,
                playerId = id,
                stringA = BotName(),
                boolValue = true,          // isBot
            });

            // Ready up later, on their own clock, so the countdown does not start the instant the
            // room fills.
            _readyAt[id] = _elapsed + _botReadyDelay + (float)_random.NextDouble() * _botReadyDelay;
        }

        private void TickBotSwapAnswers()
        {
            if (_botSwapAnswers.Count == 0) return;

            var due = new List<int>();
            foreach (KeyValuePair<int, BotAnswer> entry in _botSwapAnswers)
                if (_elapsed >= entry.Value.at) due.Add(entry.Key);

            foreach (int requestId in due)
            {
                BotAnswer answer = _botSwapAnswers[requestId];
                _botSwapAnswers.Remove(requestId);

                Submit(new LobbyRequest
                {
                    kind = LobbyRequestKind.RespondToSwap,
                    playerId = answer.playerId,
                    intValue = requestId,
                    boolValue = answer.accept,
                });
            }
        }

        private readonly Dictionary<int, float> _readyAt = new Dictionary<int, float>();

        /// <summary>Called by the manager each frame so bots can act without a MonoBehaviour of their own.</summary>
        public void TickBotDecisions(Func<int, bool> isReady)
        {
            foreach (KeyValuePair<int, float> entry in _readyAt)
            {
                if (_elapsed < entry.Value) continue;
                if (isReady(entry.Key)) continue;

                Submit(new LobbyRequest
                {
                    kind = LobbyRequestKind.SetReady,
                    playerId = entry.Key,
                    boolValue = true,
                });
            }
        }

        private static readonly string[] Names =
        {
            "SUBJECT 04", "M. ORTIZ", "NIGHT SHIFT", "K. VANCE", "THE INTERN",
            "D. HALE", "UNIT 12", "R. OKADA",
        };

        private string BotName() => Names[_random.Next(Names.Length)] + " " + _random.Next(10, 99);
    }
}
