using NV.Shared.Simulation;
using UnityEngine;

namespace NV.Game
{
    /// <summary>
    /// Tunables that are *this client's* business, plus read-only views onto the ones the server
    /// owns. Balancing an asymmetric game should not mean recompiling, but it also must not mean
    /// the two sides counting different numbers.
    ///
    /// **The rule values are no longer here.** Match duration, keys required, magazine size, device
    /// cooldowns and the rest live in <see cref="MatchConstants"/> — one file, compiled into both
    /// the server and this client from the same source. What remains below as a serialized field is
    /// exactly what this client may decide alone: how blood is drawn, how transparent an x-ray wall
    /// is, and the offline-practice knobs.
    ///
    /// The shared values are exposed here as lowercase properties on purpose. They read as fields
    /// at every call site (<c>config.matchDuration</c>), so moving them cost no changes in
    /// <c>MatchManager</c>, the HUD, or the weapon — and a property cannot be serialized, so the
    /// asset can no longer carry a stale copy. Keeping two copies is the failure this prevents: the
    /// server would count 480 seconds while a HUD read whatever the asset was last saved with, and
    /// the symptom is two clocks disagreeing with no obvious cause.
    ///
    /// Create one with **Assets ▸ Create ▸ NV ▸ Game Config**, or let
    /// <see cref="MatchBootstrap"/> build a throwaway instance at runtime: with no asset assigned
    /// the game still runs, it just cannot be tuned between sessions.
    /// </summary>
    [CreateAssetMenu(menuName = "NV/Game Config", fileName = "GameConfig")]
    public sealed class GameConfig : ScriptableObject
    {
        // ============================================================ shared rules (read-only)
        //
        // Views onto MatchConstants. Not serialized, so the asset cannot drift from the server.

        public float matchDuration => MatchConstants.MatchDuration;

        public float roleRevealDuration => MatchConstants.RoleRevealDuration;

        public int escapesToWin => MatchConstants.EscapesToWin;

        public int keysRequired => MatchConstants.KeysRequired;

        public int keysPlaced => MatchConstants.KeysPlaced;

        public int carryLimit => MatchConstants.CarryLimit;

        public float keyPickupRadius => MatchConstants.KeyPickupRadius;

        public float keyInsertInterval => MatchConstants.KeyInsertInterval;

        public float doorUseRadius => MatchConstants.DoorUseRadius;

        public float escapeHoldTime => MatchConstants.EscapeHoldTime;

        public int runnerHitsToDie => MatchConstants.RunnerHitsToDie;

        public int seekerMagazine => MatchConstants.SeekerMagazine;

        public float chainWait => MatchConstants.ChainWait;

        public float chainDragTime => MatchConstants.ChainDragTime;

        public float chainDragSpeed => MatchConstants.ChainDragSpeed;

        public float chainDragMaxTime => MatchConstants.ChainDragMaxTime;

        public float chainReloadTime => MatchConstants.ChainReloadTime;

        public int deviceCount => MatchConstants.DeviceCount;

        public float deviceUseRadius => MatchConstants.DeviceUseRadius;

        public float teleportSharedCooldown => MatchConstants.TeleportSharedCooldown;

        public float repeatableDeviceCooldown => MatchConstants.RepeatableDeviceCooldown;

        public float deviceTimeBonus => MatchConstants.DeviceTimeBonus;

        public float mapViewDuration => MatchConstants.MapViewDuration;

        public float seekerCamDuration => MatchConstants.SeekerCamDuration;

        public float freezeDuration => MatchConstants.FreezeDuration;

        // ============================================================ still decided client-side
        //
        // These are judgements, not presentation, and they belong in RealtimeConstants.Match once
        // the server makes them. They stay serialized while this client still resolves the rule —
        // RealtimeConstants is internal to the server module, so moving them now would leave the
        // client unable to read a value it is still using. Each moves with its own task:
        //   dropKeysOnDeath, teleportOnHit, hitImmunity → IG-014 (combat)
        //   seekerWinsOnWipe                            → IG-007 (win conditions, blocked on OQ-2)
        //   seekerCanActivateDevices                    → IG-013 (devices, blocked on OQ-1)
        //   chainAnchorRange                            → IG-016 (chain drag, blocked on OQ-4)

