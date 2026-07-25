using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple raycast pistol.
/// - Left mouse button: fire one round (raycast from the aim camera center).
/// - R: reload (plays the coded arms-to-hip motion, refills after reloadTime).
/// - Magazine holds 8 rounds; firing on empty forces a reload.
///
/// Uses the new Input System low-level API (Mouse.current / Keyboard.current).
/// Put this on the Player, alongside BlockRig. Assign aimCamera.
/// </summary>
public class WeaponController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Camera the shot is fired from (usually the FP camera). Defaults to Camera.main.")]
    public Camera aimCamera;
    [Tooltip("Empty transform at the tip of the barrel, used for the tracer/flash origin.")]
    public Transform muzzle;
    [Tooltip("Coded reload motion. There is no reload clip — the arms are posed in code.")]
    public ProceduralReload reloadMotion;

    [Tooltip("Block rig. Shots then leave the barrel you can actually see, rather than the " +
             "body's copy of the weapon.")]
    public BlockRig blockRig;

    [Tooltip("Character animator, told about each shot so the hands kick.")]
    public BlockCharacterAnimator characterAnimator;

    [Header("Ballistics")]
    public int magazineSize = 8;
    public float range = 100f;
    public float damage = 20f;
    public LayerMask hitMask = ~0;   // everything by default

    [Header("Timing")]
    public float fireCooldown = 0.15f;
    public float reloadTime = 1.5f;

    [Header("FX")]
    public float tracerDuration = 0.03f;

    private int _ammo;
    private float _nextFireTime;
    private bool _reloading;

    public int Ammo => _ammo;
    public bool IsReloading => _reloading;

    /// <summary>
    /// Where the crosshair is currently pointing in the world, refreshed every frame.
    /// The animator turns the pistol toward this, which is what makes the tracer leave the
    /// muzzle in a straight line instead of cutting diagonally across to the reticle.
    /// </summary>
    public Vector3 AimPoint { get; private set; }

    /// <summary>Distance from the camera to <see cref="AimPoint"/>, or <see cref="range"/> on a miss.</summary>
    public float AimDistance { get; private set; }

    /// <summary>False while the player has empty hands; set by the weapon switcher.</summary>
    public bool Armed { get; set; } = true;

    private void Awake()
    {
        if (aimCamera == null) aimCamera = Camera.main;
        if (reloadMotion == null) reloadMotion = GetComponent<ProceduralReload>();
        if (blockRig == null) blockRig = GetComponent<BlockRig>();
        if (characterAnimator == null) characterAnimator = GetComponent<BlockCharacterAnimator>();
        _ammo = magazineSize;
    }

    private void Start()
    {
        // The rig builds its blocks during Awake, so the viewmodel muzzle only exists by now.
        Transform viewmodelWeapon = blockRig != null ? blockRig.ViewWeapon : null;
        if (viewmodelWeapon != null)
        {
            Transform viewmodelMuzzle = viewmodelWeapon.Find("Muzzle");
            if (viewmodelMuzzle != null) muzzle = viewmodelMuzzle;
        }
    }

    private void Update()
    {
        // Kept current even while unarmed, so the pistol is already pointing the right way
        // on the frame it appears rather than swinging into place afterwards.
        UpdateAim();

        if (!Armed) return;

        var mouse = Mouse.current;
        var keyboard = Keyboard.current;

        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            StartReload();

        if (mouse != null && mouse.leftButton.wasPressedThisFrame
            && Time.time >= _nextFireTime && !_reloading)
        {
            if (_ammo > 0) Fire();
            else StartReload();   // click on empty -> reload
        }
    }

    /// <summary>Refreshes <see cref="AimPoint"/> from the crosshair. One ray per frame.</summary>
    private void UpdateAim()
    {
        Camera cam = aimCamera != null ? aimCamera : Camera.main;
        if (cam == null) return;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        AimDistance = Physics.Raycast(ray, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore)
            ? hit.distance
            : range;
        AimPoint = ray.origin + ray.direction * AimDistance;
    }

    private void Fire()
    {
        _ammo--;
        _nextFireTime = Time.time + fireCooldown;

        if (characterAnimator != null) characterAnimator.AddRecoil();

        Camera cam = aimCamera != null ? aimCamera : Camera.main;
        if (cam == null) return;

        // Hit detection still comes off the screen centre, so what you hit always matches
        // the crosshair rather than the barrel's own offset position.
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        float travel = range;

        if (Physics.Raycast(ray, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
        {
            travel = hit.distance;
            Debug.Log($"[Weapon] Hit '{hit.collider.name}' at {hit.point} ({hit.distance:F1}m). Ammo {_ammo}/{magazineSize}");

            // Push rigidbodies and send a damage message if the target listens for it.
            if (hit.rigidbody != null)
                hit.rigidbody.AddForceAtPosition(ray.direction * 300f, hit.point);
            hit.collider.SendMessageUpwards("OnHit", damage, SendMessageOptions.DontRequireReceiver);
        }
        else
        {
            Debug.Log($"[Weapon] Miss. Ammo {_ammo}/{magazineSize}");
        }

        // The tracer runs along the barrel, not from the muzzle across to the reticle — the
        // animator has already turned the gun onto the aim point, so the two agree, and the
        // shot reads as leaving the muzzle dead straight.
        if (muzzle != null)
            SpawnTracer(muzzle.position, muzzle.position + muzzle.forward * travel);
        else
            SpawnTracer(ray.origin, ray.origin + ray.direction * travel);

        if (_ammo <= 0) StartReload();
    }

    public void StartReload()
    {
        if (_reloading || _ammo == magazineSize) return;
        _reloading = true;

        if (reloadMotion != null)
            reloadMotion.Play(reloadTime);            // coded arms-to-hip motion

        Invoke(nameof(FinishReload), reloadTime);
    }

    private void FinishReload()
    {
        _ammo = magazineSize;
        _reloading = false;
        Debug.Log($"[Weapon] Reloaded. Ammo {_ammo}/{magazineSize}");
    }

    // Quick and cheap hit-scan tracer using a LineRenderer that self-destructs.
    private void SpawnTracer(Vector3 start, Vector3 end)
    {
        var go = new GameObject("Tracer");
        var lr = go.AddComponent<LineRenderer>();
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = new Color(1f, 0.85f, 0.4f);
        lr.startWidth = 0.02f;
        lr.endWidth = 0.005f;
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        Destroy(go, tracerDuration);
    }
}
