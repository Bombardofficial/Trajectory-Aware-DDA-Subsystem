// SkillEstimator.cs — production-ready: event-decayed EMAs + trajectory EMA + smoothed skill
using System.Collections.Generic;
using UnityEngine;

public class SkillEstimator : MonoBehaviour
{
    public static SkillEstimator Instance { get; private set; }

    // ===== Tunables (Inspector) =====

    [Header("EMA memory for DISCRETE events (in events, not seconds!)")]
    [Tooltip("Approx. number of recent COLLECT events that influence the EMA strongly.\n" +
             "E.g. 40 = about the last 40 collectible events matter most.")]
    public float windowCollect = 40f;

    [Tooltip("Approx. number of recent OBSTACLE events that influence the EMA strongly.")]
    public float windowObstacle = 40f;

    [Tooltip("Approx. number of recent MINI-GAME events that influence the EMA strongly.")]
    public float windowMiniGame = 20f;

    [Header("Trajectory smoothing (seconds)")]
    [Tooltip("Smoothing window (seconds) for continuous trajectory samples " +
             "(corner quality, lane jitter, overspeed control).")]
    public float windowTrajectory = 12f;

    [Header("Weights -> score (before logistic)")]
    [Range(0f, 3f)] public float wCollect = 1.0f;   // higher is better
    [Range(0f, 3f)] public float wMiss = 0.7f;      // subtracts
    [Range(0f, 3f)] public float wHit = 1.3f;       // subtracts

    [Header("Mini-game weights")]
    [Range(0f, 3f)] public float wMgPass = 1.0f;
    [Range(0f, 3f)] public float wMgPerfect = 1.5f;
    [Range(0f, 3f)] public float wMgFail = 1.2f;

    [Header("Mini-game fail severity multipliers")]
    [Tooltip("How much a full crash counts compared to a simple stumble.")]
    public float mgFailWeightStumble = 0.6f;
    public float mgFailWeightCrash = 1.0f;

    [Header("Trajectory weights")]
    [Tooltip("How strongly overall corner smoothness contributes to skill.")]
    [Range(0f, 3f)] public float wCorner = 1.0f;

    [Tooltip("Penalty for steering jitter when not actually changing lane.")]
    [Range(0f, 3f)] public float wLaneJitter = 0.8f;

    [Tooltip("Reward for staying under or near the speed-limit instead of constantly overspeeding.")]
    [Range(0f, 3f)] public float wOverspeedControl = 1.0f;

    [Header("Logistic squashing to 0..1")]
    [Tooltip("Steepness of the logistic curve. Lower = softer, less \"all or nothing\".")]
    [Range(0.5f, 4f)] public float slope = 1.4f;

    [Tooltip("Bias shift before logistic. 0 = centered; >0 makes same score feel more skilled.")]
    [Range(-1f, 1f)] public float bias = 0.0f;

    [Header("Skill smoothing")]
    [Tooltip("Time (in seconds) for Skill01 to move ~63% toward a new target value.\n" +
             "Smaller = more twitchy, larger = more sluggish but stable.")]
    public float skillSmoothSeconds = 0.5f;

    [Header("Debug / Export")]
    public bool writeTotals = true;

    [Header("Confidence")]
    [Tooltip("EMA mass where confidence ~= 1.0.\n" +
             "Good starting point: windowCollect + windowObstacle + windowMiniGame.")]
    public float confidenceEventsForFull = 100f;

    // ===== Per-player profile =====
    public sealed class Profile
    {
        // --- Event-decayed EMAs (these ARE NOT totals, they decay) ---
        // Collectibles
        public float colOppEMA, colGotEMA, colMissEMA;
        // Obstacles
        public float obsOppEMA, obsHitEMA;
        // Mini-game
        public float mgOppEMA, mgPassEMA, mgPerfectEMA, mgFailEMA;

        // --- Trajectory time-based EMAs (0..1) ---
        public float cornerSmoothEMA;        // HIGH = good
        public float laneJitterEMA;          // HIGH = bad
        public float overspeedControlEMA;    // HIGH = good

        // Totals (optional, for debug / telemetry)
        public int colOppTotal, colGotTotal, colMissTotal;
        public int obsOppTotal, obsHitTotal;
        public int mgOppTotal, mgPassTotal, mgPerfectTotal, mgFailTotal;
        public int trajSamplesTotal;

        // Derived rates
        public float CollectSuccessRate => SafeRatio(colGotEMA, colOppEMA);
        public float CollectMissRate => SafeRatio(colMissEMA, colOppEMA);
        public float ObstacleHitRate => SafeRatio(obsHitEMA, obsOppEMA);

