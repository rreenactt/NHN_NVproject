using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The crosshair: four bars around a gap, built in code like everything else here — no prefab,
/// no sprite asset, no scene-authored Canvas.
///
/// It is deliberately **dynamic** rather than a fixed cross. The gap widens as you move and
/// snaps wider on each shot, then settles. That is not decoration: now that rounds are real
/// projectiles with travel time, the crosshair is the only thing telling you where the barrel
/// is converged, and a reticle that visibly reacts to firing reads as feedback for a shot that
/// may not land for another 40 ms.
///
/// Every bar is drawn twice — a black bar one pixel larger behind a white one. A plain white
/// crosshair disappears against the Backrooms' pale yellow walls, which is most of this level.
///
/// Put on the Player. It finds the controller, the weapon switcher and the animator itself.
/// </summary>
public class Crosshair : MonoBehaviour
{
    [Header("References")]
    public FirstPersonController controller;
    public WeaponSwitcher weaponSwitcher;
    public BlockCharacterAnimator characterAnimator;

    [Header("Shape (pixels at 1920x1080)")]
    [Tooltip("Length of each bar.")]
    public float barLength = 9f;
    [Tooltip("Thickness of each bar.")]
    public float barThickness = 2f;
    [Tooltip("Gap between the centre and the inner end of each bar, at rest.")]
    public float restGap = 5f;
    [Tooltip("Draw a dot in the middle of the gap.")]
    public bool centreDot = true;

    [Header("Spread")]
    [Tooltip("Extra gap at full sprint. The reticle opening up as you run is the standard " +
             "shorthand for 'you are less accurate right now'.")]
    public float moveSpread = 12f;
    [Tooltip("Extra gap at the peak of the recoil kick.")]
    public float shotSpread = 10f;
    [Tooltip("Seconds for the gap to snap open. Must be well under the recoil envelope (~0.2 s) " +
             "or the crosshair is still opening while the kick is already decaying, and the shot " +
             "never visibly registers.")]
    public float openSmoothing = 0.02f;
    [Tooltip("Seconds for the gap to settle back. Slower than opening — that asymmetry is what " +
             "reads as a kick rather than a wobble.")]
    public float closeSmoothing = 0.13f;

    [Header("Hit marker")]
    [Tooltip("Four diagonal ticks that flash when a round lands. With projectile bullets the " +
             "hit happens well after the trigger pull, so this is the only confirmation you get.")]
    public bool showHitMarker = true;
    public float hitMarkerLength = 7f;
    public float hitMarkerThickness = 2f;
    [Tooltip("Distance of each tick from the centre when it appears.")]
    public float hitMarkerGap = 7f;
    [Tooltip("How far the ticks drift outward as they fade.")]
    public float hitMarkerExpand = 4f;
    public float hitMarkerDuration = 0.22f;
    public Color hitMarkerColor = new Color(1f, 0.95f, 0.85f, 1f);

    [Header("Look")]
    public Color color = new Color(1f, 1f, 1f, 0.9f);
    public Color outlineColor = new Color(0f, 0f, 0f, 0.55f);
    [Tooltip("Hide the crosshair with empty hands — there is nothing to aim.")]
    public bool hideWhenUnarmed = true;

    private Canvas _canvas;
    private float _gap;
    private float _gapVelocity;

    // Bars and the black rect behind each, held as plain fields rather than a lookup.
    // A Dictionary here is a trap: it does not survive the domain reload that a script edit
    // triggers mid-play, so every frame afterwards threw and the reticle froze half-updated,
    // while these UnityEngine.Object references came back fine.
    private RectTransform _up, _upOutline;
    private RectTransform _down, _downOutline;
    private RectTransform _left, _leftOutline;
    private RectTransform _right, _rightOutline;
    private RectTransform _dot, _dotOutline;

    // Hit marker: four diagonal ticks, plus their outlines and images for fading.
    private readonly RectTransform[] _tick = new RectTransform[4];
    private readonly RectTransform[] _tickOutline = new RectTransform[4];
    private readonly Image[] _tickImage = new Image[4];
    private readonly Image[] _tickOutlineImage = new Image[4];
    private float _hitTimer;

    private void Awake()
    {
        if (controller == null) controller = GetComponent<FirstPersonController>();
        if (weaponSwitcher == null) weaponSwitcher = GetComponent<WeaponSwitcher>();
        if (characterAnimator == null) characterAnimator = GetComponent<BlockCharacterAnimator>();

        _gap = restGap;
        Build();
    }

