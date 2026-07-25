---
name: fps-character-controller
description: >
  Build and tune a first-person (1인칭) character controller in Unity via MCP — WASD movement,
  mouse look, jump, crouch, sprint, ground detection, and camera rig. Use whenever the user
  wants to create or adjust the player/character in a 3D FPS: "캐릭터 만들어", "1인칭 이동",
  "마우스 시점", "점프/앉기/달리기", player movement, camera look, "캐릭터 움직임 이상해".
  Assumes the unity-mcp-ops skill for the actual tool calls. Prefer CharacterController over
  Rigidbody for FPS locomotion unless physics interactions are required.
---

# FPS Character Controller

Read **unity-mcp-ops** first for the script→compile→attach loop and tool names.

## Architecture decision: CharacterController, not Rigidbody

For a standard shooter, use Unity's built-in **`CharacterController`** component. It gives you
`Move()` with built-in collision + slope handling and predictable, snappy feel — no fighting the
physics solver for something that should feel deterministic. Reserve a Rigidbody-based body only
if you need ragdoll, physical push, or vehicles.

The player hierarchy you build:

```
Player                (CharacterController + PlayerController + PlayerLook)
└── CameraRoot        (empty, positioned at eye height ~1.6m)  ← camera yaw/pitch pivot
    └── Main Camera    (Camera; optional weapon holder parented here)
```

## Build order (via MCP)

1. `update_gameobject` → create `Player`, tag `Player`, set transform.
2. `update_component` → add `CharacterController`; set `height`, `radius`, `center` so the
   capsule matches eye height.
3. `update_gameobject` → create `CameraRoot` as child at local `(0, 1.6, 0)`; parent the scene's
   Main Camera under it (or create one).
4. Write `PlayerController.cs` and `PlayerLook.cs` into `Assets/Scripts/FPS/`, compile, verify
   console, then `update_component` to attach and wire the `cameraRoot` reference.

## Movement pattern (honor the conventions)

Input is read in `Update`; motion is applied in `FixedUpdate` isn't used with CharacterController
(its `Move` is frame-based) — instead keep per-frame math minimal and cache everything in `Awake`.
Gravity is integrated manually; jump sets vertical velocity directly.

```csharp
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;
    [SerializeField] private float crouchSpeed = 2.5f;
    [SerializeField] private float jumpHeight = 1.1f;
    [SerializeField] private float gravity = -19.62f;      // ~2x Physics.gravity for a snappy fall

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;        // small empty at the capsule's feet
    [SerializeField] private float groundRadius = 0.25f;
    [SerializeField] private LayerMask groundMask;

    private CharacterController controller;
    private Vector3 verticalVelocity;
    private bool isGrounded;
    private float currentSpeed;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();   // cached once, never in Update
        currentSpeed = walkSpeed;
    }

    private void Update()
    {
        isGrounded = Physics.CheckSphere(groundCheck.position, groundRadius, groundMask);
        if (isGrounded && verticalVelocity.y < 0f)
            verticalVelocity.y = -2f;                       // keep grounded, avoid drift

        // Speed selection is cheap branching, not a per-frame allocation.
        if (Input.GetKey(KeyCode.LeftShift)) currentSpeed = sprintSpeed;
        else if (Input.GetKey(KeyCode.LeftControl)) currentSpeed = crouchSpeed;
        else currentSpeed = walkSpeed;

        float inputX = Input.GetAxisRaw("Horizontal");
        float inputZ = Input.GetAxisRaw("Vertical");
        Vector3 move = (transform.right * inputX + transform.forward * inputZ).normalized;
        controller.Move(move * currentSpeed * Time.deltaTime);

        if (Input.GetButtonDown("Jump") && isGrounded)
            verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

        verticalVelocity.y += gravity * Time.deltaTime;
        controller.Move(verticalVelocity * Time.deltaTime);
    }
}
```

## Mouse look (separate script, separate concern)

Split look from movement so each stays simple. Yaw rotates the Player body; pitch rotates only
the CameraRoot, and pitch is clamped so you can't flip over.

```csharp
using UnityEngine;

public class PlayerLook : MonoBehaviour
{
    [SerializeField] private Transform cameraRoot;         // wire this via update_component
    [SerializeField] private float sensitivity = 2f;
    [SerializeField] private float pitchClamp = 85f;

    private float pitch;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;          // FPS mouse capture
        Cursor.visible = false;
    }

    private void Update()
    {
        float mouseX = Input.GetAxisRaw("Mouse X") * sensitivity;
        float mouseY = Input.GetAxisRaw("Mouse Y") * sensitivity;

        transform.Rotate(Vector3.up * mouseX);             // yaw on the body
        pitch = Mathf.Clamp(pitch - mouseY, -pitchClamp, pitchClamp);
        cameraRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }
}
```

## Crouch that actually changes the capsule

A real crouch shrinks the `CharacterController` height and lowers `CameraRoot`. Interpolate both
over a few frames (coroutine, not an `Update` loop that runs forever) so it feels smooth, and
raycast upward before standing so you don't clip into ceilings.

## Tuning checklist (when it "feels off")

- **Floaty jump** → increase `-gravity` magnitude; keep `jumpHeight` modest (1–1.3m).
- **Slidey stops** → you're likely lerping input; use `GetAxisRaw` (done above) for instant response.
- **Camera jitter** → make sure look runs in `Update` with raw mouse deltas, never `FixedUpdate`.
- **Can't climb small steps** → raise `CharacterController.stepOffset` (~0.3).
- **Wall sticking** → raise `slopeLimit` only if intended; check the capsule `radius` isn't tiny.

## New Input System

If the project uses the Input System package (`add_package` → `com.unity.inputsystem`), swap the
`Input.Get*` calls for an `InputAction`-based reader but keep the exact same Update/FixedUpdate
split and clamp logic. Ask before migrating an existing project — it changes every input call site.
