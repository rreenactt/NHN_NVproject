namespace NV.Game
{
    /// <summary>
    /// Which side a participant is on. Exactly one Seeker per match; everyone else runs.
    /// Unassigned exists so the lobby has a state that is not a lie.
    /// </summary>
    public enum Role
    {
        Unassigned = 0,
        Runner = 1,
        Seeker = 2,
    }

    /// <summary>Match state machine. The ruleset's four phases, in order.</summary>
    public enum MatchPhase
    {
        Lobby = 0,
        RoleReveal = 1,
        Playing = 2,
        Ended = 3,
    }

    /// <summary>How the match finished. There is no draw — the timer running out is a Seeker win.</summary>
    public enum MatchOutcome
    {
        None = 0,

        /// <summary>Two or more Runners got through the door.</summary>
        RunnersEscaped = 1,

        /// <summary>Timer hit zero with fewer than two escapes.</summary>
        SeekerTimeout = 2,

        /// <summary>Every Runner was killed before two of them escaped.</summary>
        SeekerWipedRunners = 3,

        /// <summary>
        /// The match could no longer be played — the Seeker left, or every Runner did — and the
        /// server ended it after a short grace. Nobody wins: counting a walkout as the other
        /// side's victory would make leaving a weapon. This is the first outcome the server
        /// writes itself (the byte must match <c>Room.AbortedOutcome</c> on the server).
        /// </summary>
        Aborted = 4,
    }

    /// <summary>
    /// The six device effects from the ruleset. A placed device instance has exactly one of these;
    /// several instances may share a type.
    /// </summary>
    public enum DeviceType
    {
        /// <summary>1x. Adds to the match timer — the Runners' answer to the time attack.</summary>
        AddTime = 0,

        /// <summary>Repeatable. Shows everyone's position on a top-down map, briefly.</summary>
        FullMapView = 1,

        /// <summary>1x. Clears the user's bleeding and its blood trail.</summary>
        StopBleeding = 2,

        /// <summary>1x. Freezes everyone and makes the walls see-through for a few seconds.</summary>
        FreezeAndXray = 3,

        /// <summary>Repeatable. A camera feed from wherever the Seeker is standing.</summary>
        SeekerCameraView = 4,

        /// <summary>Repeatable, but on a 12 s cooldown shared by every player. Moves the user elsewhere.</summary>
        Teleport = 5,
    }
}
