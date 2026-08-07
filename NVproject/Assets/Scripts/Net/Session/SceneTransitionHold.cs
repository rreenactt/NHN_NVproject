using UnityEngine;

namespace NV.Client.Net.Session
{
    /// <summary>
    /// A short "not yet" that any scene can hold up in front of <see cref="SessionSceneRouter"/>.
    ///
    /// The router cuts to the next scene the moment the session says so, which is right — the state
    /// is the server's and the scene follows it. But a cut that lands on the same frame as the state
    /// change gives the outgoing scene no chance to end: the waiting room's row of figures simply
    /// vanishes mid-idle. This lets that scene say "half a second" without the router having to know
    /// what a waiting room is, or that one exists.
    ///
    /// **It is a deadline, not a lock.** Holds expire on their own, so a scene that starts an
    /// animation and then dies — destroyed mid-transition, an exception in its update — costs the
    /// player a fraction of a second rather than stranding them in a room the match has already left
    /// without them. Nothing here can refuse a transition; it can only postpone one.
    ///
    /// Static because it outlives the scene that sets it: the holder is being unloaded by the very
    /// transition it is delaying.
    /// </summary>
    public static class SceneTransitionHold
    {
        private static float _until;

        /// <summary>Is a transition currently being held back?</summary>
        public static bool Active => Time.unscaledTime < _until;

        /// <summary>
        /// Hold the next transition for up to <paramref name="seconds"/> more.
        ///
        /// Call it every frame while the outgoing animation runs, with a window a little longer than
        /// one frame. Asking for the whole duration up front would keep the cut waiting even when the
        /// animation ends early — or worse, when it never starts.
        /// </summary>
        public static void Hold(float seconds)
        {
            float until = Time.unscaledTime + Mathf.Max(0f, seconds);
            if (until > _until) _until = until;
        }

        /// <summary>Done — let the router cut on its next frame.</summary>
        public static void Release() => _until = 0f;
    }
}
