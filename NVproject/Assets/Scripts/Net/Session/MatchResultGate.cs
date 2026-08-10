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
        /// <summary>
        /// A result has been put up and nobody has closed it yet.
        ///
        /// **This is a latch, not a reading of the room's phase.** It used to be
        /// `Phase == Ended && !Dismissed`, and that made one player's 나가기 close everyone's
        /// screen: the host reaching the waiting room asks the room back to `Waiting`
        /// (`GameLobbyBootstrap.ReopenFinishedRoom`), the phase leaves `Ended`, and every other
        /// client's gate opened at once. Whose result is on screen is a **local** question — the
        /// room moving on is not an answer to it.
        /// </summary>
        public static bool Standing { get; private set; }

        /// This match's result has already been put up once.
        ///
        /// **Without this the screen cannot be closed at all.** The router calls
        /// <see cref="Raise"/> every frame it sees a finished match, and the match stays finished
        /// until the host reopens the room — which only happens *after* the host has already left.
        /// So a plain `Standing = true` undoes every <see cref="Dismiss"/> on the very next frame,
        /// and 나가기 does nothing for anybody.
        private static bool _raised;

        /// <summary>
        /// A match has ended here. Raises the screen **once per match** — see
        /// <see cref="_raised"/> for why calling it every frame must not keep re-raising.
        /// </summary>
        public static void Raise()
        {
            if (_raised)
            {
                return;
            }

            _raised = true;
            Standing = true;
        }

        /// <summary>They pressed 나가기. The router may cut on its next frame.</summary>
        public static void Dismiss() => Standing = false;

        /// <summary>
        /// A new match is running, so no old result is owed — and the next one may be raised.
        /// Called from the router's in-game branch, which is the one place that can know a match
        /// is actually running, and the one place allowed to drop an unread result.
        /// </summary>
        public static void Rearm()
        {
            _raised = false;
            Standing = false;
        }
    }
}
