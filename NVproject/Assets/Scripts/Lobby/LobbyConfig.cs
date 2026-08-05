using UnityEngine;

namespace NV.Lobby
{
    /// <summary>
    /// Every lobby tunable in one asset, so the row can be widened or the countdown retimed
    /// without a recompile. Create one with **Assets ▸ Create ▸ NV ▸ Lobby Config**, or let
    /// <see cref="LobbyBootstrap"/> build a throwaway instance at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "NV/Lobby Config", fileName = "LobbyConfig")]
    public sealed class LobbyConfig : ScriptableObject
    {
        [Header("Room")]
        [Tooltip("Stand slots in the row. The contract is 5-6; the room widens to fit.")]
        [Range(2, 10)] public int maxPlayers = 6;

        [Tooltip("Players needed before the countdown may start at all.")]
        [Range(1, 10)] public int minPlayers = 2;

        [Tooltip("Metres between stand slots.")]
        public float slotSpacing = 1.35f;

        [Header("Countdown")]
        [Tooltip("Seconds from everyone-ready to match start.")]
        public float countdownSeconds = 10f;

        [Tooltip("Seconds remaining at which the lobby locks: no more swaps, customisation or " +
                 "un-readying. A player who can change their mind at 0.2 s left is a player who " +
                 "can desync the start.")]
        public float lockAtSeconds = 3f;

        [Header("Slots")]
        public SlotSwapMode swapMode = SlotSwapMode.SwapRequest;

        [Tooltip("Seconds a swap request waits for an answer before it lapses.")]
        public float swapRequestTimeout = 8f;

        [Header("Offline testing")]
        [Tooltip("Fake players the offline transport walks into the room. 0 leaves you alone in it.")]
        [Range(0, 9)] public int practiceLobbyBots = 4;

        [Tooltip("Seconds between fake players arriving.")]
        public float botJoinInterval = 1.4f;

        [Tooltip("Roughly how long a fake player takes to press ready.")]
        public float botReadyDelay = 6f;

        [Tooltip("Seed for bot names and choices. 0 draws a fresh one each run.")]
        public int seed = 0;

        [Header("Handoff")]
        [Tooltip("Scene loaded when the countdown reaches zero. The match layer takes over there.")]
        public string matchScene = "SampleScene";

        [Tooltip("Off, the countdown finishes and stops at Starting without loading anything — " +
                 "useful while working on the lobby itself.")]
        public bool loadMatchSceneOnStart = true;
    }
}
