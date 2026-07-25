using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Switches between empty hands (key 1) and the pistol (key 2).
///
/// The swap reuses the reload motion: the arms lower, the weapon appears or disappears
/// while it is out of sight at the bottom of that motion, and the arms come back up.
/// Hiding the pistol mid-screen would read as it vanishing, so the timing matters more
/// than the animation does.
///
/// Armed state drives three things together: both pistol copies' active state (the body's,
/// which the mirror sees, and the viewmodel's, which you see), the animator's held-weapon
/// arm pose, and the weapon's own ability to fire.
///
/// Put on the Player.
/// </summary>
public class WeaponSwitcher : MonoBehaviour
{
    [Header("References")]
    public WeaponController weapon;
    public ProceduralReload switchMotion;
    public BlockRig blockRig;
    public BlockCharacterAnimator characterAnimator;

    [Header("Settings")]
    [Tooltip("Seconds for the whole lower-swap-raise motion.")]
    public float switchDuration = 0.7f;
    public bool startArmed = true;

    private bool _armed;
    private bool _switching;

    public bool IsArmed => _armed;

    private void Awake()
    {
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (switchMotion == null) switchMotion = GetComponent<ProceduralReload>();
        if (blockRig == null) blockRig = GetComponent<BlockRig>();
        if (characterAnimator == null) characterAnimator = GetComponent<BlockCharacterAnimator>();
    }

    private void Start()
    {
        _armed = startArmed;
        ApplyArmedState();
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.digit1Key.wasPressedThisFrame) SetArmed(false);
        else if (keyboard.digit2Key.wasPressedThisFrame) SetArmed(true);
    }

    public void SetArmed(bool armed)
    {
        if (_switching || armed == _armed) return;
        if (weapon != null && weapon.IsReloading) return;

        _switching = true;

        // Swap at the bottom of the motion, where the hands are out of frame.
        if (switchMotion != null)
        {
            switchMotion.Play(switchDuration, () =>
            {
                _armed = armed;
                ApplyArmedState();
            });
            Invoke(nameof(FinishSwitch), switchDuration);
        }
        else
        {
            _armed = armed;
            ApplyArmedState();
            FinishSwitch();
        }
    }

    private void FinishSwitch() => _switching = false;

    private void ApplyArmedState()
    {
        if (blockRig != null)
        {
            if (blockRig.BodyWeapon != null) blockRig.BodyWeapon.gameObject.SetActive(_armed);
            if (blockRig.ViewWeapon != null) blockRig.ViewWeapon.gameObject.SetActive(_armed);
        }

        // The held-weapon arm pose; empty hands just swing with the walk.
        if (characterAnimator != null) characterAnimator.Armed = _armed;

        if (weapon != null) weapon.Armed = _armed;
    }
}
