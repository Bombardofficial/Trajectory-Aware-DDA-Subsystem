// DifficultyDirector.cs — multi-player aware, per-player difficulty states + DRIVER TENURE RUBBERBAND
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct DifficultyState
{
    public float overall;        // 0..1
    public float spawnPressure;  // 0..1 — obstacle sûrûség
    public float precision;      // 0..1 — mini-game tightness
    public float punishment;     // 0..1 — bünti/lock/crash keménység
    public float rewardBias;     // 0..1 — 0: obstacle-heavy, 1: reward-heavy
}

public class DifficultyDirector : MonoBehaviour
{
    public static DifficultyDirector Instance { get; private set; }

    [Header("Baseline Toggle")]
    [Tooltip("If FALSE, the game runs in BASELINE mode (fixed 0.5 skill, no adaptation).")]
    public bool enableAdaptation = true;

    [Header("Tick")]
    [Tooltip("How often the director polls skill & updates difficulty (seconds).")]
    public float tickInterval = 0.25f;

    [Tooltip("How fast difficulty can move towards the new target per second.")]
    public float rampPerSecond = 0.75f;

    [Header("Mapping from skill->difficulty")]
    [Tooltip("Gamma for skill (0..1) before mapping. >1 = hangsúly a magas skill tartományra.")]
    [Range(0.2f, 3f)] public float gammaOverall = 1.0f;

    [Tooltip("Spawn pressure (obstacles) as a function of skill.")]
    public AnimationCurve spawnPressureMap = AnimationCurve.Linear(0, 0.3f, 1, 1f);

    [Tooltip("Mini-game precision as a function of skill.")]
    public AnimationCurve precisionMap = AnimationCurve.Linear(0, 0.2f, 1, 1f);

    [Tooltip("Punishment (büntetés keménysége) as a function of skill.")]
    public AnimationCurve punishmentMap = AnimationCurve.Linear(0, 0.2f, 1, 1f);

    [Tooltip("Reward bias as a function of skill (low skill -> több collectible).")]
    public AnimationCurve rewardBiasMap = AnimationCurve.Linear(0, 0.7f, 1, 0.3f);

    [Header("Ramp direction")]
    public float rampUpPerSecond = 0.45f;    // nehezítés
    public float rampDownPerSecond = 1.10f;  // könnyítés

    // ===================== DRIVER TENURE RUBBERBAND =====================
    [Header("Driver Tenure Rubberband (gap the driver)")]
    public bool enableDriverTenure = true;

    [Tooltip("Lépcsõ hossza (sec). Te 15-öt akartál.")]
    public float tenureStepSeconds = 15f;

    [Tooltip("Hány lépcsõ után legyen maxos a 'gap' (pl. 6 -> 90 sec után tetõzik).")]
    [Min(1)] public int tenureMaxSteps = 6;

    [Tooltip("Ha kell egy kis grace, amíg nem büntetjük a drivert (sec). 0 = azonnal lépcsõzünk 15s-onként.")]
    [Min(0f)] public float tenureGraceSeconds = 0f;

