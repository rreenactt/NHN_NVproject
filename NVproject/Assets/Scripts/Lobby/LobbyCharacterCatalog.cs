using System.Collections.Generic;
using NV.Shared.Contracts.Messages;
using UnityEngine;

namespace NV.Client.Lobby
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
    /// **One skin per person, and no two people may wear the same one.** 그 판정은 서버에 있다
    /// (`Room.SetCharacter`). 여기서 하는 일은 이미 쓰이는 것을 흐리게 그리는 것뿐이다.
    ///
    /// **목록의 순서가 와이어 값이다.** `RoomPlayerEntry.CharacterId` 가 이 목록의 인덱스이며,
    /// 서버는 이름도 색도 모르고 개수(`ProtocolInfo.LobbyCharacterCount`)만 안다 — 그래야
    /// 표현이 한 곳에만 있다. 그래서 **줄을 재배열하면 이미 접속한 클라이언트와 어긋난다.**
    /// 새 캐릭터는 끝에 붙이고, 지울 때는 그 자리를 비워 둘 방법이 없으므로 개수 상수를
    /// 함께 고친다.
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

        private static List<Character> Build()
        {
            var all = Characters();

            // 개수는 와이어의 값이다. 서버가 이 상수로 범위를 검사하므로, 표가 더 길면
            // 고를 수 있게 그려 놓고 서버가 거부하는 캐릭터가 생기고, 짧으면 남이 입은
            // 캐릭터를 그릴 수 없다. 둘 다 화면에서만 이상하게 보인다.
            if (all.Count != ProtocolInfo.LobbyCharacterCount)
            {
                Debug.LogError(
                    $"[Lobby] 캐릭터 표가 {all.Count}개인데 프로토콜은 "
                    + $"{ProtocolInfo.LobbyCharacterCount}개다. 둘을 맞춘다.");
            }

            return all;
        }

        private static List<Character> Characters() => new List<Character>
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

        // ==================================================== 와이어 값으로 다루기

        /// 와이어의 캐릭터 번호가 이 표에 있는가. `RoomPlayerEntry.NoCharacter` 는 거짓이다.
        public static bool IsValidId(byte characterId) => characterId < All.Count;

        /// 번호로 찾는다. 범위를 벗어나면 null — 미배정과 모르는 번호를 같게 다룬다.
        ///
        /// 예외를 던지지 않는다. 서버가 이 빌드보다 캐릭터가 많은 표를 쓸 수 있고, 그때는
        /// "그 캐릭터를 모른다" 를 화면에 말해야 한다.
        public static Character At(byte characterId)
        {
            return IsValidId(characterId) ? All[characterId] : null;
        }

        /// 명단에 쓰는 짧은 이름. 모르는 번호는 빈 문자열이다.
        public static string LabelOf(byte characterId)
        {
            Character character = At(characterId);

            return character != null ? character.label : string.Empty;
        }
    }
}
