namespace NV.Client.Net.Session
{
    /// <summary>
    /// "The result is still on screen." A latch the scene router asks before it leaves a finished
    /// match for the waiting room.
    ///
    /// **It exists because <see cref="SceneTransitionHold"/> cannot do this job, and two attempts
    /// to make it do so both failed.** That one is a deadline — deliberately, so a scene that dies
    /// mid-animation costs half a second instead of stranding everyone. A result screen is the
    /// opposite: it waits for a person, for as long as that takes.
    ///
    /// **And the deeper reason both attempts failed is execution order.** The router runs at 0 and
    /// the HUD at 60, so anything the HUD arms is armed *after* the router has already cut the
    /// scene on that same frame. No amount of moving the call earlier inside `GameHudController`
    /// can fix that; the arming has to be something the router can read for itself. So this latch
    /// is never *set* — it is only ever *cleared*, by the person pressing the button. Its default
    /// state is "hold", and the router derives "is there a result at all" from
    /// <c>MatchManager.Phase</c>, which `MatchSync` (-75) has already written by the time the
    /// router (0) looks.
    ///
    /// Static because it outlives the scene: the thing being decided is whether to unload the
    /// scene the deciding component lives in.
    /// </summary>
    public static class MatchResultGate
    {
        /// <summary>Has the player closed this match's result?</summary>
        public static bool Dismissed { get; private set; }

        /// <summary>They pressed 나가기. The router may cut on its next frame.</summary>
        public static void Dismiss() => Dismissed = true;

        /// <summary>
        /// A new result may come. Called by the router itself whenever it sees a match that is not
        /// over — self-maintaining, so nothing has to remember to reset it between matches.
        /// </summary>
        public static void Rearm() => Dismissed = false;
    }
}
