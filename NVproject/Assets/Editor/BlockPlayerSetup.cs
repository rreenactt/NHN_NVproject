using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click (re)build of the block player. Run via the top menu:
///   Tools ▸ Block Player ▸ Build Block Player
///
/// Replaces whatever character is under the Player with the code-built white block figure:
///
///   Player (CharacterController + FirstPersonController + BlockRig
///           + BlockCharacterAnimator + ProceduralReload + WeaponController + WeaponSwitcher)
///    └─ FP Camera            (child at eye height; culls the PlayerBody layer)
///        └─ Viewmodel Arms   (built at runtime by BlockRig)
///
/// Nothing about the body is authored in the scene — <see cref="BlockRig"/> builds every
/// block during Awake, so this only has to strip the old rig, add the components and wire
/// the references. That is also why there is no prefab to keep in sync.
///
/// Safe to run repeatedly; it reuses the existing Player and camera if they are there.
/// </summary>
public static class BlockPlayerSetup
{
    private const float EyeHeight = 1.62f;        // Minecraft eye height at 1.8 m tall
    private const float ControllerHeight = 1.8f;
    private const string MaterialPath = "Assets/Materials/BlockWhite.mat";

    private const int ArmsLayer = 8;              // FirstPersonArms
    private const int BodyLayer = 9;              // PlayerBody

    [MenuItem("Tools/Block Player/Build Block Player")]
    public static void BuildBlockPlayer()
    {
        GameObject player = GameObject.Find("Player");
        if (player == null)
        {
            player = new GameObject("Player");
            Undo.RegisterCreatedObjectUndo(player, "Create Player");
            player.transform.position = Vector3.zero;
        }

        StripOldCharacter(player);

        var controllerCollider = GetOrAdd<CharacterController>(player);
        controllerCollider.height = ControllerHeight;
        controllerCollider.radius = 0.3f;
        controllerCollider.center = new Vector3(0f, ControllerHeight * 0.5f, 0f);

        Camera camera = EnsureCamera(player);
        Material blockMaterial = EnsureWhiteMaterial();

        // --- Components, in the order they depend on each other -------------
        var firstPerson = GetOrAdd<FirstPersonController>(player);
        firstPerson.cameraTransform = camera.transform;

        var rig = GetOrAdd<BlockRig>(player);
        rig.cameraTransform = camera.transform;
        rig.blockMaterial = blockMaterial;
        rig.totalHeight = ControllerHeight;
        rig.bodyLayer = BodyLayer;
        rig.armsLayer = ArmsLayer;

        var reload = GetOrAdd<ProceduralReload>(player);

        var animator = GetOrAdd<BlockCharacterAnimator>(player);
        animator.rig = rig;
        animator.controller = firstPerson;
        animator.weaponLower = reload;

        var weapon = GetOrAdd<WeaponController>(player);
        weapon.aimCamera = camera;
        weapon.reloadMotion = reload;
        weapon.blockRig = rig;
        weapon.characterAnimator = animator;
        // The body blocks carry no colliders, but the CharacterController does — a shot
        // fired from inside your own capsule would otherwise hit it immediately.
        weapon.hitMask = ~(1 << BodyLayer | 1 << ArmsLayer);

        var switcher = GetOrAdd<WeaponSwitcher>(player);
        switcher.weapon = weapon;
        switcher.switchMotion = reload;
        switcher.blockRig = rig;
        switcher.characterAnimator = animator;

        var crosshair = GetOrAdd<Crosshair>(player);
        crosshair.controller = firstPerson;
        crosshair.weaponSwitcher = switcher;
        crosshair.characterAnimator = animator;
        weapon.crosshair = crosshair;

        FixMirrorLayers();
        EnsureGround();
        EnsureLight();

        EditorUtility.SetDirty(player);
        Selection.activeGameObject = player;

        Debug.Log("[BlockPlayerSetup] Block player wired. Press Play — the body is built in code " +
                  "at Awake. WASD move, mouse look, Space jump, Shift sprint, 1/2 switch weapon, R reload.");
    }

