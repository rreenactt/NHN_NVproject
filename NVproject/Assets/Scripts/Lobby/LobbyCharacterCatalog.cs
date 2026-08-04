using System.Collections.Generic;
using UnityEngine;

namespace NV.Lobby
{
    /// <summary>How a figure stands about. One per character, so no two people in the row move alike.</summary>
    public enum IdleStyle
    {
        /// <summary>Rocks heel to toe, hands loose. Bored and settled in.</summary>
        Rock = 0,

        /// <summary>Small fast fidgets, weight never quite settled. Nervous.</summary>
        Fidget = 1,

        /// <summary>Arms folded, slow deliberate nod. Has been here before.</summary>
        Nod = 2,

        /// <summary>Head sweeps the room on a slow cycle. Watching the exits.</summary>
        Scan = 3,

        /// <summary>Every few seconds, lifts a wrist and checks it. Impatient.</summary>
        WatchCheck = 4,

        /// <summary>Bounces on the balls of the feet. Cannot stand still.</summary>
        Bounce = 5,

        /// <summary>Almost motionless, then a single sharp head turn. Unsettling.</summary>
        Stillness = 6,

        /// <summary>Periodically stretches both arms overhead. Loosening up.</summary>
        Stretch = 7,
    }

    /// <summary>The accessory on the head — the silhouette that tells two figures apart at a distance.</summary>
    public enum HeadGear
    {
        None = 0,
        Cap = 1,
        HardHat = 2,
        Band = 3,
        Hood = 4,
        Visor = 5,
    }

    /// <summary>
    /// Eight finished characters, picked whole.
    ///
    /// This replaced a per-part customiser (overalls / trim / hat, mixed freely). Mixing parts gave
    /// nine hundred combinations and no *characters* — a row of six people wearing slightly
    /// different beige. Eight complete looks, each with its own colours, silhouette and way of
    /// standing, means every figure in the row reads as somebody.
    ///
    /// **One skin per person, and no two people may wear the same one.** That is enforced on the
    /// authority (see <see cref="LobbyManager"/>), not in the UI — the UI only greys out what is
    /// already taken.
    /// </summary>
    public static class LobbyCharacterCatalog
    {
        public sealed class Character
        {
            public string id;
            public string label;
            public Color suit;      // torso and arms
            public Color trim;      // legs and belt
            public Color accent;    // head, and the accessory
            public HeadGear head;
            public IdleStyle idle;
        }

        private static List<Character> _all;

        public static IReadOnlyList<Character> All => _all ??= Build();

        public static int Count => All.Count;

        private static List<Character> Build() => new List<Character>
        {
            new Character
            {
                id = "janitor", label = "JANITOR",
                suit = new Color(0.36f, 0.42f, 0.29f), trim = new Color(0.24f, 0.27f, 0.20f),
                accent = new Color(0.86f, 0.84f, 0.76f), head = HeadGear.Cap, idle = IdleStyle.Rock,
            },
            new Character
            {
                id = "intern", label = "INTERN",
                suit = new Color(0.88f, 0.86f, 0.79f), trim = new Color(0.42f, 0.40f, 0.36f),
                accent = new Color(0.90f, 0.87f, 0.80f), head = HeadGear.None, idle = IdleStyle.Fidget,
            },
            new Character
            {
                id = "foreman", label = "FOREMAN",
                suit = new Color(0.58f, 0.31f, 0.17f), trim = new Color(0.30f, 0.26f, 0.22f),
                accent = new Color(0.87f, 0.82f, 0.74f), head = HeadGear.HardHat, idle = IdleStyle.Nod,
            },
            new Character
            {
                id = "guard", label = "NIGHT GUARD",
                suit = new Color(0.24f, 0.27f, 0.33f), trim = new Color(0.16f, 0.18f, 0.22f),
                accent = new Color(0.82f, 0.79f, 0.72f), head = HeadGear.Cap, idle = IdleStyle.Scan,
            },
            new Character
            {
                id = "technician", label = "TECHNICIAN",
                suit = new Color(0.27f, 0.47f, 0.49f), trim = new Color(0.19f, 0.30f, 0.31f),
                accent = new Color(0.85f, 0.83f, 0.76f), head = HeadGear.Visor, idle = IdleStyle.WatchCheck,
            },
            new Character
            {
                id = "visitor", label = "VISITOR",
                suit = new Color(0.90f, 0.89f, 0.85f), trim = new Color(0.66f, 0.20f, 0.17f),
                accent = new Color(0.88f, 0.85f, 0.78f), head = HeadGear.None, idle = IdleStyle.Bounce,
            },
            new Character
            {
                id = "archivist", label = "ARCHIVIST",
                suit = new Color(0.19f, 0.18f, 0.21f), trim = new Color(0.33f, 0.36f, 0.28f),
                accent = new Color(0.80f, 0.78f, 0.72f), head = HeadGear.Band, idle = IdleStyle.Stretch,
            },
            new Character
            {
                id = "stranger", label = "STRANGER",
                suit = new Color(0.15f, 0.14f, 0.15f), trim = new Color(0.11f, 0.10f, 0.11f),
                accent = new Color(0.35f, 0.33f, 0.32f), head = HeadGear.Hood, idle = IdleStyle.Stillness,
            },
        };

        public static Character Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (Character character in All)
                if (character.id == id) return character;
            return null;
        }

        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i].id == id) return i;
            return -1;
        }

        /// <summary>Server-side validation: is this even a character?</summary>
        public static bool IsValid(string id) => Find(id) != null;
    }
}
