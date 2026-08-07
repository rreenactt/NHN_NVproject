using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-person controller for the block character.
/// - WASD move, mouse look, Space to jump, Left-Shift to sprint, Esc to release cursor.
/// - Yaw rotates the whole body (so you turn your body); pitch rotates only the camera.
///
/// This moves the CharacterController directly. The previous humanoid rig drove movement
/// from baked root motion so its footsteps would plant; the block character does not need
/// that, because <see cref="BlockCharacterAnimator"/> derives its stride from the speed
/// reported here — the animation follows the movement instead of the movement following
/// the animation, which is both simpler and impossible to desynchronise.
///
/// <see cref="PlanarSpeed"/> is deliberately *measured* from actual displacement rather
/// than taken from the input. Walking into a wall then genuinely stops the stride instead
/// of leaving the legs running on the spot.
///
/// Uses the new Input System low-level API (Keyboard.current / Mouse.current),
/// so no .inputactions asset or PlayerInput component is required.
///
/// It runs in one of three modes (<see cref="controlMode"/>). Offline it moves the
/// CharacterController itself, as it always has. Connected to the game server it stops moving
/// anything — the server owns the position and this only samples input and turns the view —
/// and on a remote player's puppet it samples nothing at all. Only <see cref="HandleMove"/>
/// and the state the animator reads differ between the three; the animator itself cannot tell
/// them apart, which is the point: one pose composer, three sources of motion.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    /// <summary>Who decides where this character is.</summary>
    public enum ControlMode
    {
        /// <summary>Offline. This script moves the CharacterController from local input.</summary>
        Local,

        /// <summary>Our own character on a server. Input is sampled and sent; the position arrives back.</summary>
        NetworkAuthority,

        /// <summary>Somebody else's character. Everything comes off the wire.</summary>
        Remote,
    }

    [Header("References")]
    [Tooltip("The first-person camera. Should be a child of this object, placed at eye height.")]
    public Transform cameraTransform;

    [Header("Look")]
    [Tooltip("Mouse look sensitivity (degrees per input unit).")]
    public float mouseSensitivity = 0.1f;
    [Tooltip("Max up/down look angle in degrees.")]
    public float pitchClamp = 85f;

    [Header("Move")]
    public float walkSpeed = 4f;
    public float sprintSpeed = 7f;
    [Tooltip("Ctrl. Under half of walkSpeed on purpose: sneaking buys total silence, and silence " +
             "has to cost enough that crossing an open room quietly is a real decision. Note the " +
             "scene's walk is 2.5, not the 4 this file defaults to — set both if you change it.")]
    public float sneakSpeed = 1.1f;
    public float jumpHeight = 1.2f;
    public float gravity = -19.62f;

    [Tooltip("How briskly the character reaches target speed, in m/s². Instant response " +
             "reads as weightless; this gives the walk a little mass.")]
    public float acceleration = 45f;
    [Tooltip("Deceleration when the stick is released, in m/s².")]
    public float deceleration = 55f;

    [Header("Networking")]
    [Tooltip("Who decides where this character is. Left at Local the project behaves exactly as " +
             "it does offline; the network bootstrap switches it.")]
    public ControlMode controlMode = ControlMode.Local;

    private CharacterController _controller;
    private BlockRig _rig;
    private float _pitch;              // current camera pitch
    private float _verticalVel;        // vertical velocity for gravity/jump
    private Vector3 _planarVelocity;   // world-space horizontal velocity
    private Vector3 _lastPosition;

    private bool _networkGrounded = true;
    private bool _jumpLatched;
    private bool _interactLatched;
    private bool _fireLatched;
    private bool _inputEnabled = true;
    private bool _phasing;

    /// <summary>Current look pitch in degrees (negative = looking up).</summary>
    public float Pitch => _pitch;

    /// <summary>Local-space move input: x = strafe, y = forward. Magnitude 0..1.</summary>
    public Vector2 MoveInput { get; private set; }

    /// <summary>Horizontal speed actually achieved last frame, in m/s.</summary>
    public float PlanarSpeed { get; private set; }

    /// <summary>Vertical velocity, for the animator's jump and landing states.</summary>
    public float VerticalVelocity => _verticalVel;

    /// <summary>
    /// Grounded state. Offline this is the CharacterController's own; on the network it is the
    /// server's, because the controller is not being moved and would report a permanent fall.
    /// </summary>
    public bool IsGrounded => controlMode == ControlMode.Local
        ? _controller != null && _controller.isGrounded
        : _networkGrounded;

    /// <summary>Body yaw in degrees, as sent to the server.</summary>
    public float Yaw => transform.eulerAngles.y;

    /// <summary>Sprint key held this frame. Sampled even when this script is not moving anything.</summary>
    public bool SprintHeld { get; private set; }

    /// <summary>
    /// Ctrl held: moving deliberately slowly. <see cref="FootstepAudio"/> reads this to go quiet,
    /// which is the whole point of the key — in a game where the Seeker hunts by ear, the only
    /// thing worth trading speed for is silence.
    /// </summary>
    public bool SneakHeld { get; private set; }

    /// <summary>
    /// True once if the weapon actually took a shot since the last call, then false.
    ///
    /// **The raw button must never go on the wire.** It used to: the server read the held state out
    /// of the last input frame it had, and that frame gets repeated for up to three ticks when a new
    /// one does not arrive — so an ordinary 100 ms click, plus the repeat, outlasted the 5-tick fire
    /// interval and the server counted *two* rounds for it. The client draws one bullet per click,
    /// so the two tallies diverged on the one number that matters: the three-round magazine. The
    /// symptom was being chained after two visible shots.
    ///
    /// So the wire carries the client's *decision* — one pulse per round that <see
    /// cref="WeaponController.Fire"/> actually fired, having already applied the cooldown, the
    /// magazine and the reload. The server re-checks all of it (`Room.FireWeapons`); this is a
    /// request, not an instruction.
    /// </summary>
    public bool ConsumeFire()
    {
        bool fired = _fireLatched;
        _fireLatched = false;
        return fired;
    }

    /// <summary>
    /// Called by <see cref="WeaponController.Fire"/> on the frame a round leaves the barrel — the
    /// only thing allowed to raise the wire's fire bit. Latched rather than read live for the same
    /// reason as the jump: the tick is slower than the render loop.
    /// </summary>
    public void LatchFire() => _fireLatched = true;

    /// <summary>
    /// True once if jump was pressed since the last call, then false. The network tick runs at
    /// 30 Hz and the render loop faster, so a jump pressed between ticks has to be latched or it
    /// is simply lost — the symptom is a jump key that works about half the time.
    /// </summary>
    public bool ConsumeJump()
    {
        bool jumped = _jumpLatched;
        _jumpLatched = false;
        return jumped;
    }

    /// <summary>
    /// True once if the interact key (E) was pressed since the last call, then false. Latched for
    /// the same reason as the jump — the network tick is slower than the render loop.
    ///
    /// This is the *networked* path. <see cref="NV.Game.PlayerInteractor"/> reads the raw key for
    /// the offline one, so both fire on the same press and neither eats the other's signal: only
    /// the wire path consumes this latch.
    /// </summary>
    public bool ConsumeInteract()
    {
        bool interacted = _interactLatched;
        _interactLatched = false;
        return interacted;
    }

    /// <summary>
    /// Places the character where the server says it is. Position is the server's *feet*; the
    /// body is lifted by the rig's ground shim so the blocks still sit on the floor rather than
    /// sinking by the CharacterController's skin width.
    /// </summary>
    public void ApplyNetworkState(Vector3 feetPosition, bool grounded, float verticalVelocity)
    {
        float lift = _rig != null ? _rig.GroundOffset : 0f;
        transform.position = new Vector3(feetPosition.x, feetPosition.y + lift, feetPosition.z);
        _networkGrounded = grounded;
        _verticalVel = verticalVelocity;
    }

    /// <summary>
    /// While false the character ignores mouse and keyboard entirely and releases the cursor.
    /// The connection UI needs the pointer, and a controller that keeps grabbing it back turns
    /// every click on a button into a click that re-locks the mouse instead.
    /// </summary>
    public bool InputEnabled
    {
        get => _inputEnabled;
        set
        {
            if (_inputEnabled == value) return;

            _inputEnabled = value;

            if (controlMode == ControlMode.Remote) return;

            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value;

            if (!value)
            {
                // 입력을 끊을 때 이동 입력을 비운다. 남겨 두면 UI 를 띄운 채로 계속 달린다.
                MoveInput = Vector2.zero;
                SprintHeld = false;
                _jumpLatched = false;
                _interactLatched = false;
                _fireLatched = false;
            }
        }
    }

    /// <summary>
    /// Blocks movement while leaving the look alone. The chain-drag strands the Seeker where the
    /// chain lands and the freeze device stops everyone; both want a player who can still look
    /// around, so this is deliberately not <see cref="InputEnabled"/> — that one releases the
    /// cursor, which mid-match reads as the game having lost focus.
    /// </summary>
    public bool MovementLocked { get; set; }

    /// <summary>
    /// Puts the body somewhere else in one frame — used by the teleport-on-hit rule, the teleport
    /// device and the chain-drag.
    ///
    /// A CharacterController writes its own position back on every Move, so assigning
    /// transform.position while it is enabled is silently undone; it has to be switched off
    /// across the assignment. The last-position sample is reset too, or the animator sees one
    /// frame of displacement the width of the map and the legs blur.
    /// </summary>
    /// <summary>
    /// Suspends collision entirely so a scripted move can pass through the building.
    ///
    /// The chain-drag needs this. Hauling the Seeker across the level by calling
    /// <see cref="Teleport"/> every frame means enabling the CharacterController inside a wall
    /// dozens of times on the way, and each of those is an invitation for Unity to depenetrate the
    /// capsule somewhere it likes better — measured as landing 3.8 m off target and half a metre in
    /// the air. Held off for the whole sweep and switched back on at the far end, the path cannot
    /// be argued with.
    /// </summary>
    public bool Phasing
    {
        get => _phasing;
        set
        {
            if (_phasing == value) return;
            _phasing = value;
            if (_controller != null) _controller.enabled = !value;
        }
    }

    /// <param name="feetPosition">Ground position. The rig's ground shim is added here.</param>
    public void Teleport(Vector3 feetPosition)
    {
        float lift = _rig != null ? _rig.GroundOffset : 0f;
        var target = new Vector3(feetPosition.x, feetPosition.y + lift, feetPosition.z);

        if (_phasing || _controller == null)
        {
            // Already switched off for the duration; just move.
            transform.position = target;
        }
        else
        {
            _controller.enabled = false;
            transform.position = target;
            _controller.enabled = true;
        }

        _planarVelocity = Vector3.zero;
        _verticalVel = 0f;
        _lastPosition = target;
    }

    /// <summary>Turns a remote puppet's body and head. Never called on the local player, whose look is its own.</summary>
    public void ApplyRemoteLook(float yawDegrees, float pitchDegrees)
    {
        transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
        SetPitch(pitchDegrees);
    }

    /// <summary>
    /// Sets the look pitch directly, e.g. to face the player somewhere on spawn.
    /// Clamped like mouse look, and the camera is updated immediately.
    /// </summary>
    public void SetPitch(float degrees)
    {
        _pitch = Mathf.Clamp(degrees, -pitchClamp, pitchClamp);
        ApplyPitch();
    }

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _rig = GetComponent<BlockRig>();

        if (cameraTransform == null && controlMode != ControlMode.Remote && Camera.main != null)
            cameraTransform = Camera.main.transform;

        _lastPosition = transform.position;
    }

    private void Start()
    {
        // A remote player's puppet must not touch the cursor — it is not the one being played.
        if (controlMode == ControlMode.Remote) return;

        Cursor.lockState = _inputEnabled ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !_inputEnabled;
    }

    private void Update()
    {
        if (controlMode != ControlMode.Remote && _inputEnabled)
        {
            HandleCursor();
            HandleLook();
            HandleMove();
        }

        MeasureSpeed();
    }

    private void HandleCursor()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame
                 && Cursor.lockState == CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void HandleLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;

        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 delta = mouse.delta.ReadValue() * mouseSensitivity;

        // Yaw: rotate the whole body left/right.
        transform.Rotate(Vector3.up, delta.x, Space.Self);

        // Pitch: rotate only the camera up/down, clamped.
        _pitch = Mathf.Clamp(_pitch - delta.y, -pitchClamp, pitchClamp);
        ApplyPitch();
    }

    // The animator owns the camera's local *position* (bob), so only rotation is set here.
    private void ApplyPitch()
    {
        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    private void HandleMove()
    {
        var keyboard = Keyboard.current;

        Vector2 input = Vector2.zero;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed) input.x -= 1f;
        }
        input = Vector2.ClampMagnitude(input, 1f);

        // Held in place by the chain or by a freeze: the keys are still read above so nothing
        // latches a stale press, then thrown away here. The look is untouched.
        if (MovementLocked) input = Vector2.zero;
        MoveInput = input;

        // Sneak beats sprint when both are held. You cannot run quietly, and a player who has
        // taken their thumb off shift is telling you which of the two they meant.
        bool sneaking = !MovementLocked && keyboard != null
                        && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
        bool sprinting = !MovementLocked && !sneaking
                         && keyboard != null && keyboard.leftShiftKey.isPressed;

        SneakHeld = sneaking;
        SprintHeld = sprinting;


        // The trigger is deliberately *not* sampled here. It is the weapon that decides whether a
        // press becomes a round, and only that decision goes on the wire — see ConsumeFire.
        if (!MovementLocked && keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            _jumpLatched = true;

        // Interact is gated by MovementLocked like the jump is. Being held by the chain or frozen
        // by a device is meant to cost the player their turn, and inserting a key from inside that
        // hold would be the one action the penalty failed to stop.
        if (!MovementLocked && keyboard != null && keyboard.eKey.wasPressedThisFrame)
            _interactLatched = true;

        // Under server authority the sampling above is the whole job: the position comes back
        // from the server. Moving the controller here as well would fight it, and the two
        // would disagree in exactly the places that matter — against a wall, off a ledge.
        if (controlMode == ControlMode.NetworkAuthority) return;

        // Something else owns the body this frame — the chain, mid-haul. Move() on a disabled
        // controller is an error log per frame, and gravity has no business running while the
        // player is being dragged through a wall.
        if (_controller == null || !_controller.enabled) return;

        float targetSpeed = sneaking ? sneakSpeed : sprinting ? sprintSpeed : walkSpeed;

        Vector3 desired = (transform.right * input.x + transform.forward * input.y) * targetSpeed;

        // Ramp toward the target so starts and stops have a little weight to them.
        float rate = input.sqrMagnitude > 1e-4f ? acceleration : deceleration;
        _planarVelocity = Vector3.MoveTowards(_planarVelocity, desired, rate * Time.deltaTime);

        if (_controller.isGrounded)
        {
            // A small downward bias keeps isGrounded stable on slopes and steps.
            if (_verticalVel < 0f) _verticalVel = -2f;
            if (!MovementLocked && keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
                _verticalVel = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        _verticalVel += gravity * Time.deltaTime;

        Vector3 motion = _planarVelocity;
        motion.y = _verticalVel;
        _controller.Move(motion * Time.deltaTime);
    }

    /// <summary>
    /// Real horizontal speed from real displacement. This is what the stride is locked to,
    /// so anything that stops the body — a wall, a slope, a ledge — stops the legs too.
    /// </summary>
    private void MeasureSpeed()
    {
        Vector3 delta = transform.position - _lastPosition;
        delta.y = 0f;
        PlanarSpeed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
        _lastPosition = transform.position;

        // A remote puppet has no input to read, but the animator needs one: the swing axis comes
        // from the move direction, which is how strafing abducts the legs and walking backwards
        // reverses the cycle. Recovering it from the displacement gets all of that for free
        // instead of putting the direction on the wire.
        if (controlMode != ControlMode.Remote) return;

        if (PlanarSpeed > 0.05f)
        {
            Vector3 local = transform.InverseTransformDirection(delta.normalized);
            float magnitude = Mathf.Clamp01(PlanarSpeed / Mathf.Max(0.1f, sprintSpeed));
            MoveInput = new Vector2(local.x, local.z) * magnitude;
        }
        else
        {
            MoveInput = Vector2.zero;
        }
    }
}
