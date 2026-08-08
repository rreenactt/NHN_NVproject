using NV.Game;
using UnityEngine;

/// <summary>
/// Builds the player as a figure of plain cubes, in code. No model, no skinning, no Mecanim —
/// every joint is an empty transform with one stretched cube hanging off it, which is all a
/// blocky character needs.
///
/// **Two bodies come out of this class.** With no <see cref="Plan"/> it builds the humanoid —
/// the Minecraft-proportioned white figure every lobby character is painted onto:
///
///     head  8 x 8  x 8      torso 8 x 12 x 4      arm / leg 4 x 12 x 4
///     legs 12 + torso 12 + head 8 = 32 px tall, eyes at 28.8 px
///
/// At 1.8 m that puts the eyes at 1.62 m, which is where the camera already sits. With a
/// <see cref="BodyPlan"/> it builds that monster instead — same joints, same pixel grid, same
/// contract, different flesh. The Seeker's role reveal swaps one for the other via
/// <see cref="Rebuild"/>.
///
/// **The joint contract is what every consumer leans on and every plan must honour**:
/// Hips / Torso / Neck / ArmL / ArmR / LegL / LegR / HandR exist, every limb's pivot is at its
/// *joint* (the cube offset half its length below, so it swings from the shoulder or hip —
/// getting this wrong makes limbs orbit their own middle), and the weapon roots carry a direct
/// child named "Muzzle". The animator, the weapon and the chain all read those and none of
/// them cares which body is wearing them.
///
/// Two copies of the arms get built. The body pair lives on the <see cref="bodyLayer"/>
/// so the first-person camera culls it while the mirror still sees a whole character;
/// the viewmodel pair is parented to the camera on <see cref="armsLayer"/> so it stays
/// framed on screen at any look angle.
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
             "it decides where the gun and hands land on screen. Solved numerically, not guessed. " +
             "A monster plan substitutes its own solve; the humanoid's is restored with the body.")]
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

    /// <summary>The weapon carried on the body — what the mirror shows.</summary>
    public Transform BodyWeapon { get; private set; }
    /// <summary>The weapon you actually see, on the viewmodel arm.</summary>
    public Transform ViewWeapon { get; private set; }

    // Derived from the pixel grid at Build, so the animator can reason in metres.
    /// <summary>Leg length. The gait maths reads this as L, so it must be the LEGS, not the arms.</summary>
    public float LimbLength { get; private set; }
    /// <summary>Arm length. Equal to <see cref="LimbLength"/> on the humanoid; a plan may differ.</summary>
    public float ArmLength { get; private set; }
    public float ShoulderHeight { get; private set; }
    public float HipHeight { get; private set; }

    /// <summary>
    /// Edge of the head cube, in metres. Anything worn on the head sizes itself from this rather
    /// than from a constant, so headgear still fits if the figure's proportions are retuned.
    /// </summary>
    public float HeadSize { get; private set; }

    /// <summary>The monster this body is built as, or null for the humanoid.</summary>
    public BodyPlan Plan { get; private set; }

    /// <summary>
    /// Is this body a monster? <c>CharacterAppearance</c> keeps its lobby paint off while this
    /// is true — the plan owns its colours.
    /// </summary>
    public bool IsMonster => Plan != null;

    /// <summary>
    /// Have the blocks been made yet? The rig builds in <c>Awake</c>, but anything that dresses the
    /// body can arrive first — a networked body is painted from a roster bulletin that may land on
    /// the same frame the puppet is created.
    /// </summary>
    public bool IsBuilt => _built;

    /// <summary>
    /// Downward shim that plants the feet on the floor. A CharacterController comes to rest
    /// its own <c>skinWidth</c> above the ground, so a body hung straight off the transform
    /// floats by exactly that much — 8 cm with Unity's default, which is plainly visible.
    /// </summary>
    public float GroundOffset { get; private set; }

    private bool _built;
    private Vector3 _humanoidViewmodelOffset;
    private bool _capturedViewmodelOffset;

    private void Awake()
    {
        Build();
    }

    public void Build()
    {
        if (_built) return;
        if (blockMaterial == null) blockMaterial = CreateWhiteMaterial();

        // The humanoid's framing solve is whatever the scene serialized; remember it once so a
        // round trip through a monster plan (F1 swaps sides, rematches reassign roles) restores
        // the tuned value rather than the .cs default.
        if (!_capturedViewmodelOffset)
        {
            _humanoidViewmodelOffset = viewmodelOffset;
            _capturedViewmodelOffset = true;
        }
        viewmodelOffset = Plan != null ? Plan.viewmodelOffset : _humanoidViewmodelOffset;

        // One pixel of the grid, in metres. Both bodies are reasoned about as 32 px tall.
        float px = totalHeight / 32f;

        var capsule = GetComponent<CharacterController>();
        GroundOffset = capsule != null ? capsule.skinWidth : 0f;

        if (Plan == null) BuildHumanoid(px);
        else BuildMonster(px, Plan);

        _built = true;
    }

    /// <summary>
    /// Tears the body down and rebuilds it as <paramref name="plan"/> (null = the humanoid).
    /// The role reveal is the caller: a Seeker's body becomes the monster, and a body that
    /// stops being the Seeker becomes the humanoid again.
    ///
    /// Anything that cached a transform out of the old body — the weapon controller keeps the
    /// viewmodel muzzle — holds a destroyed reference afterwards and must rebind. Components
    /// that read the rig's properties every frame (the animator, the switcher) need nothing.
    ///
    /// Returns whether the body actually changed, so the caller only rebinds and repaints on
    /// a real swap — roles are re-announced every match and most announcements change nothing.
    /// </summary>
    public bool Rebuild(BodyPlan plan)
    {
        if (_built && Plan == plan) return false;

        Plan = plan;
        if (!_built)
        {
            // Awake has not run yet — build now so the caller finds the joints in place.
            Build();
            return true;
        }

        TearDown();
        Build();
        return true;
    }

    /// <summary>
    /// Deactivate before Destroy: destruction is deferred to end of frame, and a body that
    /// stays visible (and solid to capsule checks — the objective layer paid for this) for one
    /// extra frame alongside its replacement reads as a ghost double.
    /// </summary>
    private void TearDown()
    {
        if (Hips != null)
        {
            Hips.gameObject.SetActive(false);
            Destroy(Hips.gameObject);
        }
        if (ViewRoot != null)
        {
            ViewRoot.gameObject.SetActive(false);
            Destroy(ViewRoot.gameObject);
        }

        Hips = Torso = Neck = ArmL = ArmR = LegL = LegR = HandR = null;
        ViewRoot = ViewArmL = ViewArmR = ViewHandR = null;
        BodyWeapon = ViewWeapon = null;
        _built = false;
    }

    // ==================================================================== humanoid

    private void BuildHumanoid(float px)
    {
        LimbLength = 12f * px;          // arms and legs are both 12 px long
        ArmLength = LimbLength;
        HipHeight = 12f * px;           // hips sit at the top of the legs
        ShoulderHeight = 24f * px;      // top of the torso

        Vector3 torsoSize = new Vector3(8f, 12f, 4f) * px;
        Vector3 limbSize = new Vector3(4f, 12f, 4f) * px;
        Vector3 headSize = new Vector3(8f, 8f, 8f) * px;
        HeadSize = headSize.x;

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

        DisableViewmodelShadows();
    }

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

    // ==================================================================== monster

    /// <summary>
    /// The plan's body. Same joints, same pivot rule, same 32-px grid — different flesh.
    ///
    /// **The hunch is baked into the blocks, never into a joint's rotation.** The animator
    /// composes every joint's localRotation from scratch each LateUpdate, so a tilt stored in
    /// the Torso joint would be erased on the first frame. The spine block leans inside an
    /// upright joint instead, and the collar, hump, head and shoulders are all placed at the
    /// tilted spine's end in the joint's own space.
    /// </summary>
    private void BuildMonster(float px, BodyPlan plan)
    {
        Material flesh = plan.BodyMaterial;

        LimbLength = plan.legLengthPx * px;
        ArmLength = plan.armLengthPx * px;
        HipHeight = plan.legLengthPx * px;
        HeadSize = plan.headPx * px;

        float tilt = plan.torsoTiltDeg * Mathf.Deg2Rad;
        float spineRise = plan.torsoLengthPx * Mathf.Cos(tilt) * px;    // vertical gain
        float spineReach = plan.torsoLengthPx * Mathf.Sin(tilt) * px;   // forward lean
        ShoulderHeight = HipHeight + spineRise;

        Vector3 torsoSize = new Vector3(plan.torsoWidthPx, plan.torsoLengthPx, plan.torsoDepthPx) * px;
        Vector3 armSize = new Vector3(plan.armThickPx, plan.armLengthPx, plan.armThickPx) * px;
        Vector3 legSize = new Vector3(plan.legThickPx, plan.legLengthPx, plan.legThickPx) * px;

        Hips = NewJoint("Hips", transform, new Vector3(0f, HipHeight - GroundOffset, 0f));

        // The spine: one block leaning forward from an upright joint at the hips.
        Torso = NewJoint("Torso", Hips, Vector3.zero);
        Quaternion lean = Quaternion.Euler(plan.torsoTiltDeg, 0f, 0f);
        AddBlock(Torso, "Spine Block", lean * new Vector3(0f, torsoSize.y * 0.5f, 0f), lean,
            torsoSize, bodyLayer, flesh);

        Vector3 collar = new Vector3(0f, spineRise, spineReach);

        // The hump rides the collar and is the figure's highest point — shoulders above where
        // a head should be is most of what makes the outline read as wrong at a distance.
        if (plan.humpPx > 0f)
        {
            AddBlock(Torso, "Hump",
                collar + new Vector3(0f, plan.humpPx * 0.35f * px, -plan.torsoDepthPx * 0.15f * px),
                lean,
                new Vector3(torsoSize.x * 1.15f, plan.humpPx * px, torsoSize.z * 1.4f),
                bodyLayer, flesh);
        }

        // The head hangs forward of the collar and *below* the hump, too small for the body.
        Neck = NewJoint("Neck", Torso, collar);
        Vector3 headCentre = new Vector3(0f,
            (plan.headPx * 0.5f - plan.headDropPx) * px,
            plan.headPx * 0.35f * px);
        AddBlock(Neck, "Head Block", headCentre, Quaternion.identity,
            new Vector3(HeadSize, HeadSize, HeadSize), bodyLayer, flesh);

        // Eyes: the only lit thing on the body. In this fog two pale points read from further
        // than the whole silhouette does — they are what you see first down a corridor.
        if (plan.eyePx > 0f)
        {
            Material glow = plan.EyeMaterial;
            float eye = plan.eyePx * px;
            AddBlock(Neck, "Eye R", headCentre + new Vector3(HeadSize * 0.22f, HeadSize * 0.1f, HeadSize * 0.5f),
                Quaternion.identity, new Vector3(eye, eye * 0.8f, eye * 0.4f), bodyLayer, glow);
            AddBlock(Neck, "Eye L", headCentre + new Vector3(-HeadSize * 0.22f, HeadSize * 0.1f, HeadSize * 0.5f),
                Quaternion.identity, new Vector3(eye, eye * 0.8f, eye * 0.4f), bodyLayer, glow);
        }

        // Arms hang from the collar's corners — on this body that is above and forward of
        // where human shoulders sit, and they reach the knees.
        float armX = (torsoSize.x + armSize.x) * 0.5f;
        ArmR = NewLimb("Arm R", Torso, collar + new Vector3(armX, 0f, 0f), armSize, bodyLayer, flesh);
        ArmL = NewLimb("Arm L", Torso, collar + new Vector3(-armX, 0f, 0f), armSize, bodyLayer, flesh);

        float legX = legSize.x * 0.6f;
        LegR = NewLimb("Leg R", Hips, new Vector3(legX, 0f, 0f), legSize, bodyLayer, flesh);
        LegL = NewLimb("Leg L", Hips, new Vector3(-legX, 0f, 0f), legSize, bodyLayer, flesh);

        HandR = NewJoint("Hand R", ArmR, new Vector3(0f, -ArmLength, 0f));
        BodyWeapon = BuildBoneBarrel("Bone Barrel", HandR, px, bodyLayer, plan);

        BuildMonsterViewmodel(px, armSize, plan);
    }

    /// <summary>
    /// The monster's own first person: gaunt arms and the grown barrel, framed by the plan's
    /// viewmodel solve. Same mechanics as the humanoid pair — root rides the camera, the
    /// animator poses both pairs identically.
    /// </summary>
    private void BuildMonsterViewmodel(float px, Vector3 armSize, BodyPlan plan)
    {
        if (cameraTransform == null) return;

        Material flesh = plan.BodyMaterial;

        ViewRoot = NewJoint("Viewmodel Arms", cameraTransform, viewmodelOffset);

        float armX = (plan.torsoWidthPx + plan.armThickPx) * px * 0.5f;
        float shoulderDrop = ShoulderHeight - EyeHeight(px);

        ViewArmR = NewLimb("View Arm R", ViewRoot, new Vector3(armX, shoulderDrop, 0f), armSize, armsLayer, flesh);
        ViewArmL = NewLimb("View Arm L", ViewRoot, new Vector3(-armX, shoulderDrop, 0f), armSize, armsLayer, flesh);

        ViewHandR = NewJoint("View Hand R", ViewArmR, new Vector3(0f, -ArmLength, 0f));
        ViewWeapon = BuildBoneBarrel("Bone Barrel (Viewmodel)", ViewHandR, px, armsLayer, plan);

        DisableViewmodelShadows();
    }

    /// <summary>
    /// The Seeker's pistol, grown out of the arm instead of held: a gnarl at the wrist, a
    /// hollow bone spur for a barrel, a hook under it. **The contract is the humanoid
    /// pistol's** — the root is what <c>PointBarrelAt</c> rotates, and the direct child named
    /// "Muzzle" is where the tracer leaves and what the weapon controller rebinds to. The
    /// barrel runs along local +Z from the root for the same reason the slide does.
    /// </summary>
    private Transform BuildBoneBarrel(string name, Transform parent, float px, int layer, BodyPlan plan)
    {
        Transform gun = NewJoint(name, parent, Vector3.zero);
        Material bone = plan.BoneMaterial;

        AddBlock(gun, "Gnarl", new Vector3(0f, 0.3f * px, 0.6f * px), Quaternion.identity,
            new Vector3(2.2f, 2.2f, 2.6f) * px, layer, bone);
        AddBlock(gun, "Barrel", new Vector3(0f, 0.7f * px, 3.2f * px), Quaternion.identity,
            new Vector3(1.4f, 1.4f, 6f) * px, layer, bone);
        AddBlock(gun, "Spur", new Vector3(0f, -0.8f * px, 1.8f * px), Quaternion.identity,
            new Vector3(0.9f, 1.8f, 0.9f) * px, layer, bone);

        Transform muzzle = NewJoint("Muzzle", gun, new Vector3(0f, 0.7f * px, 6.2f * px));
        muzzle.gameObject.layer = layer;
        return gun;
    }

    // ==================================================================== shared pieces

    /// <summary>Minecraft eye height: 28.8 of the 32 px, i.e. near the top of the head. The
    /// camera sits there for every body — the monster stoops around the same lens.</summary>
    private float EyeHeight(float px) => 28.8f * px;

    /// <summary>
    /// The viewmodel is a screen-space prop, not a real object: it must not cast into
    /// the world or it would throw arm shadows from nowhere.
    /// </summary>
    private void DisableViewmodelShadows()
    {
        if (ViewRoot == null) return;
        foreach (var renderer in ViewRoot.GetComponentsInChildren<MeshRenderer>(true))
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>A joint plus the cube that hangs below it, offset so it swings from the pivot.</summary>
    private Transform NewLimb(string name, Transform parent, Vector3 localPosition, Vector3 size, int layer)
        => NewLimb(name, parent, localPosition, size, layer, blockMaterial);

    private Transform NewLimb(string name, Transform parent, Vector3 localPosition, Vector3 size,
        int layer, Material material)
    {
        Transform joint = NewJoint(name, parent, localPosition);
        AddBlock(joint, name + " Block", new Vector3(0f, -size.y * 0.5f, 0f), Quaternion.identity,
            size, layer, material);
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
        => AddBlock(parent, name, localCentre, Quaternion.identity, size, layer, blockMaterial);

    private void AddBlock(Transform parent, string name, Vector3 localCentre, Quaternion localRotation,
        Vector3 size, int layer, Material material)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.layer = layer;

        // Nothing on the character may be hit by its own shots, and the CharacterController
        // already handles collision — so the visual blocks carry no colliders at all.
        var boxCollider = cube.GetComponent<Collider>();
        if (boxCollider != null) Destroy(boxCollider);

        cube.GetComponent<MeshRenderer>().sharedMaterial = material;

        Transform t = cube.transform;
        t.SetParent(parent, false);
        t.localPosition = localCentre;
        t.localRotation = localRotation;
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
