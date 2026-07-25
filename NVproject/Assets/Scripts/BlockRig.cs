using UnityEngine;

/// <summary>
/// Builds the player as a Minecraft-style figure out of plain white cubes, in code.
/// No model, no skinning, no Mecanim — every joint is an empty transform with one
/// stretched cube hanging off it, which is all a blocky character needs.
///
/// Proportions are the Minecraft player's, in its own 16-per-block pixel grid, scaled
/// so the whole figure is <see cref="totalHeight"/> tall:
///
///     head  8 x 8  x 8      torso 8 x 12 x 4      arm / leg 4 x 12 x 4
///     legs 12 + torso 12 + head 8 = 32 px tall, eyes at 28.8 px
///
/// At 1.8 m that puts the eyes at 1.62 m, which is where the camera already sits.
///
/// Every limb's pivot is at its *joint*, not its centre: the cube is offset half its
/// length below the pivot so rotating the joint swings the limb from the shoulder or
/// hip like a real one. Getting this wrong makes limbs orbit their own middle, which
/// is the usual reason a blocky walk looks broken.
///
/// Two copies of the arms get built. The body pair lives on the <see cref="bodyLayer"/>
/// so the first-person camera culls it while the mirror still sees a whole character;
/// the viewmodel pair is parented to the camera on <see cref="armsLayer"/> so it stays
/// framed on screen at any look angle. Same idea as the old skinned viewmodel rig, but
/// there are no bones to clone — the animator just poses both pairs.
///
/// Put this on the Player. It builds during Awake, before anything reads the joints.
/// </summary>
public class BlockRig : MonoBehaviour
{
    [Header("References")]
    [Tooltip("First-person camera. The viewmodel arms are parented to it.")]
    public Transform cameraTransform;

    [Tooltip("Material for every block. A plain white URP Lit material is created at " +
             "runtime if this is left empty.")]
    public Material blockMaterial;

    [Header("Proportions")]
    [Tooltip("Total height of the figure in metres, feet to top of head.")]
    public float totalHeight = 1.8f;

    [Tooltip("Gap left between neighbouring blocks, as a fraction of a block. Keeps the " +
             "silhouette readable when everything is the same flat white.")]
    [Range(0f, 0.1f)] public float seam = 0.02f;

    [Header("Layers")]
    [Tooltip("Layer for the body — culled by your own camera, visible to the mirror.")]
    public int bodyLayer = 9;
    [Tooltip("Layer for the camera-parented viewmodel arms.")]
    public int armsLayer = 8;

    [Header("Viewmodel framing")]
    [Tooltip("Where the viewmodel arm root sits relative to the camera. This is the framing: " +
             "it decides where the gun and hands land on screen. Solved numerically, not guessed.")]
    public Vector3 viewmodelOffset = new Vector3(-0.1692f, -0.0028f, 0.1027f);

    // --- Body joints (all rotate about their own pivot) ---
    public Transform Hips { get; private set; }
    public Transform Torso { get; private set; }
    public Transform Neck { get; private set; }
    public Transform ArmL { get; private set; }
    public Transform ArmR { get; private set; }
    public Transform LegL { get; private set; }
    public Transform LegR { get; private set; }
    public Transform HandR { get; private set; }

    // --- Viewmodel joints ---
    public Transform ViewRoot { get; private set; }
    public Transform ViewArmL { get; private set; }
    public Transform ViewArmR { get; private set; }
    public Transform ViewHandR { get; private set; }

    /// <summary>The pistol carried on the body — what the mirror shows.</summary>
    public Transform BodyWeapon { get; private set; }
    /// <summary>The pistol you actually see, on the viewmodel arm.</summary>
    public Transform ViewWeapon { get; private set; }

    // Derived from the pixel grid at Build, so the animator can reason in metres.
    public float LimbLength { get; private set; }
    public float ShoulderHeight { get; private set; }
    public float HipHeight { get; private set; }

    /// <summary>
    /// Downward shim that plants the feet on the floor. A CharacterController comes to rest
    /// its own <c>skinWidth</c> above the ground, so a body hung straight off the transform
    /// floats by exactly that much — 8 cm with Unity's default, which is plainly visible.
    /// </summary>
    public float GroundOffset { get; private set; }

    private bool _built;

    private void Awake()
    {
        Build();
    }

