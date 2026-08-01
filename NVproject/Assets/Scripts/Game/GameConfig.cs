using UnityEngine;

namespace NV.Game
{
    /// <summary>
    /// Every tunable in the ruleset, in one asset, so balancing an asymmetric game does not mean
    /// recompiling. The defaults here are the ruleset's defaults
    /// (<c>.claude/skills/game-rules/references/ruleset.md</c>) — if a number changes, change it
    /// there first, then here.
    ///
    /// Create one with **Assets ▸ Create ▸ NV ▸ Game Config**, or let
    /// <see cref="MatchBootstrap"/> build a throwaway instance at runtime: with no asset assigned
    /// the game still runs on these values, it just cannot be tuned between sessions.
    /// </summary>
    [CreateAssetMenu(menuName = "NV/Game Config", fileName = "GameConfig")]
    public sealed class GameConfig : ScriptableObject
    {
        [Header("Match")]
        [Tooltip("Seconds on the clock. 8:00 is the ruleset default.")]
        public float matchDuration = 480f;

        [Tooltip("Seconds the role reveal is held before play starts.")]
        public float roleRevealDuration = 4f;

        [Tooltip("Runners who have to get through the door for a Runner win.")]
        public int escapesToWin = 2;

        [Tooltip("End the match the moment no Runner is left standing. Off, the Seeker has to " +
                 "sit out the rest of the clock.")]
        public bool seekerWinsOnWipe = true;

        [Header("Objective")]
        [Tooltip("Keys that must go into the door before it opens.")]
        public int keysRequired = 10;

        [Tooltip("Keys scattered at the start. Below keysRequired the match is unwinnable for " +
                 "Runners, so the placer clamps it up.")]
        public int keysPlaced = 10;

        [Tooltip("How many keys one Runner can carry. 0 or less means no limit.")]
        public int carryLimit = 0;

        [Tooltip("Metres. How close a Runner has to be to pick a key up.")]
        public float keyPickupRadius = 1.4f;

        [Tooltip("Seconds between two inserts, so ten keys are a visible commitment at the door " +
                 "rather than one keypress.")]
        public float keyInsertInterval = 0.6f;

        [Tooltip("Metres from the door you can insert from, and escape through once it is open.")]
        public float doorUseRadius = 2.2f;

        [Tooltip("Seconds a Runner has to stay in the open doorway to count as escaped.")]
        public float escapeHoldTime = 0.8f;

        [Tooltip("Drop carried keys where a Runner dies. Off, they return to the pool and " +
                 "respawn elsewhere in the level.")]
        public bool dropKeysOnDeath = true;

        [Header("Combat")]
        [Tooltip("Hits that kill a Runner. The first one only makes them bleed.")]
        public int runnerHitsToDie = 2;

        [Tooltip("Teleport the Runner to a random valid location on every non-fatal hit.")]
        public bool teleportOnHit = true;

        [Tooltip("Seconds of immunity after a hit. Without it a three-round burst kills through " +
                 "the teleport before the victim has rendered a frame anywhere else.")]
        public float hitImmunity = 0.75f;

        [Header("Bleeding")]
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

        [Header("Seeker gun")]
        [Tooltip("Rounds in the magazine. Three, and emptying it is a real decision.")]
        public int seekerMagazine = 3;

        [Tooltip("Seconds the Seeker is stuck at the chain's anchor before the reload starts.")]
        public float chainWait = 3f;

        [Tooltip("Seconds. Floor on the drag, for when the altar is already close.")]
        public float chainDragTime = 0.45f;

        [Tooltip("How fast the chain reels the Seeker in, in m/s, measured along the walked route " +
                 "and not across the map. Fast enough to be violent; the three-second hold at the " +
                 "far end is where the real cost lives.")]
        public float chainDragSpeed = 45f;

        [Tooltip("Seconds. Ceiling on the drag. The route can be 400 m through a maze from the far " +
                 "corner upstairs, and nobody should spend fifteen seconds watching it.")]
        public float chainDragMaxTime = 3.5f;

        [Tooltip("Metres. Only used when there is no altar in the level: how far the chain looks " +
                 "for a wall to yank the Seeker to instead.")]
        public float chainAnchorRange = 9f;

        [Tooltip("Seconds to reload once the chain lets go.")]
        public float chainReloadTime = 1.5f;

        [Header("Devices")]
        [Tooltip("Device instances placed in the level. The ruleset says 8-9.")]
        public int deviceCount = 9;

        [Tooltip("Shots to destroy a device. The Seeker's counter-play.")]
        public int deviceDestroyHits = 4;

        [Tooltip("Seconds of global lockout after anyone uses a teleport device. Shared across " +
                 "all Runners, not per player.")]
        public float teleportSharedCooldown = 12f;

        [Tooltip("Seconds before a repeatable device can be used again at all. Stops one panel " +
                 "being spammed into a permanent map hack.")]
        public float repeatableDeviceCooldown = 8f;

        [Tooltip("Seconds added to the clock by an Add Time device.")]
        public float deviceTimeBonus = 60f;

        [Tooltip("Seconds the full-map view stays up.")]
        public float mapViewDuration = 6f;

        [Tooltip("Seconds the Seeker camera feed stays up.")]
        public float seekerCamDuration = 6f;

        [Tooltip("Seconds everyone is frozen and the walls are see-through.")]
        public float freezeDuration = 5f;

        [Tooltip("Wall opacity during the x-ray. 0 is invisible, which loses all sense of place.")]
        [Range(0f, 1f)] public float xrayWallAlpha = 0.18f;

        [Tooltip("Metres. How close a Runner has to be to use a device.")]
        public float deviceUseRadius = 2.2f;

        [Tooltip("Let the Seeker activate devices too. Off by default: the Seeker's interaction " +
                 "with a device is shooting it.")]
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
