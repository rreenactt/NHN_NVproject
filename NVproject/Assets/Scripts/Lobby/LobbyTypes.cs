using System;
using System.Collections.Generic;

namespace NV.Lobby
{
    /// <summary>
    /// What the lobby is doing. Every piece of input in the lobby is gated on this, and the
    /// networked version replicates it — see the replication table in
    /// <c>.claude/skills/lobby-builder/references/netcode-integration.md</c>.
    /// </summary>
    public enum LobbyState
    {
        /// <summary>Players joining, readying, swapping, dressing up.</summary>
        Waiting = 0,

        /// <summary>Everyone is ready and the clock is running. Un-readying still cancels it.</summary>
        CountingDown = 1,

        /// <summary>Past the lock threshold. No swaps, no customization, no un-readying.</summary>
        Locked = 2,

        /// <summary>Handing off to the match. The lobby is done.</summary>
        Starting = 3,
    }

    /// <summary>How clicking another player's slot behaves.</summary>
    public enum SlotSwapMode
    {
        /// <summary>Click an empty slot to move there. Occupied slots do nothing.</summary>
        FreeMove = 0,

        /// <summary>Empty slots as above, plus clicking an occupied slot asks its owner to trade.</summary>
        SwapRequest = 1,
    }

    /// <summary>
    /// One person in the row. Pure data — no Unity types — so the networked transport can send it
    /// without dragging a MonoBehaviour along.
    ///
    /// NETCODE: every field here is in the replication table. If you add one, add a row there in
    /// the same edit; state that is not in the table is how desyncs get shipped.
    /// </summary>
    [Serializable]
    public sealed class LobbyPlayer
    {
        public int id;
        public string displayName;
        public int slotIndex = -1;
        public bool isReady;
        public bool isLocal;
        public bool isBot;

        /// <summary>
        /// Which of the eight characters this player is wearing. **Unique across the room** — the
        /// authority refuses a pick that somebody else already has, so this doubles as an identity.
        /// </summary>
        public string characterId;
    }

    /// <summary>A client asking the authority for something. Never applied directly — see <see cref="ILobbyTransport"/>.</summary>
    public enum LobbyRequestKind
    {
        Join = 0,
        Leave = 1,
        SetReady = 2,
        MoveToSlot = 3,
        RequestSwap = 4,
        RespondToSwap = 5,
        SetCharacter = 6,
    }

    /// <summary>The authority telling clients what actually happened.</summary>
    public enum LobbyEventKind
    {
        PlayerJoined = 0,
        PlayerLeft = 1,
        ReadyChanged = 2,
        SlotChanged = 3,
        CharacterChanged = 4,
        SwapRequested = 5,
        SwapResolved = 6,
        StateChanged = 7,
        CountdownChanged = 8,
        Rejected = 9,
    }

    /// <summary>
    /// One client→authority message. Deliberately a flat struct of primitives: it maps onto an RPC
    /// argument list in every stack without a custom serializer.
    /// </summary>
    [Serializable]
    public struct LobbyRequest
    {
        public LobbyRequestKind kind;
        public int playerId;
        public int intValue;        // slot index, target player id, or request id
        public bool boolValue;      // ready / accept
        public string stringA;      // display name, or the character id being picked
    }

    /// <summary>One authority→clients message.</summary>
    [Serializable]
    public struct LobbyEvent
    {
        public LobbyEventKind kind;
        public int playerId;
        public int intValue;
        public bool boolValue;
        public float floatValue;
        public string stringA;
        public string stringB;
    }
}
