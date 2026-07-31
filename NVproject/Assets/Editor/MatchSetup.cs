using System.IO;
using NV.Game;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor entry points for the match layer, in the same spirit as
/// <see cref="BlockPlayerSetup"/>: one menu item that puts the scene into the state the runtime
/// expects, and one that produces the config asset so the numbers can be tuned without a
/// recompile.
/// </summary>
public static class MatchSetup
{
    private const string ConfigPath = "Assets/Settings/GameConfig.asset";

    [MenuItem("Tools/Backrooms/Set Up Match", priority = 20)]
    public static void SetUpMatch()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("[MatchSetup] Exit play mode first — scene edits made in play mode are discarded.");
            return;
        }

        var existing = Object.FindFirstObjectByType<MatchBootstrap>();
        if (existing != null)
        {
            Debug.Log("[MatchSetup] Match object already present.");
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        var go = new GameObject("Match");
        Undo.RegisterCreatedObjectUndo(go, "Set Up Match");

        var bootstrap = Undo.AddComponent<MatchBootstrap>(go);
        bootstrap.config = LoadOrCreateConfig();
        bootstrap.map = Object.FindFirstObjectByType<BackroomsMapGenerator>();
        bootstrap.player = Object.FindFirstObjectByType<FirstPersonController>();

        EditorUtility.SetDirty(go);
        Selection.activeGameObject = go;

        Debug.Log("[MatchSetup] Match object added. Press play: F1 swaps side, F2 restarts, F5 takes a hit.");
    }

    [MenuItem("Tools/Backrooms/Create Game Config Asset", priority = 21)]
    public static void CreateConfigAsset()
    {
        GameConfig config = LoadOrCreateConfig();
        Selection.activeObject = config;
        EditorGUIUtility.PingObject(config);
    }

    private static GameConfig LoadOrCreateConfig()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
        if (existing != null) return existing;

        string directory = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
        }

        var config = ScriptableObject.CreateInstance<GameConfig>();
        AssetDatabase.CreateAsset(config, ConfigPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[MatchSetup] Created {ConfigPath}.");
        return config;
    }
}
