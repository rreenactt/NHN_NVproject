using UnityEngine;

/// <summary>
/// Animates the block character entirely in code — there is no Animator, no clip and no
/// blend tree anywhere in this project any more. Every joint's local rotation is composed
/// from scratch each frame, which is only practical because the figure is rigid blocks.
///
/// The one relation that makes the walk read as real:
///
///     a rotating leg of length L, swinging by A radians at angular rate w, moves its
///     foot backwards through mid-stance at L * A * w. Set that equal to the body's
///     actual speed v and the planted foot tracks the ground exactly:
///
///         A = v / (L * w)
///
/// So the stride is *derived from measured speed*, never guessed. Cadence is picked first
/// (a natural 0.95-1.55 strides/sec), the swing angle follows, and if that angle would
/// become a splits the cadence rises instead of the stride widening. This is why the feet
/// do not skate — the old rig needed baked root motion to get the same result.
///
/// Everything else is the naturalness budget: the swing axis is derived from the movement
/// direction so strafing side-steps and walking backwards reverses on its own, arms lag the
/// legs slightly the way real arms do, the body drops as the legs split, it rolls once per
/// stride, leans into speed, and the head counter-rotates part of that motion so the gaze
/// stays level. Idle breathes, jumps tuck, landings squash.
///
/// Put on the Player, alongside <see cref="BlockRig"/> and <see cref="FirstPersonController"/>.
/// </summary>
[DefaultExecutionOrder(100)]   // after the controller has moved and set the camera pitch
public class BlockCharacterAnimator : MonoBehaviour
{
    [Header("References")]
    public BlockRig rig;
    public FirstPersonController controller;
    [Tooltip("Optional lower-the-weapon envelope, used by reloading and weapon swaps.")]
    public ProceduralReload weaponLower;

    [Header("Gait")]
    [Tooltip("Strides per second at walking pace. One stride is a full left+right cycle.")]
    public float walkStrideRate = 0.95f;
    [Tooltip("Strides per second at full sprint.")]
    public float sprintStrideRate = 1.55f;
    [Tooltip("Widest the legs may open, in degrees. Past this the cadence rises instead, " +
             "so the no-slide relation still holds without the character doing the splits.")]
    public float maxLegSwing = 52f;
    [Tooltip("Arm swing as a fraction of leg swing.")]
    [Range(0f, 1.5f)] public float armSwingRatio = 0.75f;
    [Tooltip("How far the arms lag the legs, in radians of stride phase. Real arm swing " +
             "trails the legs slightly; zero looks like clockwork.")]
    public float armPhaseLag = 0.18f;
    [Tooltip("Seconds for the stride to spin up and settle out. Stops legs freezing mid-step.")]
    public float gaitSmoothing = 0.12f;

    [Header("Body motion")]
    [Tooltip("How much of the *geometric* hip drop to actually apply. Kneeless legs would " +
             "drop ~0.23 m at full stride, which bounces absurdly — a quarter of it reads right.")]
    [Range(0f, 1f)] public float bobAmount = 0.25f;
    [Tooltip("Share of the body's bob passed on to the camera. Keep small; more is nauseating.")]
    [Range(0f, 1f)] public float cameraBobRatio = 0.25f;
    [Tooltip("Degrees of roll, once per stride, as weight shifts from foot to foot.")]
    public float lateralSway = 2.5f;
    [Tooltip("Degrees the torso leans forward at full sprint.")]
    public float sprintLean = 8f;
    [Tooltip("Degrees the body banks into a sideways step.")]
    public float strafeLean = 4f;

    [Header("Idle")]
    public float breathRate = 0.22f;
    [Tooltip("Degrees of torso rise and fall while standing still.")]
    public float breathAmount = 1.1f;

    [Header("Air and landing")]
    [Tooltip("Degrees the leading leg tucks up while rising.")]
    public float jumpTuck = 26f;
    [Tooltip("Degrees the legs spread while falling.")]
    public float fallSpread = 15f;
    [Tooltip("How far the body compresses on landing, as a fraction of leg length.")]
    [Range(0f, 0.4f)] public float landSquash = 0.13f;
    public float landRecovery = 0.3f;

