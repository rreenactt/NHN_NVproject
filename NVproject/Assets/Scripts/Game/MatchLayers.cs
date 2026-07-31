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

        public static int RunnerVisionMask => 1 << RunnerVision;
        public static int SeekerVisionMask => 1 << SeekerVision;

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
