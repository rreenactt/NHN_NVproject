using UnityEngine;

/// <summary>
/// Footsteps, synthesised in code like the level's hum — there is no audio asset in this project
/// and this does not add one.
///
/// **The falloff is the feature.** In a hide-and-seek game a footstep is not decoration, it is the
/// Seeker's main sensor and the Runner's main leak, so the interesting part is not the sound but
/// where it stops being audible. The rolloff is therefore a hand-built curve rather than Unity's
/// logarithmic default: the default never actually reaches zero, so a Runner three rooms away
/// stays faintly present, and "faintly present everywhere" is the same as no information at all.
/// This curve is flat and loud inside <see cref="fullVolumeRange"/>, drops steeply through the
/// middle, and is *exactly* silent at <see cref="hearingRange"/>.
///
/// Steps are driven by the animator's stride phase where there is one, so the sound lands on the
/// frame the foot does. Anything without an animator — a practice Runner on a NavMeshAgent — falls
/// back to one step per <see cref="strideLength"/> metres travelled, which is the same thing
/// measured a cruder way.
///
/// Put it on any agent that should be audible. It builds and configures its own AudioSource.
/// </summary>
[DisallowMultipleComponent]
public class FootstepAudio : MonoBehaviour
{
    [Header("Hearing")]
    [Tooltip("Metres. Past this the step is silent — not quiet, silent. This is the balance knob: " +
             "raise it and the Seeker hears the whole floor, lower it and Runners can walk past a " +
             "wall unnoticed.")]
    public float hearingRange = 18f;

    [Tooltip("Metres. Inside this the step plays at full volume; the falloff starts here.")]
    public float fullVolumeRange = 1.6f;

    [Tooltip("Volume of somebody else's footstep at full volume.")]
    [Range(0f, 1f)] public float volume = 0.85f;

    [Tooltip("Volume of your own footsteps, which are heard flat rather than positioned — the " +
             "listener is inside your own head, where 3D panning is meaningless.")]
    [Range(0f, 1f)] public float selfVolume = 0.3f;

    [Tooltip("On for the agent this client is playing. Own steps go 2D and quiet.")]
    public bool isLocalListener;

    [Header("Gait")]
    [Tooltip("Metres per step for anything with no animator to read a stride phase from.")]
    public float strideLength = 1.05f;

    [Tooltip("Below this speed nothing is walking, so nothing is heard.")]
    public float minSpeed = 0.4f;

    [Tooltip("Volume multiplier at a full sprint. Running is supposed to be a decision you can " +
             "be punished for.")]
    public float sprintLoudness = 1.4f;

    [Tooltip("Volume multiplier while Ctrl is held. 0 is genuinely silent — the price is moving " +
             "at under half walking pace. Raise it to leave the Seeker a faint tell instead.")]
    [Range(0f, 1f)] public float sneakLoudness = 0f;

    [Tooltip("Seconds. Floor on the gap between two steps, so a stutter in the gait cannot " +
             "machine-gun the sound.")]
    public float minStepInterval = 0.14f;

    [Header("Landing")]
    [Tooltip("Volume of the thud on landing, relative to a step.")]
    public float landLoudness = 1.5f;

    [Tooltip("Downward speed in m/s that counts as a full-strength landing.")]
    public float landFullSpeed = 8f;

    [Header("References")]
    public FirstPersonController controller;
    public BlockCharacterAnimator characterAnimator;
    public UnityEngine.AI.NavMeshAgent navAgent;

    /// <summary>Steps played since the component woke. Exists so the gait can be checked numerically.</summary>
    public int StepCount { get; private set; }

    private static AudioClip[] _stepClips;
    private static AudioClip _landClip;

    private AudioSource _source;
    private float _lastSin;
    private bool _hasLastSin;
    private float _travelled;
    private float _nextStepTime;
    private Vector3 _lastPosition;
    private bool _wasGrounded = true;
    private int _lastClip = -1;

    private void Awake()
    {
        if (controller == null) controller = GetComponent<FirstPersonController>();
        if (characterAnimator == null) characterAnimator = GetComponent<BlockCharacterAnimator>();
        if (navAgent == null) navAgent = GetComponent<UnityEngine.AI.NavMeshAgent>();

        _lastPosition = transform.position;
        EnsureSource();
    }