    [Header("Aim")]
    [Tooltip("Share of the look pitch the head takes. The rest of the look is simply not shown " +
             "on the body — your own camera culls it anyway, this is for the mirror.")]
    [Range(0f, 1f)] public float headShare = 0.7f;
    [Tooltip("Share of the look pitch the torso takes when looking up.")]
    [Range(0f, 1f)] public float torsoShareUp = 0.3f;
    [Tooltip("Share when looking down. Lower — a deep bend folds the torso into the camera.")]
    [Range(0f, 1f)] public float torsoShareDown = 0.12f;
    [Tooltip("How much of the body's own sway the head cancels so the gaze stays level.")]
    [Range(0f, 1f)] public float headStabilise = 0.55f;

    [Header("Arm poses (directions the arm points, in body / camera space)")]
    [Tooltip("Right arm while holding the pistol. The y sets how far below level the arm points — " +
             "raising it (toward 0) also pushes the hand further from the lens, which shrinks the " +
             "arms on screen as well as lifting them.")]
    public Vector3 armedDirectionR = new Vector3(-0.12f, -0.155f, 1f);
    [Tooltip("Left arm while holding the pistol. Angled hard inward: the left shoulder is a " +
             "third of a metre off-centre, so anything gentler leaves that arm stranded against " +
             "the edge of the screen instead of on the grip.")]
    public Vector3 armedDirectionL = new Vector3(0.80f, -0.170f, 1f);
    [Tooltip("Where the arms go while reloading or swapping: down and in toward the hip.")]
    public Vector3 loweredDirection = new Vector3(0.12f, -1f, 0.3f);
    [Tooltip("Seconds to raise or lower the weapon pose when arming or disarming.")]
    public float armedBlendTime = 0.18f;

    [Header("Body arms (what the mirror and other players see)")]
    [Tooltip("How much of the walk's arm swing survives into the held-weapon pose on the body. " +
             "0 welds the arms to the gun, which reads as a mannequin sliding around.")]
    [Range(0f, 1f)] public float armedBodySway = 0.55f;

    [Tooltip("Share of the look pitch the body's arms take, on top of the torso's share, so " +
             "the character visibly raises and lowers the gun as you aim.")]
    [Range(0f, 1f)] public float armAimFollow = 0.5f;

    [Header("Barrel aim")]
    [Tooltip("How firmly the pistol turns onto whatever the crosshair is on. 1 lines the barrel " +
             "up with the shot exactly, so the tracer leaves the muzzle in a straight line.")]
    [Range(0f, 1f)] public float barrelAlign = 1f;

    [Tooltip("Furthest the gun may turn away from the hand's natural pose, in degrees. Aiming at " +
             "something very close needs a big angle — this stops the pistol wrenching sideways.")]
    public float barrelAlignClamp = 28f;

    [Tooltip("Seconds for the barrel to settle onto a new aim distance.")]
    public float barrelSmoothing = 0.07f;

    [Tooltip("Weapon that reports the aim point. Found on this object if left empty.")]
    public WeaponController weapon;

    [Header("Recoil")]
    [Tooltip("Degrees the hand flicks up on each shot. Deliberately tiny — this should read as " +
             "a twitch, not a punch.")]
    public float recoilKick = 3.2f;

    [Tooltip("Metres the hands push back toward the shoulder on each shot.")]
    public float recoilPush = 0.014f;

    [Tooltip("Seconds to reach the top of the kick. Short: the rise is the part that reads as a bang.")]
    public float recoilRise = 0.035f;

    [Tooltip("Seconds to settle back down. Longer than the rise, or it looks like a bounce.")]
    public float recoilFall = 0.17f;

    [Tooltip("Share of the kick the body's arms take, for the mirror and other players.")]
    [Range(0f, 1f)] public float recoilBodyShare = 0.7f;