    public void Build()
    {
        if (_built) return;
        if (blockMaterial == null) blockMaterial = CreateWhiteMaterial();

        // One pixel of the Minecraft grid, in metres. The figure is 32 px tall.
        float px = totalHeight / 32f;

        LimbLength = 12f * px;          // arms and legs are both 12 px long
        HipHeight = 12f * px;           // hips sit at the top of the legs
        ShoulderHeight = 24f * px;      // top of the torso

        Vector3 torsoSize = new Vector3(8f, 12f, 4f) * px;
        Vector3 limbSize = new Vector3(4f, 12f, 4f) * px;
        Vector3 headSize = new Vector3(8f, 8f, 8f) * px;

        var capsule = GetComponent<CharacterController>();
        GroundOffset = capsule != null ? capsule.skinWidth : 0f;

        // Hips carry the whole figure, so the animator can bob and squash it in one place.
        Hips = NewJoint("Hips", transform, new Vector3(0f, HipHeight - GroundOffset, 0f));

        Torso = NewJoint("Torso", Hips, Vector3.zero);
        AddBlock(Torso, "Torso Block", new Vector3(0f, torsoSize.y * 0.5f, 0f), torsoSize, bodyLayer);

        // Head pivots at the neck, so looking up and down rotates it about the collar.
        Neck = NewJoint("Neck", Torso, new Vector3(0f, torsoSize.y, 0f));
        AddBlock(Neck, "Head Block", new Vector3(0f, headSize.y * 0.5f, 0f), headSize, bodyLayer);

        // Arms hang from the top corners of the torso; legs from the hips, side by side.
        float armX = (torsoSize.x + limbSize.x) * 0.5f;
        float legX = limbSize.x * 0.5f;

        ArmR = NewLimb("Arm R", Torso, new Vector3(armX, torsoSize.y, 0f), limbSize, bodyLayer);
        ArmL = NewLimb("Arm L", Torso, new Vector3(-armX, torsoSize.y, 0f), limbSize, bodyLayer);
        LegR = NewLimb("Leg R", Hips, new Vector3(legX, 0f, 0f), limbSize, bodyLayer);
        LegL = NewLimb("Leg L", Hips, new Vector3(-legX, 0f, 0f), limbSize, bodyLayer);

        HandR = NewJoint("Hand R", ArmR, new Vector3(0f, -LimbLength, 0f));
        BodyWeapon = BuildPistol("Pistol", HandR, px, bodyLayer);

        BuildViewmodel(px, limbSize);

        _built = true;
    }

    /// <summary>
    /// The arms you see. Anatomically the shoulders sit below and behind the lens, so a
    /// faithful copy would hang out of frame entirely — <see cref="viewmodelOffset"/> is
    /// what lifts the pair into shot. Because the root rides the camera, the arms hold
    /// their place on screen however far you pitch.
    /// </summary>
    private void BuildViewmodel(float px, Vector3 limbSize)
    {
        if (cameraTransform == null) return;

        ViewRoot = NewJoint("Viewmodel Arms", cameraTransform, viewmodelOffset);

        // Mirror the body's shoulder spacing, measured from the eye rather than the hips.
        float armX = (8f + 4f) * px * 0.5f;
        float shoulderDrop = ShoulderHeight - EyeHeight(px);

        ViewArmR = NewLimb("View Arm R", ViewRoot, new Vector3(armX, shoulderDrop, 0f), limbSize, armsLayer);
        ViewArmL = NewLimb("View Arm L", ViewRoot, new Vector3(-armX, shoulderDrop, 0f), limbSize, armsLayer);

        ViewHandR = NewJoint("View Hand R", ViewArmR, new Vector3(0f, -LimbLength, 0f));
        ViewWeapon = BuildPistol("Pistol (Viewmodel)", ViewHandR, px, armsLayer);

        // The viewmodel is a screen-space prop, not a real object: it must not cast into
        // the world or it would throw arm shadows from nowhere.
        foreach (var renderer in ViewRoot.GetComponentsInChildren<MeshRenderer>(true))
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>Minecraft eye height: 28.8 of the 32 px, i.e. near the top of the head.</summary>
    private float EyeHeight(float px) => 28.8f * px;

    /// <summary>
    /// A blocky pistol, since the character it belongs to is blocky too. Built rather than
    /// referenced so nothing depends on the old imported model's child objects.
    /// </summary>
    private Transform BuildPistol(string name, Transform parent, float px, int layer)
    {
        Transform pistol = NewJoint(name, parent, Vector3.zero);

        // Sized against the 4 px hand: a grip you can see and a slide that reads as a barrel.
        AddBlock(pistol, "Grip", new Vector3(0f, -1.2f * px, 0f), new Vector3(1.6f, 3f, 1.6f) * px, layer);
        AddBlock(pistol, "Slide", new Vector3(0f, 0.7f * px, 1.9f * px), new Vector3(1.6f, 1.6f, 5f) * px, layer);

        Transform muzzle = NewJoint("Muzzle", pistol, new Vector3(0f, 0.7f * px, 4.4f * px));
        muzzle.gameObject.layer = layer;
        return pistol;
    }

    /// <summary>A joint plus the cube that hangs below it, offset so it swings from the pivot.</summary>
    private Transform NewLimb(string name, Transform parent, Vector3 localPosition, Vector3 size, int layer)
    {
        Transform joint = NewJoint(name, parent, localPosition);
        AddBlock(joint, name + " Block", new Vector3(0f, -size.y * 0.5f, 0f), size, layer);
        return joint;
    }

    private Transform NewJoint(string name, Transform parent, Vector3 localPosition)
    {
        var joint = new GameObject(name).transform;
        joint.SetParent(parent, false);
        joint.localPosition = localPosition;
        joint.localRotation = Quaternion.identity;
        return joint;
    }

    private void AddBlock(Transform parent, string name, Vector3 localCentre, Vector3 size, int layer)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.layer = layer;

        // Nothing on the character may be hit by its own shots, and the CharacterController
        // already handles collision — so the visual blocks carry no colliders at all.
        var boxCollider = cube.GetComponent<Collider>();
        if (boxCollider != null) Destroy(boxCollider);

        cube.GetComponent<MeshRenderer>().sharedMaterial = blockMaterial;

        Transform t = cube.transform;
        t.SetParent(parent, false);
        t.localPosition = localCentre;
        t.localRotation = Quaternion.identity;
        t.localScale = size * (1f - seam);
    }

    private static Material CreateWhiteMaterial()
    {
        // URP's Lit shader, since this project renders through URP; Standard would show pink.
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader) { name = "Block White (runtime)" };
        material.color = Color.white;
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.1f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        return material;
    }
}
