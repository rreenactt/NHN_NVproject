using UnityEngine;

namespace NV.Game
{
    /// <summary>
    /// The two layers the ruleset's asymmetry is built out of, plus the one call that applies
    /// them to a camera.
    ///
    /// "The door is visible to Runners only" and "the Seeker can see the blood trail" are the same
    /// problem: one world, two truths about what is in it. Doing that with two copies of the
    /// object, or by toggling renderers as roles change, means every new object has to remember
    /// the rule. A culling mask means the object only has to pick a layer — and it cannot leak,
    /// because the camera never renders the layer at all.
    ///
    /// Layers are defined in ProjectSettings/TagManager.asset: 10 RunnerVision, 11 SeekerVision.
    /// </summary>
    public static class MatchLayers
    {
        public const int RunnerVision = 10;
        public const int SeekerVision = 11;

        /// <summary>Unity's Default layer — every camera in the game renders it.</summary>
        public const int Everyone = 0;

        public static int RunnerVisionMask => 1 << RunnerVision;
        public static int SeekerVisionMask => 1 << SeekerVision;

        /// <summary>
        /// Which layer a blood mark belongs on, decided by whether this client is the one leaking it.
        ///
        /// "Visible to the Seeker" was implemented as `SeekerVision` for every mark, which also hid
        /// a Runner's blood from the Runner dripping it — the pool that punishes standing still was
        /// invisible to the only person it punishes, so the rule could not be learned or judged.
        ///
        /// **The decision is per client, which is why one layer covers both halves of the rule.**
        /// A body's trail is laid on every machine that can see the body, so on the bleeder's own
        /// machine it goes on the layer everyone renders, and on everybody else's it stays on
        /// `SeekerVision` — the Seeker sees it, other Runners do not, and neither of those needed a
        /// second layer. It also keeps the Seeker-camera device honest: the feed renders with the
        /// Seeker's mask, and `Everyone` is in it, so a Runner watching that feed sees the trail
        /// they are actually leaving.
        /// </summary>
        public static int BloodLayer(bool ownTrail) => ownTrail ? Everyone : SeekerVision;

        /// <summary>
        /// Points a camera at the half of the world its owner is allowed to see. Called on every
        /// role change, including the debug role swap — get it wrong in one direction and the
        /// Seeker walks straight to the door.
        /// </summary>
        public static void ApplyRoleVisibility(Camera camera, Role role)
        {
            if (camera == null) return;

            int mask = camera.cullingMask;
            bool seeker = role == Role.Seeker;

            // Seeker: blood yes, door no. Runner: the exact opposite. An unassigned player sees
            // neither, which is what the lobby should look like.
            if (seeker) mask |= SeekerVisionMask; else mask &= ~SeekerVisionMask;
            if (role == Role.Runner) mask |= RunnerVisionMask; else mask &= ~RunnerVisionMask;

            camera.cullingMask = mask;
        }
    }
}