    /// <summary>Set by the weapon switcher. Drives the held-weapon arm pose.</summary>
    public bool Armed { get; set; } = true;

    private float _phase;             // stride phase in radians
    private float _swing;             // current leg swing amplitude, radians
    private float _swingVelocity;
    private float _speed;             // smoothed planar speed
    private float _speedVelocity;
    private Vector3 _swingAxis = Vector3.right;
    private float _armedWeight = 1f;
    private float _armedVelocity;
    private float _strafe, _strafeVelocity;

    private float _landTimer;
    private bool _wasGrounded = true;
    private float _airTime;
    private float _aimDistance = 10f;
    private float _aimDistanceVelocity;

    private float _recoilElapsed = -1f;   // negative = no shot in flight
    private float _recoilWeight;

    /// <summary>How far through the recoil kick we currently are, 0..1. Read by the crosshair.</summary>
    public float RecoilWeight => _recoilWeight;

    /// <summary>
    /// Stride phase in radians, 0..2π. <c>Sin</c> of it is the leg swing, so it crosses zero
    /// exactly at each footfall — which is what <see cref="FootstepAudio"/> listens for rather
    /// than running a timer of its own. A step you hear at a moment the foot is not touching the
    /// floor is worse than no step sound at all.
    /// </summary>
    public float StridePhase => _phase;

    /// <summary>Current leg swing amplitude in radians. Near zero means the legs are not walking.</summary>
    public float StrideSwing => _swing;

    /// <summary>
    /// Called by the weapon on each shot. Restarts the kick from the top rather than adding to
    /// it, so holding the trigger gives an even chatter instead of the hands climbing away.
    /// </summary>
    public void AddRecoil()
    {
        _recoilElapsed = 0f;
    }

    private Vector3 _cameraBasePosition;
    private bool _haveCameraBase;

    private void Awake()
    {
        if (rig == null) rig = GetComponent<BlockRig>();
        if (controller == null) controller = GetComponent<FirstPersonController>();
        if (weaponLower == null) weaponLower = GetComponent<ProceduralReload>();
        if (weapon == null) weapon = GetComponent<WeaponController>();
    }

    private void Start()
    {
        _armedWeight = Armed ? 1f : 0f;
        if (controller != null && controller.cameraTransform != null)
        {
            _cameraBasePosition = controller.cameraTransform.localPosition;
            _haveCameraBase = true;
        }
    }

    private void LateUpdate()
    {
        if (rig == null || controller == null || rig.Hips == null) return;

        UpdateGait();
        UpdateStateTimers();

        float lowered = weaponLower != null ? weaponLower.Weight : 0f;

        PoseBodyAndLegs();
        PoseArms(lowered);
        PoseViewmodel(lowered);
        AlignBarrels(lowered);
    }

    /// <summary>
    /// Turns both pistols onto the aim point so the barrel is collinear with the shot. The
    /// aim *distance* is what gets smoothed rather than the point itself: glancing from a
    /// near wall to the sky changes the point by 100 m but should only swing the gun a few
    /// degrees, and damping the distance keeps that motion sane.
    /// </summary>
    private void AlignBarrels(float lowered)
    {
        if (weapon == null || barrelAlign <= 0f) return;
        if (controller.cameraTransform == null) return;

        _aimDistance = Mathf.SmoothDamp(_aimDistance, weapon.AimDistance, ref _aimDistanceVelocity, barrelSmoothing);

        Transform camera = controller.cameraTransform;
        Vector3 aimPoint = camera.position + camera.forward * _aimDistance;

        // Fade the alignment out while the weapon is lowered or stowed, so the gun is not
        // still tracking the crosshair from down by the hip.
        float weight = barrelAlign * _armedWeight * (1f - lowered);
        if (weight <= 0.001f) return;

        PointBarrelAt(rig.ViewWeapon, aimPoint, weight);
        PointBarrelAt(rig.BodyWeapon, aimPoint, weight);
    }