        public float MiniGamePassRate => SafeRatio(mgPassEMA, mgOppEMA);
        public float MiniGamePerfectRate => SafeRatio(mgPerfectEMA, mgOppEMA);
        public float MiniGameFailRate => SafeRatio(mgFailEMA, mgOppEMA);

        // Trajectory exposures
        public float CornerSmoothness => Mathf.Clamp01(cornerSmoothEMA);
        public float LaneJitter => Mathf.Clamp01(laneJitterEMA);
        public float OverspeedControl => Mathf.Clamp01(overspeedControlEMA);

        public float Skill01 { get; private set; } = 0.5f;

        readonly SkillEstimator _root;
        public Profile(SkillEstimator root) { _root = root; }

        // Confidence is based on *recent* effective mass (EMA), not totals
        public float Confidence
        {
            get
            {
                float mass = colOppEMA + obsOppEMA + mgOppEMA;
                return Mathf.Clamp01(mass / Mathf.Max(1f, _root.confidenceEventsForFull));
            }
        }

        // --- Core tick (smooth skill) ---
        public void Tick(float dt)
        {
            float score = 0f;

            // Collectibles & obstacles
            score += _root.wCollect * CollectSuccessRate;
            score -= _root.wMiss * CollectMissRate;
            score -= _root.wHit * ObstacleHitRate;

            // Mini-game
            score += _root.wMgPass * MiniGamePassRate;
            score += _root.wMgPerfect * MiniGamePerfectRate;
            score -= _root.wMgFail * MiniGameFailRate;

            // Trajectory
            score += _root.wCorner * CornerSmoothness;
            score -= _root.wLaneJitter * LaneJitter;
            score += _root.wOverspeedControl * OverspeedControl;

            // Logistic squash
            float x = _root.slope * (score + _root.bias);
            float target = 1f / (1f + Mathf.Exp(-x));

            // Smooth toward target
            float alphaSkill = AlphaTime(_root.skillSmoothSeconds, dt);
            Skill01 = Mathf.Lerp(Skill01, target, alphaSkill);
        }

        // ===== Helpers =====

        // Event-decay factor: after 1 "event", remaining mass = exp(-1/windowEvents)
        float DecayFactorEvents(float windowEvents)
        {
            float w = Mathf.Max(1f, windowEvents);
            return Mathf.Exp(-1f / w);
        }

        // Time-based EMA alpha: 63% response at windowSeconds
        float AlphaTime(float windowSeconds, float dt)
        {
            float w = Mathf.Max(0.001f, windowSeconds);
            return 1f - Mathf.Exp(-dt / w);
        }

        void DecayCollect()
        {
            float d = DecayFactorEvents(_root.windowCollect);
            colOppEMA *= d; colGotEMA *= d; colMissEMA *= d;
        }

        void DecayObstacle()
        {
            float d = DecayFactorEvents(_root.windowObstacle);
            obsOppEMA *= d; obsHitEMA *= d;
        }

        void DecayMiniGame()
        {
            float d = DecayFactorEvents(_root.windowMiniGame);
            mgOppEMA *= d; mgPassEMA *= d; mgPerfectEMA *= d; mgFailEMA *= d;
        }

        static float SafeRatio(float num, float den)
            => (den <= 1e-4f) ? 0f : Mathf.Clamp01(num / den);

        // ===== Event API (Collectibles) =====
        public void OnCollectibleSpawned()
        {
            DecayCollect();
            colOppEMA += 1f;
            if (_root.writeTotals) colOppTotal++;
        }

        public void OnCollectibleCollected()
        {
            DecayCollect();
            colGotEMA += 1f;
            if (_root.writeTotals) colGotTotal++;
        }

        public void OnCollectibleMissed()
        {
            DecayCollect();
            colMissEMA += 1f;
            if (_root.writeTotals) colMissTotal++;
        }

        // ===== Event API (Obstacles) =====
        public void OnObstacleSpawned()
        {
            DecayObstacle();
            obsOppEMA += 1f;
            if (_root.writeTotals) obsOppTotal++;
        }

        public void OnObstacleHit()
        {
            DecayObstacle();
            obsHitEMA += 1f;
            if (_root.writeTotals) obsHitTotal++;
        }

        // ===== Event API (Mini-game) =====
        public void OnMiniGameStarted()
        {
            DecayMiniGame();
            mgOppEMA += 1f;
            if (_root.writeTotals) mgOppTotal++;
        }

        public void OnMiniGamePass()
        {
            DecayMiniGame();
            mgPassEMA += 1f;
            if (_root.writeTotals) mgPassTotal++;
        }