    /// <summary>
    /// Applies the settings once the owner has finished configuring this component.
    ///
    /// It has to be here rather than only in Awake: <c>AddComponent</c> runs Awake *immediately*,
    /// so a spawner that adds this and then sets <see cref="isLocalListener"/> — which is exactly
    /// what the bootstrap does — would otherwise leave the local player's own steps positioned in
    /// 3D around a listener sitting inside them.
    /// </summary>
    private void Start() => ApplySettings();

    /// <summary>Call after changing any of the fields above at runtime.</summary>
    public void Refresh()
    {
        EnsureSource();
        ApplySettings();
    }

    /// <summary>
    /// Builds the source if it has gone. The reference is a plain field that a domain reload wipes
    /// without re-running Awake, and the failure is inaudible rather than loud, so it is worth the
    /// cheap re-check on every play.
    /// </summary>
    private void EnsureSource()
    {
        if (_source != null) return;

        Transform existing = transform.Find("Footsteps");
        var go = existing != null ? existing.gameObject : new GameObject("Footsteps");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;   // at the feet: that is where the sound is

        _source = go.GetComponent<AudioSource>();
        if (_source == null) _source = go.AddComponent<AudioSource>();

        ApplySettings();
    }

    private void ApplySettings()
    {
        if (_source == null) return;

        _source.playOnAwake = false;
        _source.loop = false;
        _source.dopplerLevel = 0f;      // a walking pace does not shift pitch; it only sounds broken
        _source.spread = 30f;
        _source.spatialBlend = isLocalListener ? 0f : 1f;
        _source.minDistance = fullVolumeRange;
        _source.maxDistance = hearingRange;
        _source.rolloffMode = AudioRolloffMode.Custom;

        // Built once. Rebuilding the curve per step would allocate one on every footfall of every
        // agent in the level.
        _source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, BuildRolloff());
    }

    /// <summary>
    /// Distance → loudness, over 0..1 of <see cref="hearingRange"/>. Roughly inverse-square through
    /// the middle so approaching someone reads as a steady swell, but pinned to 0 at the far end so
    /// there is a real edge to being heard.
    /// </summary>
    private AnimationCurve BuildRolloff()
    {
        float plateau = Mathf.Clamp01(fullVolumeRange / Mathf.Max(0.01f, hearingRange));

        var curve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(plateau, 1f),
            new Keyframe(Mathf.Lerp(plateau, 1f, 0.15f), 0.55f),
            new Keyframe(Mathf.Lerp(plateau, 1f, 0.30f), 0.28f),
            new Keyframe(Mathf.Lerp(plateau, 1f, 0.50f), 0.10f),
            new Keyframe(Mathf.Lerp(plateau, 1f, 0.75f), 0.03f),
            new Keyframe(1f, 0f));

        for (int i = 0; i < curve.length; i++)
            curve.SmoothTangents(i, 0.6f);

        return curve;
    }

    private void Update()
    {
        Vector3 position = transform.position;
        Vector3 delta = position - _lastPosition;
        _lastPosition = position;

        float speed = MeasureSpeed(delta);
        bool grounded = controller == null || controller.IsGrounded;

        HandleLanding(grounded);

        if (!grounded || speed < minSpeed)
        {
            // Reset the gait trackers, or stopping mid-stride banks a step that fires the instant
            // you move again.
            _hasLastSin = false;
            _travelled = 0f;
            return;
        }

        if (characterAnimator != null && characterAnimator.StrideSwing > 0.02f)
        {
            // The legs cross the vertical twice per cycle — once per foot — and that is exactly
            // where Sin(phase) changes sign.
            float swing = Mathf.Sin(characterAnimator.StridePhase);
            if (_hasLastSin && swing * _lastSin < 0f) Step(speed);
            _lastSin = swing;
            _hasLastSin = true;
            return;
        }

        _travelled += new Vector3(delta.x, 0f, delta.z).magnitude;
        if (_travelled < strideLength) return;

        _travelled = 0f;
        Step(speed);
    }

    private float MeasureSpeed(Vector3 delta)
    {
        if (controller != null) return controller.PlanarSpeed;
        if (navAgent != null && navAgent.enabled && navAgent.isOnNavMesh) return navAgent.velocity.magnitude;

        delta.y = 0f;
        return Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
    }

    private void HandleLanding(bool grounded)
    {
        if (grounded && !_wasGrounded && controller != null)
        {
            float impact = Mathf.Clamp01(Mathf.Abs(controller.VerticalVelocity) / Mathf.Max(0.1f, landFullSpeed));
            Play(LandClip(), landLoudness * Mathf.Lerp(0.45f, 1f, impact), Random.Range(0.94f, 1.02f));
        }
        _wasGrounded = grounded;
    }

    private void Step(float speed)
    {
        if (Time.time < _nextStepTime) return;
        _nextStepTime = Time.time + minStepInterval;

        float sprintSpeed = controller != null ? Mathf.Max(0.1f, controller.sprintSpeed) : 6f;
        float effort = Mathf.Clamp01(speed / sprintSpeed);
        float loudness = Mathf.Lerp(0.7f, sprintLoudness, effort);

        if (controller != null && controller.SneakHeld) loudness *= sneakLoudness;

        // Silent means silent: no voice is allocated at all. The cadence still advances above, so
        // letting go of Ctrl mid-stride does not fire a banked step.
        if (loudness <= 0.001f) return;

        Play(NextStepClip(), loudness, Random.Range(0.92f, 1.08f));
        StepCount++;
    }

    private void Play(AudioClip clip, float loudness, float pitch)
    {
        EnsureSource();
        if (clip == null || _source == null) return;

        // Cheap self-correction. Whoever spawned this may have set isLocalListener after Awake,
        // and a domain reload can leave a source configured for the wrong side; one float compare
        // per step is a much smaller price than a player hearing their own steps panned in 3D
        // around their own head.
        if (!Mathf.Approximately(_source.spatialBlend, isLocalListener ? 0f : 1f)) ApplySettings();

        _source.pitch = pitch;
        _source.PlayOneShot(clip, (isLocalListener ? selfVolume : volume) * loudness);
    }

    /// <summary>Never the same sample twice running — repetition is what makes footsteps read as a machine.</summary>
    private AudioClip NextStepClip()
    {
        AudioClip[] clips = StepClips();
        if (clips.Length == 0) return null;

        int index = Random.Range(0, clips.Length);
        if (index == _lastClip) index = (index + 1) % clips.Length;
        _lastClip = index;
        return clips[index];
    }

    // ============================================================ synthesis

    private static AudioClip[] StepClips()
    {
        if (_stepClips != null && _stepClips.Length > 0 && _stepClips[0] != null) return _stepClips;

        // Five pairs of the same shoes, not five different pairs: the tock pitch and the heel-toe
        // gap wander a little, everything else holds. Vary them more than this and each step
        // sounds like a different person.
        //
        // Heavy shoes: the tock sits an octave down, the weight is more than twice what it was and
        // holds far longer, and the click is pulled back so the slap tops the thud rather than
        // leading it.
        _stepClips = new AudioClip[5];
        for (int i = 0; i < _stepClips.Length; i++)
            _stepClips[i] = BuildShoeStep("Footstep " + i, 4021 + i * 977,
                tockHz: 300f + i * 38f,
                bodyHz: 60f + i * 4.5f,
                bodyDecay: 21f,
                toeDelay: 0.050f + i * 0.004f,
                toeLevel: 0.48f,
                clickLevel: 0.22f,
                bodyLevel: 1.15f,
                length: 0.36f);

        return _stepClips;
    }

    private static AudioClip LandClip()
    {
        if (_landClip != null) return _landClip;

        // Both shoes at once and flat, so there is no heel-then-toe — just a lower, longer slam
        // with all the weight behind it.
        _landClip = BuildShoeStep("Landing", 90210,
            tockHz: 225f, bodyHz: 46f, bodyDecay: 13f, toeDelay: 0.022f, toeLevel: 0.55f,
            clickLevel: 0.30f, bodyLevel: 1.5f, length: 0.55f);
        return _landClip;
    }

    /// <summary>
    /// A leather-soled shoe on a hard floor under thin carpet.
    ///
    /// A shoe is almost the opposite of the soft thud this used to make, and it is three things
    /// arriving in a particular order:
    /// <list type="bullet">
    /// <item>a <b>click</b> — the sole slapping down. Very short and bright, made by
    /// differentiating white noise, which is the cheapest high-pass there is.</item>
    /// <item>a <b>tock</b> — the hollow heel ringing. A damped sine near 1 kHz, plus an
    /// *inharmonic* partial at 1.63× so it reads as a knock on something solid rather than as a
    /// musical note. A whole-number ratio here would sound like a tuned drum.</item>
    /// <item>a <b>body</b> — the weight behind it, a fast low sine. Shoes have far less of this
    /// than a boot, and too much of it turns the walk back into a thud.</item>
    /// </list>
    ///
    /// Then the detail that actually sells it: the <b>toe tap</b>. Real dress shoes land heel
    /// first and the ball of the foot follows some 50 ms later, so each footfall is a soft
    /// double. One tap alone reads as a stick hitting a box.
    /// </summary>
    private static AudioClip BuildShoeStep(string name, int seed, float tockHz, float bodyHz,
        float bodyDecay, float toeDelay, float toeLevel, float clickLevel, float bodyLevel,
        float length)
    {
        const int sampleRate = 44100;
        int count = Mathf.RoundToInt(sampleRate * length);
        var samples = new float[count];

        var random = new System.Random(seed);
        float previousWhite = 0f;
        float lowPassed = 0f;

        // A couple of milliseconds of attack. Starting a waveform at full amplitude puts a click
        // in front of every step, and a click is the one thing the ear never forgives.
        int attack = Mathf.Max(1, sampleRate / 900);

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)sampleRate;

            float white = (float)(random.NextDouble() * 2.0 - 1.0);
            float bright = white - previousWhite;                 // differentiator = high-pass
            previousWhite = white;
            lowPassed = Mathf.Lerp(lowPassed, white, 0.12f);      // grit, darker still

            float value = Tap(t, bright, lowPassed);

            // The ball of the foot, arriving after the heel and softer.
            float toe = t - toeDelay;
            if (toe >= 0f) value += Tap(toe, bright, lowPassed) * toeLevel;

            float envelope = i < attack ? i / (float)attack : 1f;
            samples[i] = value * envelope * 0.8f;
        }

        // Tail to zero so the clip cannot end on a discontinuity.
        int fade = Mathf.Min(count, sampleRate / 200);
        for (int i = 0; i < fade; i++)
            samples[count - fade + i] *= 1f - i / (float)fade;

        // Normalise. The click, the tock and the weight all peak within a millisecond of each
        // other, so the sum overshoots 1.0 and SetData clips it into distortion — measured at 1.24
        // before this. It also lands differently for each variant, which reads as an uneven walk
        // rather than as variety.
        float loudest = 0f;
        for (int i = 0; i < count; i++) loudest = Mathf.Max(loudest, Mathf.Abs(samples[i]));
        if (loudest > 1e-4f)
        {
            float scale = 0.92f / loudest;
            for (int i = 0; i < count; i++) samples[i] *= scale;
        }

        var clip = AudioClip.Create(name, count, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;

        float Tap(float u, float bright, float grit)
        {
            float click = bright * Mathf.Exp(-u * 300f) * clickLevel;
            // The tock is kept alive even down here on purpose: 60 Hz alone is inaudible on a
            // laptop speaker, and this partial is what survives to say a step happened at all.
            float tock = Mathf.Sin(2f * Mathf.PI * tockHz * u) * Mathf.Exp(-u * 55f) * 0.30f;
            float knock = Mathf.Sin(2f * Mathf.PI * tockHz * 1.63f * u) * Mathf.Exp(-u * 95f) * 0.12f;

            // The weight. A slow decay here is what separates a heavy shoe from a light one —
            // the low end has to still be there when the toe lands, or the step reads as a tap
            // with a thud stapled to the front of it.
            float weight = Mathf.Sin(2f * Mathf.PI * bodyHz * u) * Mathf.Exp(-u * bodyDecay) * bodyLevel;
            float scuff = grit * Mathf.Exp(-u * 70f) * 0.18f;

            return click + tock + knock + weight + scuff;
        }
    }
}