    /// <summary>
    /// Orients the pistol straight at the aim point, in world space, ignoring the arm. The gun
    /// cannot inherit the hand's orientation: the barrel is perpendicular to a rigid arm block,
    /// so an arm reaching forward would force the barrel to point up or sideways. Aiming it
    /// directly is both simpler and the only thing that makes the shot leave the muzzle straight.
    /// </summary>
    private void PointBarrelAt(Transform gun, Vector3 aimPoint, float weight)
    {
        if (gun == null || gun.parent == null) return;

        Vector3 toTarget = aimPoint - gun.position;
        if (toTarget.sqrMagnitude < 1e-6f) return;

        Vector3 reference = controller.cameraTransform != null
            ? controller.cameraTransform.forward
            : transform.forward;

        // Cap how far the gun may point away from the look direction, so aiming at something
        // almost touching the muzzle does not wrench the pistol sideways.
        float angle = Vector3.Angle(reference, toTarget);
        Vector3 aimDirection = angle > barrelAlignClamp && angle > 1e-4f
            ? Vector3.Slerp(reference.normalized, toTarget.normalized, barrelAlignClamp / angle)
            : toTarget;

        // The up reference must be the CAMERA's up, not world up. Look far enough up and the
        // barrel approaches world up itself; LookRotation's two arguments become parallel, the
        // roll is then undefined, and the pistol spins on screen — measured at -131° of roll by
        // 82° of look pitch. Camera up is always near-perpendicular to where the gun points,
        // since the barrel is clamped to within barrelAlignClamp of the look direction.
        Vector3 upReference = controller.cameraTransform != null
            ? controller.cameraTransform.up
            : Vector3.up;

        Quaternion aimed = Quaternion.LookRotation(aimDirection, upReference);

        // The muzzle rises with the hand. This lands after the shot has already been traced
        // from last frame's orientation, so the kick never pulls the bullet off target.
        if (_recoilWeight > 0f)
        {
            Vector3 axis = controller.cameraTransform != null
                ? controller.cameraTransform.right
                : transform.right;
            aimed = Quaternion.AngleAxis(-recoilKick * _recoilWeight, axis) * aimed;
        }

        gun.rotation = Quaternion.Slerp(gun.parent.rotation, aimed, weight);
    }

    /// <summary>
    /// Turns measured speed into a stride. See the class comment for why the swing angle is
    /// solved from speed rather than authored — it is what keeps the feet planted.
    /// </summary>
    private void UpdateGait()
    {
        float rawSpeed = controller.PlanarSpeed;
        _speed = Mathf.SmoothDamp(_speed, rawSpeed, ref _speedVelocity, gaitSmoothing);

        float legLength = Mathf.Max(1e-4f, rig.LimbLength);
        float targetSwing = 0f;
        float omega = 0f;

        if (_speed > 0.05f)
        {
            float sprintSpeed = Mathf.Max(0.1f, controller.sprintSpeed);
            float rate = Mathf.Lerp(walkStrideRate, sprintStrideRate, Mathf.Clamp01(_speed / sprintSpeed));
            omega = rate * 2f * Mathf.PI;

            // A = v / (L * w): the swing that makes the planted foot match ground speed.
            targetSwing = _speed / (legLength * omega);

            float cap = maxLegSwing * Mathf.Deg2Rad;
            if (targetSwing > cap)
            {
                // Too wide — take shorter, quicker steps instead of opening the legs further.
                targetSwing = cap;
                omega = _speed / (legLength * cap);
            }
        }

        _swing = Mathf.SmoothDamp(_swing, targetSwing, ref _swingVelocity, gaitSmoothing);

        // Keep stepping while the gait eases out, so the legs finish their stride.
        if (omega <= 0f && _swing > 0.01f) omega = walkStrideRate * 2f * Mathf.PI;
        _phase = Mathf.Repeat(_phase + omega * Time.deltaTime, Mathf.PI * 2f);

        // The swing axis follows the direction of travel, so a sideways step abducts the legs
        // and walking backwards reverses the cycle — both for free, with no extra states.
        Vector2 input = controller.MoveInput;
        if (input.sqrMagnitude > 1e-4f)
        {
            Vector3 localDirection = new Vector3(input.x, 0f, input.y).normalized;
            Vector3 axis = Vector3.Cross(Vector3.up, localDirection);
            if (axis.sqrMagnitude > 1e-6f)
                _swingAxis = Vector3.Slerp(_swingAxis, axis.normalized, 1f - Mathf.Exp(-12f * Time.deltaTime));
        }

        _strafe = Mathf.SmoothDamp(_strafe, input.x, ref _strafeVelocity, 0.15f);
    }

