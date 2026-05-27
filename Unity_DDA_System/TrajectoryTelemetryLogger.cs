using System;
using UnityEngine;
using ArcadeVP;

public class TrajectoryTelemetryLogger : MonoBehaviour
{
    [Header("Enable/Disable")]
    public bool enableLogging = true;

    [Header("Sampling")]
    [Range(1f, 30f)] public float sampleHz = 10f;

    [Header("References")]
    public ArcadeVehicleController car;

    [Header("Flush")]
    public float flushEverySeconds = 1.0f;

    private CsvLogWriter _writer;
    private string _activeSessionId = "";
    private float _accum = 0f;

    private void Update()
    {
        if (!enableLogging) return;

        // If the telemetry session has changed or ended, dispose the old writer to start fresh on the next log
        if (_writer != null && (!TelemetrySessionContext.IsActive || TelemetrySessionContext.SessionId != _activeSessionId))
        {
            _writer.Dispose();
            _writer = null;
        }

        if (!PlayerJoinManager.IsRaceStarted) return;

        // if no car reference is set, try to find one in the scene
        if (car == null)
        {
            car = FindObjectOfType<ArcadeVehicleController>();
        }

        // if we still don't have a car reference, we can't log anything, so just return
        if (car == null) return;
        // ----------------------------------------------------------------------

        if (_writer == null)
        {
            if (!TelemetrySessionContext.IsActive)
            {
                TelemetrySessionContext.StartNew(-1);
            }

            _activeSessionId = TelemetrySessionContext.SessionId;

            string[] meta =
            {
                $"# sessionId={TelemetrySessionContext.SessionId}",
                $"# playerCount={TelemetrySessionContext.PlayerCount}",
                $"# startedUtc={TelemetrySessionContext.StartedUtcIso}"
            };

            string header =
                "t,sessionId,driverId,trackT,lane,speed,speedLimit,overshootRatio,isDrifting,isGrounded,cornerAngle,curvature," +
                "spawnPressure,precision,punishment,rewardBias,cornerSmooth01,laneJitter01,overspeedControl01";

            _writer = new CsvLogWriter(
                TelemetrySessionContext.MakeFilePath("trajectory"),
                meta,
                header,
                flushEverySeconds
            );
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        _accum += dt;
        float period = 1f / Mathf.Max(1f, sampleHz);
        if (_accum < period) return;
        _accum -= period;

        string driverId = GetDriverIdSafe();

        var dir = DifficultyDirector.Instance;
        float sp = dir ? Mathf.Clamp01(dir.Current.spawnPressure) : 0.5f;
        float pr = dir ? Mathf.Clamp01(dir.Current.precision) : 0.5f;
        float pu = dir ? Mathf.Clamp01(dir.Current.punishment) : 0.5f;
        float rb = dir ? Mathf.Clamp01(dir.Current.rewardBias) : 0.5f;

        float t = TelemetrySessionContext.TimeSinceStart();

        _writer.AppendRow(
            t,
            TelemetrySessionContext.SessionId,
            driverId,
            Mathf.Clamp01(car.TrackTNormalized),
            car.CurrentLane,
            car.Speed,
            car.ActiveSpeedLimit,
            car.CurrentOvershootRatio,
            car.IsDrifting ? 1 : 0,
            car.IsGrounded ? 1 : 0,
            car.DetectedCornerAngle,
            car.CurrentCurvature,
            sp, pr, pu, rb,
            car.LastCornerSmooth01,
            car.LastLaneJitter01,
            car.LastOverspeedControl01
        );
    }

    private void OnDestroy()
    {
        _writer?.Dispose();
    }

    private string GetDriverIdSafe()
    {
        if (PlayerManager.Instance && PlayerManager.Instance.CurrentDriver)
            return PlayerManager.Instance.CurrentDriver.name;
        return "unknown_driver";
    }
}