        public void OnMiniGamePerfect()
        {
            DecayMiniGame();
            mgPerfectEMA += 1f;
            if (_root.writeTotals) mgPerfectTotal++;
        }

        public void OnMiniGameFail(bool fullCrash)
        {
            DecayMiniGame();
            float w = fullCrash ? _root.mgFailWeightCrash : _root.mgFailWeightStumble;
            mgFailEMA += w;
            if (_root.writeTotals) mgFailTotal++;
        }

        // ===== Trajectory samples (time-based EMA) =====
        public void OnTrajectorySample(
            float cornerSmooth01,
            float laneJitter01,
            float overspeedControl01,
            float windowSeconds,
            float dt)
        {
            float a = AlphaTime(windowSeconds, dt);

            if (cornerSmooth01 >= 0f)
                cornerSmoothEMA = Mathf.Lerp(cornerSmoothEMA, Mathf.Clamp01(cornerSmooth01), a);

            if (laneJitter01 >= 0f)
                laneJitterEMA = Mathf.Lerp(laneJitterEMA, Mathf.Clamp01(laneJitter01), a);

            if (overspeedControl01 >= 0f)
                overspeedControlEMA = Mathf.Lerp(overspeedControlEMA, Mathf.Clamp01(overspeedControl01), a);

            if (cornerSmooth01 >= 0f || laneJitter01 >= 0f || overspeedControl01 >= 0f)
            {
                if (_root.writeTotals) trajSamplesTotal++;
            }
        }
    }

    // ===== Runtime state =====
    readonly Dictionary<Passenger, Profile> _profiles = new();
    Profile _active;                 // null if no active driver
    Passenger _activePassenger;

    public Profile Global { get; private set; }
    public Profile Active => _active; // NOTE: can be null (intentionally)
    public IReadOnlyDictionary<Passenger, Profile> Profiles => _profiles;
    public Passenger ActivePassenger => _activePassenger;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Global = new Profile(this);
    }

    void OnEnable()
    {
        PlayerManager.OnDriverChanged += HandleDriverChanged;

        if (PlayerManager.Instance && PlayerManager.Instance.CurrentDriver)
            SetActiveDriver(PlayerManager.Instance.CurrentDriver);
    }

    void OnDisable()
    {
        PlayerManager.OnDriverChanged -= HandleDriverChanged;
    }

    void HandleDriverChanged(Passenger oldP, Passenger newP) => SetActiveDriver(newP);

    void SetActiveDriver(Passenger p)
    {
        _activePassenger = p;

        if (p == null)
        {
            _active = null;
            return;
        }

        if (!_profiles.TryGetValue(p, out var prof))
        {
            prof = new Profile(this);
            _profiles.Add(p, prof);
        }

        _active = prof;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        Global?.Tick(dt);
        foreach (var kv in _profiles)
            kv.Value.Tick(dt);
    }

    // ===== Public API: collectibles =====
    public void OnCollectibleSpawned()
    {
        Global?.OnCollectibleSpawned();
        _active?.OnCollectibleSpawned();
    }

    public void OnCollectibleCollected()
    {
        Global?.OnCollectibleCollected();
        _active?.OnCollectibleCollected();
    }

    public void OnCollectibleMissed()
    {
        Global?.OnCollectibleMissed();
        _active?.OnCollectibleMissed();
    }

    // ===== Public API: obstacles =====
    public void OnObstacleSpawned()
    {
        Global?.OnObstacleSpawned();
        _active?.OnObstacleSpawned();
    }

    public void OnObstacleHit()
    {
        Global?.OnObstacleHit();
        _active?.OnObstacleHit();
    }

    // ===== Public API: mini-game =====
    public void OnMiniGameStarted()
    {
        Global?.OnMiniGameStarted();
        _active?.OnMiniGameStarted();
    }

    public void OnMiniGamePass()
    {
        Global?.OnMiniGamePass();
        _active?.OnMiniGamePass();
    }

    public void OnMiniGamePerfect()
    {
        Global?.OnMiniGamePerfect();
        _active?.OnMiniGamePerfect();
    }

    public void OnMiniGameFail(bool fullCrash)
    {
        Global?.OnMiniGameFail(fullCrash);
        _active?.OnMiniGameFail(fullCrash);
    }

    // ===== Public API: trajectory =====
    public void OnTrajectorySample(float cornerSmooth01, float laneJitter01, float overspeedControl01, float dt)
    {
        // hard safety
        if (dt <= 0f) return;

        Global?.OnTrajectorySample(cornerSmooth01, laneJitter01, overspeedControl01, windowTrajectory, dt);
        _active?.OnTrajectorySample(cornerSmooth01, laneJitter01, overspeedControl01, windowTrajectory, dt);
    }
}