    private void UpdateStateTimers()
    {
        bool grounded = controller.IsGrounded;

        if (grounded && !_wasGrounded)
        {
            // Squash scaled by how hard the landing was, capped so a hop is not a faceplant.
            float impact = Mathf.Clamp01(Mathf.Abs(controller.VerticalVelocity) / 9f);
            _landTimer = landRecovery * Mathf.Max(0.35f, impact);
            _airTime = 0f;
        }

        if (!grounded) _airTime += Time.deltaTime;
        else _airTime = 0f;

        _wasGrounded = grounded;
        if (_landTimer > 0f) _landTimer = Mathf.Max(0f, _landTimer - Time.deltaTime);

        float armedTarget = Armed ? 1f : 0f;
        _armedWeight = Mathf.SmoothDamp(_armedWeight, armedTarget, ref _armedVelocity, armedBlendTime);

        UpdateRecoil();
    }

    /// <summary>
    /// Snappy rise, slower settle, SmoothStep on each leg — the same shape as the reload
    /// envelope and for the same reason: a plain decay crawls out of the extremes.
    /// </summary>
    private void UpdateRecoil()
    {
        if (_recoilElapsed < 0f) { _recoilWeight = 0f; return; }

        _recoilElapsed += Time.deltaTime;
        float rise = Mathf.Max(1e-3f, recoilRise);
        float fall = Mathf.Max(1e-3f, recoilFall);

        if (_recoilElapsed >= rise + fall)
        {
            _recoilElapsed = -1f;
            _recoilWeight = 0f;
            return;
        }

        _recoilWeight = _recoilElapsed < rise
            ? Mathf.SmoothStep(0f, 1f, _recoilElapsed / rise)
            : Mathf.SmoothStep(1f, 0f, (_recoilElapsed - rise) / fall);
    }

