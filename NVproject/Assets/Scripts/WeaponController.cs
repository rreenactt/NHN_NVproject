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

    [Tooltip("The bang. Added automatically if missing, so every scene with a weapon has one.")]
    public WeaponAudio weaponAudio;

    [Tooltip("The body this weapon is held by. Networked, each shot is latched on it and that " +
             "latch — not the mouse button — is what the input frame carries.")]
    public FirstPersonController body;

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
    private FirstPersonController _playerController;

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

    /// <summary>
    /// Trigger disabled without unarming. The chain-drag holds this while the Seeker is being
    /// hauled about — the pistol is still in shot, it just cannot be fired.
    /// </summary>
    public bool FireBlocked { get; set; }

    /// <summary>
    /// Raised the instant the last round leaves the magazine, and it *replaces* the reload rather
    /// than running alongside it. The Seeker's magazine does not refill on its own: the chain-drag
    /// hooks this, drags the shooter off, and only then calls <see cref="ForceReload"/>. Left
    /// unhooked, the weapon reloads by itself as it always did.
    /// </summary>
    public System.Action onMagazineEmpty;

    private void Awake()
    {
        if (aimCamera == null) aimCamera = Camera.main;
        if (reloadMotion == null) reloadMotion = GetComponent<ProceduralReload>();
        if (blockRig == null) blockRig = GetComponent<BlockRig>();
        if (characterAnimator == null) characterAnimator = GetComponent<BlockCharacterAnimator>();
        if (crosshair == null) crosshair = GetComponent<Crosshair>();
        if (body == null) body = GetComponent<FirstPersonController>();

        // Self-supplying: a scene that has a weapon at all should make a noise when it fires,
        // without anything having to remember to wire it.
        if (weaponAudio == null) weaponAudio = GetComponent<WeaponAudio>();
        if (weaponAudio == null) weaponAudio = gameObject.AddComponent<WeaponAudio>();

        _playerController = GetComponent<FirstPersonController>();

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

        // The ESC menu (and anything else that frees the cursor) turns input off at the
        // controller, and the weapon must honour the same flag: the click that presses a menu
        // button must not also leave the barrel. Deliberately not FireBlocked — that flag is
        // the chain's, and a menu that borrowed it would hand a chained Seeker a loaded gun
        // on close.
        if (_playerController != null && !_playerController.InputEnabled) return;

        var mouse = Mouse.current;
        var keyboard = Keyboard.current;

        if (FireBlocked) return;

        // A manual reload is only offered when nothing else owns the empty magazine. Under the
        // chain rule the Seeker does not get to top up mid-fight — that is the whole cost.
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame && onMagazineEmpty == null)
            StartReload();

        if (mouse != null && mouse.leftButton.wasPressedThisFrame
            && Time.time >= _nextFireTime && !_reloading)
        {
            if (_ammo > 0) Fire();
            else if (onMagazineEmpty == null) StartReload();   // click on empty -> reload
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

        // Tell the wire that a round was taken. This is the *only* thing that raises the fire bit:
        // the server used to read the raw button instead and counted a long click as two rounds, so
        // the magazine the server kept and the bullets this client drew were different numbers.
        if (body != null) body.LatchFire();

        // Before the round is even resolved. The bang is not feedback about a hit — it is the
        // event itself, and everyone within sixty metres is entitled to hear it immediately.
        if (weaponAudio != null) weaponAudio.PlayShot();

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

        if (_ammo > 0) return;

        if (onMagazineEmpty != null) onMagazineEmpty();
        else StartReload();
    }

    /// <summary>
    /// Reloads on someone else's schedule — the chain-drag calls this once it has finished with
    /// the Seeker. It bypasses the "already reloading" guard because the magazine has been empty
    /// for several seconds by then and nothing else was ever going to refill it.
    /// </summary>
    public void ForceReload(float duration)
    {
        CancelInvoke(nameof(FinishReload));
        _reloading = true;

        if (reloadMotion != null) reloadMotion.Play(duration);
        Invoke(nameof(FinishReload), duration);
    }

    /// <summary>Magazine capacity, so the HUD does not have to guess. Resizes the current magazine.</summary>
    public void SetMagazineSize(int size)
    {
        magazineSize = Mathf.Max(1, size);
        _ammo = Mathf.Min(_ammo, magazineSize);
    }

    /// <summary>
    /// The server's round count. Networked, the magazine is not something this client keeps track of
    /// — the server decides whether a trigger pull becomes a round (`Room.FireWeapons`) and sends
    /// what is left, so the HUD has to draw that rather than a local tally.
    ///
    /// It only ever *overwrites*. The local `Fire` still decrements as a prediction so the shell
    /// icons react on the frame the trigger goes down, and the next bulletin corrects it 0.5 s later
    /// — a shot the server refused shows up as a shell flickering back on.
    /// </summary>
    public void AcceptAmmo(int rounds)
    {
        int had = _ammo;
        _ammo = Mathf.Clamp(rounds, 0, magazineSize);

        // The server can empty the magazine while the local counter still shows rounds: it fires
        // on *held* at its own interval (`Room.FireWeapons`), the local `Fire` only on the click
        // edge, so one held click can spend three server rounds against one local one. The chain
        // then starts server-side (the body is dragged by snapshots) with `ChainDrag.Trigger`
        // never called — no chain drawn, no HELD banner, and the drag interpolates through walls.
        // The falling edge here is the missed signal; `Trigger` guards itself against repeats.
        if (had > 0 && _ammo == 0 && onMagazineEmpty != null) onMagazineEmpty();
    }

    /// <summary>
    /// Full magazine, now, cancelling any reload in progress. A match starts with a full weapon —
    /// without this, whatever was left in the magazine when the last match ended carries over, and
    /// a Seeker can begin a round one round from being chained.
    /// </summary>
    public void Refill()
    {
        CancelInvoke(nameof(FinishReload));
        _ammo = magazineSize;
        _reloading = false;
        _nextFireTime = 0f;
    }

    /// <summary>
    /// Called by a round when it lands, however long after the trigger pull that is. Marking the
    /// hit here rather than at fire time is the whole point — with projectiles you genuinely do
    /// not know yet whether the shot connected.
    /// </summary>
    private void OnBulletImpact(RaycastHit hit)
    {
        // **서버가 판정하는 매치에서는 여기서 마커를 띄우지 않는다.**
        //
        // 이 탄은 로컬 예측이고, 원격 몸에는 총알을 멈출 콜라이더가 없다 — `CharacterController`
        // 는 꺼져 있고 블록에는 콜라이더가 없다. 그래서 사람을 맞혀도 실제로 맞는 것은 **그 뒤의
        // 벽**이고, 마커는 벽이든 사람이든 항상 떴다. 항상 뜨는 신호는 아무 말도 하지 않는다.
        //
        // 서버는 누가 맞았는지 알고 있으므로 그쪽에서 띄운다(`MatchManager.AcceptCombatState`).
        // 전문이 2Hz 라 최대 0.5초 늦지만, **늦고 참인 편이 즉시이고 무의미한 것보다 낫다** —
        // 세 발뿐인 탄창에서 맞았는지 여부는 조준 타이밍보다 훨씬 큰 정보다.
        var match = NV.Game.MatchManager.Instance;
        if (match != null && match.ServerOwnsCombat) return;

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
