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
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
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
    public float jumpHeight = 1.2f;
    public float gravity = -19.62f;

    [Tooltip("How briskly the character reaches target speed, in m/s². Instant response " +
             "reads as weightless; this gives the walk a little mass.")]
    public float acceleration = 45f;
    [Tooltip("Deceleration when the stick is released, in m/s².")]
    public float deceleration = 55f;

    private CharacterController _controller;
    private float _pitch;              // current camera pitch
    private float _verticalVel;        // vertical velocity for gravity/jump
    private Vector3 _planarVelocity;   // world-space horizontal velocity
    private Vector3 _lastPosition;

    /// <summary>Current look pitch in degrees (negative = looking up).</summary>
    public float Pitch => _pitch;

    /// <summary>Local-space move input: x = strafe, y = forward. Magnitude 0..1.</summary>
    public Vector2 MoveInput { get; private set; }

    /// <summary>Horizontal speed actually achieved last frame, in m/s.</summary>
    public float PlanarSpeed { get; private set; }

    /// <summary>Vertical velocity, for the animator's jump and landing states.</summary>
    public float VerticalVelocity => _verticalVel;

    public bool IsGrounded => _controller != null && _controller.isGrounded;

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

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        _lastPosition = transform.position;
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        HandleCursor();
        HandleLook();
        HandleMove();
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
        MoveInput = input;

        bool sprinting = keyboard != null && keyboard.leftShiftKey.isPressed;
        float targetSpeed = sprinting ? sprintSpeed : walkSpeed;

        Vector3 desired = (transform.right * input.x + transform.forward * input.y) * targetSpeed;

        // Ramp toward the target so starts and stops have a little weight to them.
        float rate = input.sqrMagnitude > 1e-4f ? acceleration : deceleration;
        _planarVelocity = Vector3.MoveTowards(_planarVelocity, desired, rate * Time.deltaTime);

        if (_controller.isGrounded)
        {
            // A small downward bias keeps isGrounded stable on slopes and steps.
            if (_verticalVel < 0f) _verticalVel = -2f;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
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
    }
}
