using NV.Client.Net;
using NV.Client.Net.Session;
using UnityEngine;

namespace NV.Client.Map
{
    /// <summary>
    /// Puts the room's map into this scene. **This is what makes one scene serve every map.**
    ///
    /// Before this existed, a map needed a scene of its own, that scene had to be in Build Settings,
    /// and the pairing lived in code (<see cref="MapSceneTable"/>) — three edits per map, one of them
    /// a scene nobody can review in a diff. A level baked by the generator is a prefab plus an
    /// asset, and both are things this component can look up by name at runtime.
    ///
    /// <see cref="MapSceneTable"/> is not gone: two levels have scenes that carry more than the map
    /// (SampleScene's runtime generator, MultiplayerTest's instrumentation), and the router still
    /// sends those maps to those scenes. This component serves everything else.
    /// </summary>
    /// <remarks>
    /// Execution order is load-bearing. <c>MatchBootstrap</c> is <c>-70</c> and looks for the level
    /// in its own <c>Awake</c>; the map has to exist by then or the match layer decides there is no
    /// level and quietly runs with no objectives. <c>Instantiate</c> runs the new object's Awake
    /// synchronously, so building it here is enough.
    /// </remarks>
    [DefaultExecutionOrder(-90)]
    public sealed class MapRuntimeLoader : MonoBehaviour
    {
        [Tooltip("Map to build when there is no session — opening this scene directly in the editor. " +
                 "Blank means the catalog's first playable row.")]
        [SerializeField] private string editorFallbackMapId = string.Empty;

        /// <summary>The map that was built, or empty if none was.</summary>
        public string LoadedMapId { get; private set; } = string.Empty;

        private void Awake()
        {
            var mapId = ResolveMapId();

            if (string.IsNullOrEmpty(mapId))
            {
                Debug.LogError(
                    "[NV] 이 씬이 어느 맵을 열어야 하는지 알 수 없다. 세션도 없고 " +
                    "editorFallbackMapId 도 비어 있으며 카탈로그에 쓸 만한 줄이 없다.");
                return;
            }

            var catalog = MapCatalog.Load();
            var entry = catalog == null ? null : catalog.Find(mapId);

            if (entry == null)
            {
                // 조용히 넘기면 플레이어가 허공에서 떨어지고, 원인이 서버인지 씬인지
                // 화면에서 구분할 수 없다.
                Debug.LogError(
                    $"[NV] 맵 '{mapId}' 이 이 빌드의 카탈로그에 없다. " +
                    "Tools ▸ NV ▸ Map ▸ Map Generator 로 그 맵을 구우면 카탈로그에 등록된다.");
                return;
            }

            if (entry.prefab == null)
            {
                Debug.LogError(
                    $"[NV] 맵 '{mapId}' 의 카탈로그 줄에 프리팹이 없다. " +
                    "굽을 때 프리팹 쓰기를 켜야 이 씬이 그 레벨을 세울 수 있다.");
                return;
            }

            var level = Instantiate(entry.prefab);
            level.name = entry.prefab.name;

            LoadedMapId = mapId;
        }

        /// <summary>
        /// Which map this scene is for.
        ///
        /// The room is the answer whenever there is one — the server decided it at creation and the
        /// client must not second-guess it, or the two simulate different terrain and the only
        /// symptom is a map-hash mismatch. The fallback exists so this scene can be opened straight
        /// from the editor, which is how anybody looks at a level while working on it.
        /// </summary>
        private string ResolveMapId()
        {
            var session = NetSession.Current;

            if (session != null && !string.IsNullOrEmpty(session.Room.MapName))
            {
                return session.Room.MapName;
            }

            if (!string.IsNullOrEmpty(editorFallbackMapId))
            {
                return editorFallbackMapId;
            }

            var catalog = MapCatalog.Load();

            if (catalog == null) return string.Empty;

            foreach (var entry in catalog.Entries)
            {
                if (entry != null && entry.prefab != null) return entry.mapId;
            }

            return string.Empty;
        }
    }
}