    private void PoseBodyAndLegs()
    {
        bool airborne = !controller.IsGrounded && _airTime > 0.06f;
        float stride = Mathf.Sin(_phase);

        // --- Hips: bob, roll, and landing squash ---
        // Splitting the legs geometrically lowers the hips by L*(1-cos A); kneeless legs
        // would make that a pogo stick, so only a fraction is applied.
        float hipDrop = rig.LimbLength * (1f - Mathf.Cos(_swing)) * bobAmount;
        float bob = -hipDrop * (1f - Mathf.Cos(2f * _phase)) * 0.5f;

        float squash = 0f;
        if (_landTimer > 0f)
        {
            // Ease out of the compression rather than snapping back.
            float t = _landTimer / Mathf.Max(1e-3f, landRecovery);
            squash = -rig.LimbLength * landSquash * Mathf.Sin(t * Mathf.PI) * Mathf.Sign(t);
        }

        float roll = -lateralSway * stride * Mathf.Clamp01(_swing / 0.4f) - strafeLean * _strafe;

        rig.Hips.localPosition = new Vector3(0f, rig.HipHeight - rig.GroundOffset + bob + squash, 0f);
        rig.Hips.localRotation = Quaternion.Euler(0f, 0f, roll);

        // --- Torso: speed lean, aim share, breathing ---
        float pitch = controller.Pitch;                       // negative looking up
        float torsoShare = pitch < 0f ? torsoShareUp : torsoShareDown;
        float lean = sprintLean * Mathf.Clamp01(_speed / Mathf.Max(0.1f, controller.sprintSpeed));

        // Breathing only shows through when the body is otherwise still.
        float stillness = 1f - Mathf.Clamp01(_speed / 0.6f);
        float breath = breathAmount * stillness * Mathf.Sin(Time.time * breathRate * 2f * Mathf.PI);

        float torsoPitch = lean + pitch * torsoShare + breath;
        rig.Torso.localRotation = Quaternion.Euler(torsoPitch, 0f, 0f);

        // --- Head: takes the look, then cancels part of the body's own motion so the
        // gaze stays level instead of rolling with every step.
        float headPitch = pitch * headShare - torsoPitch * headStabilise;
        float headRoll = -roll * headStabilise;
        rig.Neck.localRotation = Quaternion.Euler(headPitch, 0f, headRoll);

        // --- Legs ---
        if (airborne)
        {
            bool rising = controller.VerticalVelocity > 0.5f;
            float lead = rising ? jumpTuck : fallSpread;
            float trail = rising ? -jumpTuck * 0.45f : -fallSpread * 0.7f;
            float blend = Mathf.Clamp01(_airTime / 0.18f);

            rig.LegR.localRotation = Quaternion.Slerp(Quaternion.identity,
                Quaternion.AngleAxis(lead, Vector3.right), blend);
            rig.LegL.localRotation = Quaternion.Slerp(Quaternion.identity,
                Quaternion.AngleAxis(trail, Vector3.right), blend);
        }
        else
        {
            float swingDegrees = _swing * Mathf.Rad2Deg;
            float legBend = _landTimer > 0f
                ? 18f * Mathf.Sin(_landTimer / Mathf.Max(1e-3f, landRecovery) * Mathf.PI)
                : 0f;

            rig.LegR.localRotation = Quaternion.AngleAxis(swingDegrees * stride, _swingAxis)
                                   * Quaternion.AngleAxis(legBend, Vector3.right);
            rig.LegL.localRotation = Quaternion.AngleAxis(-swingDegrees * stride, _swingAxis)
                                   * Quaternion.AngleAxis(legBend, Vector3.right);
        }
    }

    private void PoseArms(float lowered)
    {
        // Arms trail the legs a touch and swing opposite them.
        float armStride = Mathf.Sin(_phase - armPhaseLag);
        float freeSwing = _swing * Mathf.Rad2Deg * armSwingRatio;

        // Free-swinging arms hang and counter-rotate; the held pose points them at the gun.
        Quaternion swingR = Quaternion.AngleAxis(-freeSwing * armStride, _swingAxis);
        Quaternion swingL = Quaternion.AngleAxis(freeSwing * armStride, _swingAxis);

        // Armed, the arms track where you are looking on top of the torso's own share, so the
        // character visibly raises and lowers the gun instead of aiming from a fixed pose.
        Quaternion aimLift = Quaternion.AngleAxis(controller.Pitch * armAimFollow, Vector3.right);

        Quaternion heldR = aimLift * AimLimb(armedDirectionR);
        Quaternion heldL = aimLift * AimLimb(armedDirectionL);

        // The arms are committed to the grip, but keeping some of the stride in them is what
        // stops the body reading as a mannequin being slid around the level.
        heldR = Quaternion.Slerp(heldR, swingR * heldR, armedBodySway);
        heldL = Quaternion.Slerp(heldL, swingL * heldL, armedBodySway);

        Quaternion poseR = Quaternion.Slerp(swingR, heldR, _armedWeight);
        Quaternion poseL = Quaternion.Slerp(swingL, heldL, _armedWeight);

        if (lowered > 0f)
        {
            Quaternion low = AimLimb(loweredDirection);
            poseR = Quaternion.Slerp(poseR, low, lowered);
            poseL = Quaternion.Slerp(poseL, low, lowered);
        }

        // Negative about local X raises the hand: the arm's length axis pitches toward forward.
        if (_recoilWeight > 0f)
        {
            Quaternion kick = Quaternion.AngleAxis(
                -recoilKick * recoilBodyShare * _recoilWeight * _armedWeight, Vector3.right);
            poseR = kick * poseR;
            poseL = kick * poseL;
        }

        rig.ArmR.localRotation = poseR;
        rig.ArmL.localRotation = poseL;
    }

