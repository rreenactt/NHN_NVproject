using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Projectile pistol.
/// - Left mouse button: launches an actual <see cref="Bullet"/> out of the muzzle.
/// - R: reload (plays the coded arms-to-hip motion, refills after reloadTime).
/// - Magazine holds 8 rounds; firing on empty forces a reload.
///
/// The shot is **not** hitscan. Nothing here resolves a hit: the round travels and works out
/// its own impact, so distant shots take visible time to land and a target that moves after the
/// trigger pull can be missed.
///
/// One raycast remains, in <see cref="UpdateAim"/>, and it is not hit detection — it is what the
/// animator uses to converge the barrel onto whatever the crosshair is over. Without it the gun
/// would fire parallel to the camera and every close shot would land beside the reticle by the
/// width of the muzzle offset.
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

    [Tooltip("Crosshair, flashed as a hit marker when a round connects.")]
    public Crosshair crosshair;

    [Header("Ballistics")]
    public int magazineSize = 8;
    [Tooltip("How far ahead the crosshair looks when converging the barrel. Not a bullet range — " +
             "the round itself is limited by its own lifetime.")]
    public float range = 100f;
    public float damage = 20f;
    public LayerMask hitMask = ~0;   // everything by default

    [Tooltip("Muzzle velocity in m/s. Fast enough to read as a bullet, slow enough to watch.")]
    public float bulletSpeed = 120f;
    [Tooltip("Bullet drop in m/s². 0 keeps the round on the crosshair at every range.")]
    public float bulletGravity = 0f;
    [Tooltip("Seconds a round survives before expiring.")]
    public float bulletLifetime = 3f;

    [Header("Timing")]
    public float fireCooldown = 0.15f;
    public float reloadTime = 1.5f;

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
        if (crosshair == null) crosshair = GetComponent<Crosshair>();
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

    /// <summary>
    /// Launches an actual round. There is no hit detection here at all any more — the bullet
    /// resolves its own impact as it travels, so a shot can miss a target that walks out of the
    /// way after you pulled the trigger, which hitscan could never do.
    /// </summary>
    private void Fire()
    {
        _ammo--;
        _nextFireTime = Time.time + fireCooldown;

        if (characterAnimator != null) characterAnimator.AddRecoil();

        Camera cam = aimCamera != null ? aimCamera : Camera.main;
        if (cam == null) return;

        Vector3 origin = muzzle != null ? muzzle.position : cam.transform.position;

        // Standing flush against a wall can push the muzzle through it. Starting the round there
        // would let you shoot through walls by hugging them, so fall back to the eye position.
        if (muzzle != null
            && Physics.Linecast(cam.transform.position, origin, hitMask, QueryTriggerInteraction.Ignore))
        {
            origin = cam.transform.position;
        }

        // Aim at the crosshair, NOT along muzzle.forward. The muzzle is a viewmodel bone: it bobs
        // with the walk, sways, and is re-aimed in LateUpdate — so reading its rotation here, in
        // Update, uses last frame's orientation and adds the bob on top. Rounds fired while moving
        // or turning then leave at visibly wrong angles. AimPoint was refreshed at the top of this
        // same Update, so it is the one direction that is both current and on the reticle.
        Vector3 direction = AimPoint - origin;
        if (direction.sqrMagnitude < 1e-6f) direction = cam.transform.forward;

        Bullet.Spawn(origin, direction.normalized, bulletSpeed, damage, bulletGravity, hitMask,
            bulletLifetime, OnBulletImpact);
        Debug.Log($"[Weapon] Fired. Ammo {_ammo}/{magazineSize}");

        if (_ammo <= 0) StartReload();
    }

    /// <summary>
    /// Called by a round when it lands, however long after the trigger pull that is. Marking the
    /// hit here rather than at fire time is the whole point — with projectiles you genuinely do
    /// not know yet whether the shot connected.
    /// </summary>
    private void OnBulletImpact(RaycastHit hit)
    {
        if (crosshair != null) crosshair.ShowHitMarker();
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

}
