using UnityEngine;

namespace NV.Client.Map
{
    /// <summary>
    /// The parameters a level is generated from, as an asset so a layout can be saved, shared and
    /// diffed instead of living in one person's window.
    ///
    /// **What every level has** is here; what a particular generator needs goes on a subclass.
    /// A single settings type covering all generators would put the Backrooms' stairwell rectangle
    /// in front of someone laying out a test arena, and would grow a field every time a generator
    /// is added.
    /// </summary>
    /// <remarks>
    /// These live in the runtime assembly rather than under <c>Editor/</c> on purpose: an asset
    /// whose script sits in an editor-only assembly cannot be loaded outside the editor, and the
    /// failure shows up as a null reference in a build rather than as a compile error.
    /// </remarks>
    public abstract class MapGeneratorSettings : ScriptableObject
    {
        [Tooltip("Export filename, baked asset name and the server's map id. The server registers " +
                 "every file in its map directory under that file's own name, so this one string is " +
                 "the map's identity everywhere — a level whose name drifts is judged against a " +
                 "stale export, and the only symptom is a map-hash mismatch on connect.")]
        public string mapName = string.Empty;

        [Tooltip("Fixed layout. Levels that draw no randomness ignore it.")]
        public int seed;

        [Tooltip("Fresh layout every generation. OFF, and it blocks export: the collision boxes are " +
                 "hashed against the server's copy, so a seed that changes per run makes the file " +
                 "describe terrain that will never be built again.")]
        public bool randomizeSeed;

        [Header("Shown in the lobby — never used for judgement")]
        [Tooltip("Human-readable name for the create-room list. Blank means the lobby shows mapName.")]
        public string displayName = string.Empty;

        [Tooltip("One line under the name in the create-room list.")]
        public string description = string.Empty;

        [Tooltip("Advice printed as \"2-8명\". 0 means the server's own min/capacity. A map does not " +
                 "get to set the room size — that is the server's judgement.")]
        public int recommendedPlayersMin;

        public int recommendedPlayersMax;

        [Tooltip("Free-form labels the lobby may filter on, e.g. match, dev, small.")]
        public string[] tags = new string[0];

        /// <summary>
        /// Why a level made from these settings would not reproduce, or <c>null</c> if it would.
        ///
        /// Asked of the settings rather than of the generator because that is where the answer is:
        /// <see cref="randomizeSeed"/> is the whole of it today, and a generator that draws no
        /// randomness at all cannot be made irreproducible by any value here.
        /// </summary>
        public virtual string DescribeBlocker()
        {
            return DescribeNameBlocker() ?? DescribeSeedBlocker();
        }

        /// <summary>
        /// A level with no name cannot be written anywhere.
        ///
        /// <see cref="mapName"/> is deliberately not defaulted: a blank preset that silently
        /// claimed "backrooms" would overwrite a real map's file the first time somebody pressed
        /// export.
        /// </summary>
        protected string DescribeNameBlocker()
        {
            if (!string.IsNullOrEmpty(mapName)) return null;

            return "맵 이름이 비어 있다. 이 값이 곧 export 파일명이자 서버의 맵 id 다.";
        }

        /// <summary>
        /// A level that redraws its seed describes terrain that will never be built again.
        ///
        /// Generators that draw no randomness are not subject to this and say so by not calling it.
        /// </summary>
        protected string DescribeSeedBlocker()
        {
            if (!randomizeSeed) return null;

            return "randomizeSeed 가 켜져 있다. 씨드를 매번 새로 뽑으므로 export 한 지형이 " +
                   "다음 생성에서 다시 만들어지지 않는다. 끄고, 고정할 씨드를 seed 필드에 적는다.";
        }

        /// <summary>The seed to actually draw from.</summary>
        public int ResolveSeed()
        {
            return randomizeSeed ? new System.Random().Next() : seed;
        }
    }
}
