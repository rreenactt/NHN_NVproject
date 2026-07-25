using UnityEngine;

/// <summary>
/// The "lower the weapon and bring it back up" envelope, used both for reloading and for
/// swapping between empty hands and the pistol. There is no clip for either.
///
/// This used to pose the humanoid arm bones itself. It no longer touches any transform:
/// it only produces a 0..1 <see cref="Weight"/>, and <see cref="BlockCharacterAnimator"/>
/// blends the arms toward its lowered pose by that amount. One place composes the pose, so
/// the reload cannot fight the walk cycle or the aim.
///
/// The motion runs in three phases — lower, hold, raise — each eased with SmoothStep so the
/// arms accelerate and settle instead of snapping at the extremes. A plain sine crawls
/// through the extremes and rushes the middle, which is what made the old motion look
/// mechanical. Whoever started it can hook the moment the arms reach the bottom, which is
/// where a weapon swap belongs: out of sight.
///
/// Put on the Player.
/// </summary>
public class ProceduralReload : MonoBehaviour
{
    [Tooltip("How far to blend into the lowered pose (0..1).")]
    [Range(0f, 1f)] public float poseWeight = 1f;

    [Tooltip("Fraction of the motion spent bringing the arms down.")]
    [Range(0.05f, 0.8f)] public float lowerShare = 0.3f;

    [Tooltip("Fraction spent held at the bottom — the reload itself, or the weapon swap.")]
    [Range(0f, 0.8f)] public float holdShare = 0.2f;

    private float _time;
    private float _duration;
    private bool _active;
    private bool _reachedBottom;
    private System.Action _onBottom;

    public bool IsReloading => _active;

    /// <summary>How far into the lowered pose the arms currently are (0..1).</summary>
    public float Weight { get; private set; }

    /// <param name="onBottom">Called once, when the arms are fully lowered.</param>
    public void Play(float duration, System.Action onBottom = null)
    {
        _duration = Mathf.Max(0.05f, duration);
        _time = 0f;
        _active = true;
        _reachedBottom = false;
        _onBottom = onBottom;
    }

    // Runs before the animator (execution order 100), so Weight is current when it reads it.
    private void Update()
    {
        if (!_active) return;

        _time += Time.deltaTime;
        Weight = EnvelopeAt(Mathf.Clamp01(_time / _duration)) * poseWeight;

        if (_time >= _duration)
        {
            _active = false;
            Weight = 0f;
        }
    }

    /// <summary>Eased down-hold-up curve, firing <see cref="_onBottom"/> at the bottom.</summary>
    private float EnvelopeAt(float progress)
    {
        float lower = Mathf.Clamp(lowerShare, 0.05f, 0.8f);
        float hold = Mathf.Clamp(holdShare, 0f, 1f - lower - 0.05f);
        float raiseStart = lower + hold;

        if (progress < lower)
            return Mathf.SmoothStep(0f, 1f, progress / lower);

        if (progress < raiseStart)
        {
            ReachBottom();
            return 1f;
        }

        ReachBottom();
        return Mathf.SmoothStep(1f, 0f, (progress - raiseStart) / Mathf.Max(1e-3f, 1f - raiseStart));
    }

    private void ReachBottom()
    {
        if (_reachedBottom) return;
        _reachedBottom = true;
        _onBottom?.Invoke();
    }
}