    private void Build()
    {
        // Clear any half-built canvas from a previous attempt, or a rebuild would stack a
        // second crosshair on top of the first.
        Transform stale = transform.Find("Crosshair Canvas");
        if (stale != null) Destroy(stale.gameObject);

        var canvasGo = new GameObject("Crosshair Canvas");
        canvasGo.transform.SetParent(transform, false);

        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;

        // Scale with the screen so the crosshair is the same apparent size at any resolution.
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _up = MakeBar(canvasGo.transform, "Up", out _upOutline);
        _down = MakeBar(canvasGo.transform, "Down", out _downOutline);
        _left = MakeBar(canvasGo.transform, "Left", out _leftOutline);
        _right = MakeBar(canvasGo.transform, "Right", out _rightOutline);

        if (centreDot)
        {
            _dot = MakeBar(canvasGo.transform, "Dot", out _dotOutline);
            Place(_dot, _dotOutline, Vector2.zero, new Vector2(barThickness, barThickness));
        }

        // Ticks sit at the four diagonals, each rotated to lie along its own diagonal so the
        // set reads as an X rather than four loose dashes.
        for (int i = 0; i < 4; i++)
        {
            _tick[i] = MakeBar(canvasGo.transform, "Hit " + i, out _tickOutline[i]);
            _tickImage[i] = _tick[i].GetComponent<Image>();
            _tickOutlineImage[i] = _tickOutline[i].GetComponent<Image>();

            float angle = 45f + i * 90f;
            _tick[i].localRotation = Quaternion.Euler(0f, 0f, angle);
            _tickOutline[i].localRotation = Quaternion.Euler(0f, 0f, angle);
        }
        LayoutHitMarker(0f);

        Layout();
    }

    /// <summary>Flash the hit marker. Called by the weapon when one of its rounds connects.</summary>
    public void ShowHitMarker()
    {
        if (!showHitMarker) return;
        _hitTimer = hitMarkerDuration;
    }

    /// <param name="strength">1 at the moment of impact, fading to 0.</param>
    private void LayoutHitMarker(float strength)
    {
        float radius = hitMarkerGap + hitMarkerExpand * (1f - strength);
        var size = new Vector2(hitMarkerLength, hitMarkerThickness);

        for (int i = 0; i < 4; i++)
        {
            if (_tick[i] == null) continue;

            float angle = (45f + i * 90f) * Mathf.Deg2Rad;
            var position = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

            _tick[i].anchoredPosition = position;
            _tick[i].sizeDelta = size;
            _tickOutline[i].anchoredPosition = position;
            _tickOutline[i].sizeDelta = size + Vector2.one * 2f;

            Color fill = hitMarkerColor;
            fill.a = hitMarkerColor.a * strength;
            _tickImage[i].color = fill;

            Color edge = outlineColor;
            edge.a = outlineColor.a * strength;
            _tickOutlineImage[i].color = edge;
        }
    }

    /// <summary>One bar: a black rect one pixel proud on every side, with a white rect on top.</summary>
    private RectTransform MakeBar(Transform parent, string name, out RectTransform outline)
    {
        var outlineGo = new GameObject(name + " Outline", typeof(RectTransform));
        outlineGo.transform.SetParent(parent, false);
        var outlineRect = (RectTransform)outlineGo.transform;
        CentreAnchors(outlineRect);
        var outlineImage = outlineGo.AddComponent<Image>();
        outlineImage.color = outlineColor;
        outlineImage.raycastTarget = false;

        var barGo = new GameObject(name, typeof(RectTransform));
        barGo.transform.SetParent(parent, false);
        var barRect = (RectTransform)barGo.transform;
        CentreAnchors(barRect);
        var barImage = barGo.AddComponent<Image>();
        barImage.color = color;
        barImage.raycastTarget = false;

        outline = outlineRect;
        return barRect;
    }

    private static void CentreAnchors(RectTransform rect)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private void Update()
    {
        // A mid-play script edit can leave the component alive with its build gone. Rebuild
        // rather than throwing every frame for the rest of the session.
        if (_canvas == null || _up == null) { Build(); return; }

        bool armed = weaponSwitcher == null || weaponSwitcher.IsArmed;
        bool visible = armed || !hideWhenUnarmed;
        if (_canvas.enabled != visible) _canvas.enabled = visible;
        if (!visible) return;

        float speedFraction = 0f;
        if (controller != null && controller.sprintSpeed > 0.01f)
            speedFraction = Mathf.Clamp01(controller.PlanarSpeed / controller.sprintSpeed);

        float recoil = characterAnimator != null ? characterAnimator.RecoilWeight : 0f;

        float target = restGap + moveSpread * speedFraction + shotSpread * recoil;
        float smoothing = target > _gap ? openSmoothing : closeSmoothing;
        _gap = Mathf.SmoothDamp(_gap, target, ref _gapVelocity, smoothing);

        Layout();

        if (_hitTimer > 0f)
        {
            _hitTimer = Mathf.Max(0f, _hitTimer - Time.deltaTime);
            // Squared falloff: bright and sharp on impact, gone quickly, rather than a slow smear.
            float strength = hitMarkerDuration > 0f ? _hitTimer / hitMarkerDuration : 0f;
            LayoutHitMarker(strength * strength);
        }
    }

    private void Layout()
    {
        float offset = _gap + barLength * 0.5f;
        var vertical = new Vector2(barThickness, barLength);
        var horizontal = new Vector2(barLength, barThickness);

        Place(_up, _upOutline, new Vector2(0f, offset), vertical);
        Place(_down, _downOutline, new Vector2(0f, -offset), vertical);
        Place(_left, _leftOutline, new Vector2(-offset, 0f), horizontal);
        Place(_right, _rightOutline, new Vector2(offset, 0f), horizontal);
    }

    private static void Place(RectTransform bar, RectTransform outline, Vector2 position, Vector2 size)
    {
        if (bar == null) return;
        bar.anchoredPosition = position;
        bar.sizeDelta = size;

        if (outline == null) return;
        outline.anchoredPosition = position;
        outline.sizeDelta = size + Vector2.one * 2f;
    }
}