        [Header("Match (moves to the server)")]
        [Tooltip("End the match the moment no Runner is left standing. Off, the Seeker has to " +
                 "sit out the rest of the clock. The design doc does not list a wipe win at all — " +
                 "see OQ-2.")]
        public bool seekerWinsOnWipe = true;

        [Header("Objective (moves to the server)")]
        [Tooltip("Drop carried keys where a Runner dies. Off, they return to the pool and " +
                 "respawn elsewhere in the level.")]
        public bool dropKeysOnDeath = true;

        [Header("Combat (moves to the server)")]
        [Tooltip("Teleport the Runner to a random valid location on every non-fatal hit.")]
        public bool teleportOnHit = true;

        [Tooltip("Seconds of immunity after a hit. Without it a three-round burst kills through " +
                 "the teleport before the victim has rendered a frame anywhere else.")]
        public float hitImmunity = 0.75f;

        // ============================================================ this client's own
        //
        // Pure presentation, or offline-practice only. Nothing here changes what the server judges,
        // so two clients may legitimately disagree about these values.

        [Header("Bleeding (presentation)")]
        [Tooltip("Metres between blood marks while a bleeding Runner moves.")]
        public float bloodSpacing = 1.1f;

        [Tooltip("Seconds a blood mark stays before it fades out.")]
        public float bloodLifetime = 25f;

        [Tooltip("Seconds a bleeding Runner may stand still before it starts costing them. " +
                 "This is the 'keep moving' rule.")]
        public float bleedStillGrace = 2f;

        [Tooltip("Standing still while bleeding pools blood where you are: one mark per this " +
                 "many seconds, growing, and they outlast the running trail.")]
        public float bleedPoolInterval = 0.7f;

        [Tooltip("How much longer a pool mark lasts than a running mark. Standing still is what " +
                 "paints a sign over your hiding place.")]
        public float bleedPoolLifetimeScale = 2.5f;

        [Header("Devices (presentation)")]
        [Tooltip("Wall opacity during the x-ray. 0 is invisible, which loses all sense of place.")]
        [Range(0f, 1f)] public float xrayWallAlpha = 0.18f;

        [Tooltip("Metres. Only used when there is no altar in the level: how far the chain looks " +
                 "for a wall to yank the Seeker to instead.")]
        public float chainAnchorRange = 9f;

        [Header("Devices (moves to the server)")]
        [Tooltip("Shots to destroy a device. The Seeker's counter-play.")]
        public int deviceDestroyHits = 4;

        [Tooltip("Let the Seeker activate devices too. Off by default: the Seeker's interaction " +
                 "with a device is shooting it. The design doc §5.2 calls the teleport a " +
                 "Seeker-only device, which contradicts this — see OQ-1.")]
        public bool seekerCanActivateDevices = false;

        [Header("HUD")]
        [Tooltip("Show Runners a compass arrow pointing at the door. It is the HUD contract's " +
                 "door marker, but it does make a hidden door easy to find — turn it off for a " +
                 "match where locating the door is meant to be most of the job.")]
        public bool showDoorCompass = true;

        [Header("Offline testing")]
        [Tooltip("Role the local player takes when there is nobody else. F1 swaps it.")]
        public Role localRole = Role.Runner;

        [Tooltip("Wandering practice Runners spawned when the match is offline, so the Seeker " +
                 "side has something to shoot and something to hear. 0 by default — raise it to " +
                 "bring the dummies back for solo testing.")]
        public int practiceRunners = 0;

        [Tooltip("Practice Runner walk speed, in m/s.")]
        public float practiceRunnerSpeed = 3.2f;

        [Tooltip("Random seed for key/door/device placement. 0 draws a fresh one each match — " +
                 "the door is supposed to move.")]
        public int placementSeed = 0;
    }
}