    [Tooltip("A lépcsõzött 0..1 tenure-t ezzel formázzuk. Linear jó kezdés. (0->0, 1->1)")]
    public AnimationCurve tenureCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Header("Tenure effect strengths (applied to CURRENT DRIVER only)")]
    [Tooltip("Max mennyivel tolja fel a spawnPressure-t max tenure-nél.")]
    [Range(0f, 1f)] public float spawnPressureBoostAtMaxTenure = 0.35f;

    [Tooltip("Max mennyivel húzza le a rewardBias-t max tenure-nél (kevesebb collectible).")]
    [Range(0f, 1f)] public float rewardBiasPenaltyAtMaxTenure = 0.35f;

    [Tooltip("Opcionális: mini-game szigorítás max tenure-nél.")]
    [Range(0f, 1f)] public float precisionBoostAtMaxTenure = 0.10f;

    [Tooltip("Opcionális: bünti keményítés max tenure-nél.")]
    [Range(0f, 1f)] public float punishmentBoostAtMaxTenure = 0.12f;

    [Header("Scale tenure by how much better the driver is (recommended ON)")]
    public bool scaleTenureByAdvantage = true;

    [Tooltip("Ha a driver csak kicsivel jobb, már induljon valamennyi gap (pozitív bias).")]
    [Range(-0.2f, 0.2f)] public float advantageBias = 0.06f;

    [Tooltip("Akkora skill-különbségnél legyen 100% hatás, amit itt adsz meg (0.25-0.45 jó).")]
    [Range(0.05f, 0.8f)] public float advantageRange = 0.35f;

    [Tooltip("Ha nincs advantage, ennyi szorzót azért kap a tenure (ne legyen 0).")]
    [Range(0f, 1f)] public float minAdvantageMultiplier = 0.25f;

    [Header("Extra: scale tenure by driver skill too (prevents bullying low-skill drivers)")]
    [Tooltip("0 = tenure nem függ a driver skilltõl. 1 = tenure teljesen a skilltõl függ.")]
    [Range(0f, 1f)] public float scaleTenureByDriverSkill = 0.65f;

    // runtime debug (read-only)
    [Header("Runtime (read-only)")]
    public float currentDriverTenureSeconds;
    public int currentDriverTenureSteps;
    public float currentDriverTenure01;
    public float currentDriverAdvantage01;

    // ===================== PUBLIC READ ACCESS =====================

    public DifficultyState GlobalCurrent => _globalPublished;
    public IReadOnlyDictionary<Passenger, DifficultyState> PerPlayerCurrent => _perPlayerPublished;
    public Passenger CurrentDriver { get; private set; }

    public DifficultyState Current
    {
        get
        {
            if (CurrentDriver && _perPlayerPublished.TryGetValue(CurrentDriver, out var s))
                return s;
            return _globalPublished;
        }
    }

    // ===================== INTERNAL STATE =====================

    DifficultyState _globalPublished;
    readonly Dictionary<Passenger, DifficultyState> _perPlayerPublished = new();

    float _lastTick;

    float _driverStartTime = 0f;

    static readonly List<Passenger> _toRemove = new();

    void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        float baseSkill = 0.5f;
        _globalPublished = ComputeDesiredFromSkill(baseSkill);
    }

    void OnEnable()
    {
        PlayerManager.OnDriverChanged += HandleDriverChanged;
        if (PlayerManager.Instance && PlayerManager.Instance.CurrentDriver)
        {
            CurrentDriver = PlayerManager.Instance.CurrentDriver;
            _driverStartTime = Time.time;
        }
    }

    void OnDisable()
    {
        PlayerManager.OnDriverChanged -= HandleDriverChanged;
    }

    void HandleDriverChanged(Passenger oldP, Passenger newP)
    {
        CurrentDriver = newP;
        _driverStartTime = Time.time;

        // reset runtime debug
        currentDriverTenureSeconds = 0f;
        currentDriverTenureSteps = 0;
        currentDriverTenure01 = 0f;
        currentDriverAdvantage01 = 0f;
    }

    void Update()
    {
        if (Time.time - _lastTick < tickInterval)
            return;

        _lastTick = Time.time;

        var est = SkillEstimator.Instance;
        if (est == null)
            return;

        float step = rampPerSecond * tickInterval;

        // -------- 1) Global difficulty (optional / telemetry) --------
        float gSkill = 0.5f;
        float gConf = 0f;

        if (est.Global != null)
        {
            gSkill = est.Global.Skill01;
            gConf = est.Global.Confidence;
        }

        float gSkillConf = Mathf.Lerp(0.5f, gSkill, gConf);
        var gDesired = ComputeDesiredFromSkill(gSkillConf);
        _globalPublished = RampTowards(_globalPublished, gDesired, step);

        // -------- 2) Per-player difficulty from profiles --------
        var profiles = est.Profiles;
        if (profiles == null) return;

        // cleanup dead keys
        _toRemove.Clear();
        foreach (var kv in _perPlayerPublished)
            if (!kv.Key) _toRemove.Add(kv.Key);
        foreach (var dead in _toRemove)
            _perPlayerPublished.Remove(dead);
        _toRemove.Clear();

        // Precompute tenure/advantage for current driver (so consistent within this tick)
        float driverTenure01Local = 0f;
        float driverAdv01Local = 0f;

        if (enableDriverTenure && CurrentDriver && profiles.TryGetValue(CurrentDriver, out var driverProf) && driverProf != null)
        {
            // tenure seconds
            currentDriverTenureSeconds = Mathf.Max(0f, Time.time - _driverStartTime);

            // stepwise tenure (15s steps)
            float tEff = Mathf.Max(0f, currentDriverTenureSeconds - tenureGraceSeconds);
            int stepsDone = (tenureStepSeconds <= 0.01f) ? 0 : Mathf.FloorToInt(tEff / tenureStepSeconds);
            stepsDone = Mathf.Clamp(stepsDone, 0, tenureMaxSteps);
            currentDriverTenureSteps = stepsDone;

            float baseTenure01 = (tenureMaxSteps <= 0) ? 0f : (stepsDone / (float)tenureMaxSteps);
            baseTenure01 = Mathf.Clamp01(baseTenure01);
            baseTenure01 = Mathf.Clamp01(tenureCurve.Evaluate(baseTenure01));

            // driver skill (confidence blended)
            float dSkill = Mathf.Lerp(0.5f, driverProf.Skill01, driverProf.Confidence);
            float dSkillGamma = Mathf.Pow(Mathf.Clamp01(dSkill), gammaOverall);

            // average other players skill
            float sum = 0f; int cnt = 0;
            foreach (var kv in profiles)
            {
                var p = kv.Key;
                var prof = kv.Value;
                if (!p || prof == null) continue;
                if (p == CurrentDriver) continue;

                float s = Mathf.Lerp(0.5f, prof.Skill01, prof.Confidence);
                sum += Mathf.Clamp01(s);
                cnt++;
            }
            float otherAvg = (cnt > 0) ? (sum / cnt) : dSkill;

            // advantage 0..1
            float adv01 = 0f;
            if (scaleTenureByAdvantage && cnt > 0)
            {
                float diff = (dSkill - otherAvg) + advantageBias;
                adv01 = Mathf.Clamp01(diff / Mathf.Max(0.001f, advantageRange));
            }

            float advMult = scaleTenureByAdvantage ? Mathf.Lerp(minAdvantageMultiplier, 1f, adv01) : 1f;

            // also scale by driver skill (so low skill driver isn't bullied)
            float skillMult = Mathf.Lerp(1f, Mathf.Lerp(0.35f, 1f, dSkillGamma), scaleTenureByDriverSkill);

            driverTenure01Local = baseTenure01 * advMult * skillMult;
            driverTenure01Local = Mathf.Clamp01(driverTenure01Local);

            driverAdv01Local = adv01;

            currentDriverTenure01 = driverTenure01Local;
            currentDriverAdvantage01 = driverAdv01Local;
        }
        else
        {
            currentDriverTenureSeconds = 0f;
            currentDriverTenureSteps = 0;
            currentDriverTenure01 = 0f;
            currentDriverAdvantage01 = 0f;
        }

        // Now compute per-player desired + ramp
        foreach (var kv in profiles)
        {
            var p = kv.Key;
            var prof = kv.Value;
            if (!p || prof == null) continue;

            float s = Mathf.Lerp(0.5f, prof.Skill01, prof.Confidence);
            if (!enableAdaptation) s = 0.5f; // BASELINE: Erõltetett fix közepes nehézség
            float sGamma = Mathf.Pow(Mathf.Clamp01(s), gammaOverall);

            var desired = ComputeDesiredFromSkill(sGamma);

            // Apply driver tenure rubberband ONLY to current driver
            if (enableAdaptation && enableDriverTenure && p == CurrentDriver && driverTenure01Local > 0f)
            {
                desired = ApplyDriverTenure(desired, driverTenure01Local);
            }

            if (_perPlayerPublished.TryGetValue(p, out var current))
                _perPlayerPublished[p] = RampTowards(current, desired, step);
            else
                _perPlayerPublished[p] = desired;
        }
    }

    // ===================== Helpers =====================

    DifficultyState ApplyDriverTenure(DifficultyState s, float tenure01)
    {
        // IMPORTANT: clamp everything 0..1 (world spawner uses these)
        s.spawnPressure = Mathf.Clamp01(s.spawnPressure + tenure01 * spawnPressureBoostAtMaxTenure);
        s.rewardBias = Mathf.Clamp01(s.rewardBias - tenure01 * rewardBiasPenaltyAtMaxTenure);

        if (precisionBoostAtMaxTenure > 0f)
            s.precision = Mathf.Clamp01(s.precision + tenure01 * precisionBoostAtMaxTenure);

        if (punishmentBoostAtMaxTenure > 0f)
            s.punishment = Mathf.Clamp01(s.punishment + tenure01 * punishmentBoostAtMaxTenure);

        return s;
    }

    DifficultyState ComputeDesiredFromSkill(float skill01)
    {
        float s = Mathf.Clamp01(skill01);
        float sGamma = Mathf.Pow(s, gammaOverall);

        return new DifficultyState
        {
            overall = sGamma,
            spawnPressure = Mathf.Clamp01(spawnPressureMap.Evaluate(sGamma)),
            precision = Mathf.Clamp01(precisionMap.Evaluate(sGamma)),
            punishment = Mathf.Clamp01(punishmentMap.Evaluate(sGamma)),
            rewardBias = Mathf.Clamp01(rewardBiasMap.Evaluate(sGamma)),
        };
    }

    float Step(float from, float to, float dt)
    {
        float r = (to > from) ? rampUpPerSecond : rampDownPerSecond;
        return r * dt;
    }

    DifficultyState RampTowards(DifficultyState from, DifficultyState to, float step)
    {
        from.overall = Mathf.MoveTowards(from.overall, to.overall, step);

        // spawnPressure külön rampol (mert ez a legérzékenyebb)
        from.spawnPressure = Mathf.MoveTowards(from.spawnPressure, to.spawnPressure, Step(from.spawnPressure, to.spawnPressure, tickInterval));

        from.precision = Mathf.MoveTowards(from.precision, to.precision, step);
        from.punishment = Mathf.MoveTowards(from.punishment, to.punishment, step);
        from.rewardBias = Mathf.MoveTowards(from.rewardBias, to.rewardBias, step);

        return from;
    }
}
