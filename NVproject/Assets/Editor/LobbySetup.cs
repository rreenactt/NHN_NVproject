using System.Collections.Generic;
using System.IO;
using System.Linq;
using NV.Lobby;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor entry points for the lobby. The lobby lives in its own scene so the gameplay scene stays
/// exactly as it is — the handoff is a scene load, which is also what the networked version will
/// do when the server transitions the room.
/// </summary>
public static class LobbySetup
{
    private const string ScenePath = "Assets/Scenes/Lobby.unity";
    private const string ConfigPath = "Assets/Settings/LobbyConfig.asset";

    [MenuItem("Tools/Backrooms/Create Lobby Scene", priority = 30)]
    public static void CreateLobbyScene()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("[LobbySetup] Exit play mode first — scene edits made in play mode are discarded.");
            return;
        }

        if (File.Exists(ScenePath))
        {
            Debug.Log("[LobbySetup] " + ScenePath + " already exists; opening it.");
            EditorSceneManager.OpenScene(ScenePath);
            EnsureInBuildSettings();
            return;
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var go = new GameObject("Lobby");
        var bootstrap = go.AddComponent<LobbyBootstrap>();
        bootstrap.config = LoadOrCreateConfig();

        EditorSceneManager.MarkSceneDirty(scene);

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);

        EnsureInBuildSettings();

        Debug.Log("[LobbySetup] Created " + ScenePath +
                  ". The room, the row, the manager and the UI are all built at runtime — press play.");
    }

    [MenuItem("Tools/Backrooms/Create Lobby Config Asset", priority = 31)]
    public static void CreateConfigAsset()
    {
        LobbyConfig config = LoadOrCreateConfig();
        Selection.activeObject = config;
        EditorGUIUtility.PingObject(config);
    }

    private static LobbyConfig LoadOrCreateConfig()
    {
        var existing = AssetDatabase.LoadAssetAtPath<LobbyConfig>(ConfigPath);
        if (existing != null) return existing;

        string directory = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
        }

        var config = ScriptableObject.CreateInstance<LobbyConfig>();
        AssetDatabase.CreateAsset(config, ConfigPath);
        AssetDatabase.SaveAssets();
        Debug.Log("[LobbySetup] Created " + ConfigPath + ".");
        return config;
    }

    /// <summary>
    /// Both scenes have to be in the build settings or <c>SceneManager.LoadScene</c> throws when the
    /// countdown ends — the failure lands at the end of a ten-second wait, which is the worst
    /// possible moment to discover it.
    /// </summary>
    private static void EnsureInBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();

        AddIfMissing(scenes, ScenePath);
        AddIfMissing(scenes, "Assets/Scenes/SampleScene.unity");

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void AddIfMissing(List<EditorBuildSettingsScene> scenes, string path)
    {
        if (!File.Exists(path)) return;
        if (scenes.Any(s => s.path == path)) return;

        scenes.Add(new EditorBuildSettingsScene(path, true));
        Debug.Log("[LobbySetup] Added " + path + " to the build settings.");
    }
}