    /// <summary>
    /// The viewmodel arms hold the same poses, but in camera space — their root rides the
    /// camera, so the pose that points "forward" always points out of the screen. The walk
    /// only sways them; a full swing this close to the lens is unreadable.
    /// </summary>
    private void PoseViewmodel(float lowered)
    {
        if (rig.ViewArmR == null) return;

        float armStride = Mathf.Sin(_phase - armPhaseLag);
        float sway = _swing * Mathf.Rad2Deg * armSwingRatio * 0.3f;

        Quaternion swayR = Quaternion.AngleAxis(-sway * armStride, Vector3.right);
        Quaternion swayL = Quaternion.AngleAxis(sway * armStride, Vector3.right);

        Quaternion heldR = swayR * AimLimb(armedDirectionR);
        Quaternion heldL = swayL * AimLimb(armedDirectionL);

        // Unarmed, the arms drop out of frame instead of waving in front of the lens.
        Quaternion downR = AimLimb(new Vector3(0.15f, -1f, 0.12f));
        Quaternion downL = AimLimb(new Vector3(-0.15f, -1f, 0.12f));

        Quaternion poseR = Quaternion.Slerp(downR, heldR, _armedWeight);
        Quaternion poseL = Quaternion.Slerp(downL, heldL, _armedWeight);

        if (lowered > 0f)
        {
            Quaternion low = AimLimb(loweredDirection);
            poseR = Quaternion.Slerp(poseR, low, lowered);
            poseL = Quaternion.Slerp(poseL, low, lowered);
        }

        if (_recoilWeight > 0f)
        {
            Quaternion kick = Quaternion.AngleAxis(-recoilKick * _recoilWeight * _armedWeight, Vector3.right);
            poseR = kick * poseR;
            poseL = kick * poseL;
        }

        rig.ViewArmR.localRotation = poseR;
        rig.ViewArmL.localRotation = poseL;

        // A little push straight back toward the shoulder sells the kick more than angle alone.
        rig.ViewRoot.localPosition = rig.viewmodelOffset
            + Vector3.back * (recoilPush * _recoilWeight * _armedWeight);

        ApplyCameraBob();
    }

    private void ApplyCameraBob()
    {
        if (!_haveCameraBase || cameraBobRatio <= 0f) return;

        float hipDrop = rig.LimbLength * (1f - Mathf.Cos(_swing)) * bobAmount;
        float bob = -hipDrop * (1f - Mathf.Cos(2f * _phase)) * 0.5f * cameraBobRatio;

        // Horizontal figure-of-eight, once per stride, so the sway is not purely vertical.
        float lateral = hipDrop * 0.35f * cameraBobRatio * Mathf.Sin(_phase);

        controller.cameraTransform.localPosition = _cameraBasePosition + new Vector3(lateral, bob, 0f);
    }

    /// <summary>
    /// Rotation that points a limb's length axis (its local -Y) along <paramref name="direction"/>,
    /// in the joint's parent space. The roll it happens to pick is deliberately not controlled:
    /// every limb block has a **square** cross-section, so rolling one is invisible.
    ///
    /// What must *not* happen is a held weapon inheriting that arbitrary roll — that is what had
    /// the pistol's barrel aiming 74° off, very nearly straight up. Trying to pin the roll here
    /// instead is worse: <c>LookRotation</c> prioritises its forward argument and orthogonalises
    /// the length axis away, which left the arm hanging straight down. The gun is therefore
    /// oriented independently in <see cref="PointBarrelAt"/> and ignores the arm entirely.
    /// </summary>
    private static Quaternion AimLimb(Vector3 direction)
    {
        if (direction.sqrMagnitude < 1e-6f) return Quaternion.identity;
        return Quaternion.FromToRotation(Vector3.down, direction.normalized);
    }
}