    /// <summary>
    /// Removes the imported character and every component that only existed to drive it.
    /// The old scripts are gone from the project, so these lookups are by name to survive
    /// their absence.
    /// </summary>
    private static void StripOldCharacter(GameObject player)
    {
        // Any child holding a SkinnedMeshRenderer or an Animator is the old model.
        for (int i = player.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = player.transform.GetChild(i);
            bool isOldModel = child.GetComponentInChildren<SkinnedMeshRenderer>(true) != null
                           || child.GetComponentInChildren<Animator>(true) != null;
            if (isOldModel)
                Undo.DestroyObjectImmediate(child.gameObject);
        }

        // Any leftover viewmodel rig built into the scene by hand.
        Transform camera = player.transform.Find("FP Camera");
        if (camera != null)
        {
            for (int i = camera.childCount - 1; i >= 0; i--)
            {
                Transform child = camera.GetChild(i);
                if (child.name.Contains("Viewmodel"))
                    Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        // An Animator directly on the Player would keep trying to drive nothing.
        var strayAnimator = player.GetComponent<Animator>();
        if (strayAnimator != null) Undo.DestroyObjectImmediate(strayAnimator);
    }

    private static Camera EnsureCamera(GameObject player)
    {
        Transform existing = player.transform.Find("FP Camera");
        GameObject cameraGo = existing != null ? existing.gameObject : null;

        if (cameraGo == null)
        {
            cameraGo = new GameObject("FP Camera");
            Undo.RegisterCreatedObjectUndo(cameraGo, "Create FP Camera");
            cameraGo.transform.SetParent(player.transform, false);
            cameraGo.AddComponent<AudioListener>();
        }

        cameraGo.transform.localPosition = new Vector3(0f, EyeHeight, 0f);
        cameraGo.transform.localRotation = Quaternion.identity;
        cameraGo.tag = "MainCamera";

        var camera = GetOrAdd<Camera>(cameraGo);
        // The viewmodel arms sit very close to the lens, so the near plane has to be tight.
        camera.nearClipPlane = 0.02f;

        // **하늘은 실내에 없다.** 두 맵 모두 `RenderSettings.skybox` 를 비우는데, 지우는
        // 것만으로는 부족하다 — clear flags 가 Skybox 로 남아 있으면 스카이박스 재질이
        // 없는 자리를 카메라의 배경색이 메우고, 그 기본값은 유니티의 파란 회색이다.
        // 어두운 복도 끝의 틈이 대낮으로 열린다.
        //
        // 실제 색은 매치가 시작되며 안개색으로 덮인다(`RoleVision.Apply`) — 거리가 배경에서
        // 끝나는 대신 배경으로 녹아야 하기 때문이다. 여기 두는 값은 그 전까지의 것이다.
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.035f, 0.032f, 0.023f, 1f);
        // Your own body is present for the mirror and for shadows, but never for your eyes.
        camera.cullingMask = ~(1 << BodyLayer);

        return camera;
    }

    /// <summary>The mirror must not reflect the viewmodel arms, or they float in the reflection.</summary>
    private static void FixMirrorLayers()
    {
        foreach (var mirror in Object.FindObjectsByType<PlanarMirror>(FindObjectsSortMode.None))
        {
            mirror.reflectLayers &= ~(1 << ArmsLayer);
            EditorUtility.SetDirty(mirror);
        }
    }

    private static Material EnsureWhiteMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");

        // URP's Lit shader — the Standard shader renders pink in this project.
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        var material = new Material(shader);
        material.color = Color.white;
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.1f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);

        AssetDatabase.CreateAsset(material, MaterialPath);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        var component = target.GetComponent<T>();
        if (component == null) component = Undo.AddComponent<T>(target);
        return component;
    }

    private static void EnsureGround()
    {
        foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            if (renderer.gameObject.name == "Ground") return;

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        Undo.RegisterCreatedObjectUndo(ground, "Create Ground");
        ground.name = "Ground";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(5f, 1f, 5f);
    }

    private static void EnsureLight()
    {
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (light.type == LightType.Directional) return;

        var lightGo = new GameObject("Directional Light");
        Undo.RegisterCreatedObjectUndo(lightGo, "Create Light");
        var directional = lightGo.AddComponent<Light>();
        directional.type = LightType.Directional;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }
}
