using UnityEngine;

public class SkillTelemetryLogger : MonoBehaviour
{
    [Header("Logging frequency")]
    [Tooltip("Seconds between log rows.")]
    public float logInterval = 0.5f;

    [Header("Enable/Disable")]
    public bool enableLogging = true;

    [Header("Flush")]
    public float flushEverySeconds = 1.0f;

    private CsvLogWriter _writer;
    private string _activeSessionId = "";
    private float _nextLogTime;
    private bool _raceWasRunning;

    private void OnEnable()
    {
        _nextLogTime = Time.time + logInterval;
    }

    private void OnDisable()
    {
        _writer?.Dispose();
        _writer = null;
    }

    private void Update()
    {
        if (!enableLogging) return;

        // If session has changed since last log, dispose old writer to start a new file on next log
        if (_writer != null && (!TelemetrySessionContext.IsActive || TelemetrySessionContext.SessionId != _activeSessionId))
        {
            _writer.Dispose();
            _writer = null;
        }

        bool raceRunning = PlayerJoinManager.IsRaceStarted;

        if (raceRunning && !_raceWasRunning)
        {
            EnsureWriter();
        }

        _raceWasRunning = raceRunning;

        if (!raceRunning)
            return;

        if (Time.time < _nextLogTime)
            return;

        _nextLogTime = Time.time + logInterval;

        if (_writer == null)
            EnsureWriter();

        if (_writer == null)
            return;

        LogFrame();
    }

    private void EnsureWriter()
    {
        if (_writer != null) return;

        if (!TelemetrySessionContext.IsActive)
        {
            int playerCount = GuessPlayerCount();
            TelemetrySessionContext.StartNew(playerCount);
        }

        _activeSessionId = TelemetrySessionContext.SessionId;

        string[] meta =
        {
            $"# sessionId={TelemetrySessionContext.SessionId}",
            $"# playerCount={TelemetrySessionContext.PlayerCount}",
            $"# startedUtc={TelemetrySessionContext.StartedUtcIso}"
        };

        string header =
            "time,sessionId,currentDriverPlayerNum," +
            "p1Points,p2Points,p3Points,p4Points," +
            "globalSkill,globalConf,globalOverall,globalSpawn,globalPrecision,globalPunishment,globalRewardBias," +
            "driverSkill,driverConf,driverOverall,driverSpawn,driverPrecision,driverPunishment,driverRewardBias," +
            "driverCollectSuccess,driverCollectMiss,driverObstacleHit," +
            "driverMgPass,driverMgPerfect,driverMgFail," +
            "tenureSeconds,tenure01,advantage01";

        _writer = new CsvLogWriter(
            TelemetrySessionContext.MakeFilePath("skill"),
            meta,
            header,
            flushEverySeconds
        );

        Debug.Log("[SkillTelemetryLogger] Logging to: " + _writer.FilePath);
    }

    private void LogFrame()
    {
        var est = SkillEstimator.Instance;
        var dir = DifficultyDirector.Instance;
        var pm = PlayerManager.Instance;

        if (est == null || dir == null)
            return;

        DifficultyState global = dir.GlobalCurrent;
        var globalProf = est.Global;

        DifficultyState current = dir.Current;
        var activeProf = est.Active;
        var activePassenger = est.ActivePassenger;

        int currentDriverPlayerNum = activePassenger ? activePassenger.PlayerNumber : -1;

        int p1Points = 0;
        int p2Points = 0;
        int p3Points = 0;
        int p4Points = 0;

        FillAllPlayerPoints(pm, ref p1Points, ref p2Points, ref p3Points, ref p4Points);

        float gSkill = (globalProf != null) ? globalProf.Skill01 : 0.5f;
        float gConf = (globalProf != null) ? globalProf.Confidence : 0f;

        float aSkill = (activeProf != null) ? activeProf.Skill01 : 0.5f;
        float aConf = (activeProf != null) ? activeProf.Confidence : 0f;

        float aColSucc = (activeProf != null) ? activeProf.CollectSuccessRate : 0f;
        float aColMiss = (activeProf != null) ? activeProf.CollectMissRate : 0f;
        float aHitRate = (activeProf != null) ? activeProf.ObstacleHitRate : 0f;

        float aMgPass = (activeProf != null) ? activeProf.MiniGamePassRate : 0f;
        float aMgPerf = (activeProf != null) ? activeProf.MiniGamePerfectRate : 0f;
        float aMgFail = (activeProf != null) ? activeProf.MiniGameFailRate : 0f;

        float dTenureSec = dir.currentDriverTenureSeconds;
        float dTenure01 = dir.currentDriverTenure01;
        float dAdv01 = dir.currentDriverAdvantage01;

        float t = TelemetrySessionContext.TimeSinceStart();

        _writer.AppendRow(
            t.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            TelemetrySessionContext.SessionId,
            currentDriverPlayerNum.ToString(),

            p1Points.ToString(),
            p2Points.ToString(),
            p3Points.ToString(),
            p4Points.ToString(),

            gSkill.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            gConf.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            global.overall.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            global.spawnPressure.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            global.precision.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            global.punishment.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            global.rewardBias.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),

            aSkill.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            aConf.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            current.overall.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            current.spawnPressure.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            current.precision.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            current.punishment.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            current.rewardBias.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),

            aColSucc.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            aColMiss.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            aHitRate.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),

            aMgPass.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            aMgPerf.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            aMgFail.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),

            dTenureSec.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            dTenure01.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            dAdv01.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
        );
    }

    private void FillAllPlayerPoints(
        PlayerManager pm,
        ref int p1Points,
        ref int p2Points,
        ref int p3Points,
        ref int p4Points)
    {
        if (pm == null) return;

        if (pm.CurrentDriver != null)
            AssignPointsByPlayerNumber(pm.CurrentDriver.PlayerNumber, pm.CurrentDriver.Points,
                ref p1Points, ref p2Points, ref p3Points, ref p4Points);

        if (pm.Passengers != null)
        {
            for (int i = 0; i < pm.Passengers.Count; i++)
            {
                var p = pm.Passengers[i];
                if (p == null) continue;

                AssignPointsByPlayerNumber(p.PlayerNumber, p.Points,
                    ref p1Points, ref p2Points, ref p3Points, ref p4Points);
            }
        }
    }

    private void AssignPointsByPlayerNumber(
        int playerNumber,
        int points,
        ref int p1Points,
        ref int p2Points,
        ref int p3Points,
        ref int p4Points)
    {
        switch (playerNumber)
        {
            case 1: p1Points = points; break;
            case 2: p2Points = points; break;
            case 3: p3Points = points; break;
            case 4: p4Points = points; break;
        }
    }

    private int GuessPlayerCount()
    {
        var pm = PlayerManager.Instance;
        if (pm == null) return -1;

        int count = 0;
        if (pm.CurrentDriver != null) count++;

        if (pm.Passengers != null)
            count += pm.Passengers.Count;

        return count > 0 ? count : -1;
    }
}