using System;
using UnityEngine;

public class EventTelemetryLogger : MonoBehaviour
{
    public static EventTelemetryLogger Instance { get; private set; }

    [Header("Enable/Disable")]
    public bool enableLogging = true;

    [Header("Flush")]
    public float flushEverySeconds = 1.0f;

    private CsvLogWriter _writer;
    private string _activeSessionId = "";
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        if (!enableLogging) return;

        // If the session has changed or ended, dispose the old writer
        if (_writer != null && (!TelemetrySessionContext.IsActive || TelemetrySessionContext.SessionId != _activeSessionId))
        {
            _writer.Dispose();
            _writer = null;
        }

        if (_writer != null) return;
        if (!PlayerJoinManager.IsRaceStarted) return;

        if (!TelemetrySessionContext.IsActive)
        {
            int pc = GuessPlayerCount();
            TelemetrySessionContext.StartNew(pc);
        }

        _activeSessionId = TelemetrySessionContext.SessionId;

        string[] meta =
        {
            $"# sessionId={TelemetrySessionContext.SessionId}",
            $"# playerCount={TelemetrySessionContext.PlayerCount}",
            $"# startedUtc={TelemetrySessionContext.StartedUtcIso}"
        };

        string header = "t,sessionId,driverId,eventType,trackT,lane,speed,v1,v2,v3,note";
        _writer = new CsvLogWriter(
            TelemetrySessionContext.MakeFilePath("events"),
            meta,
            header,
            flushEverySeconds
        );

        Debug.Log("[EventTelemetryLogger] Logging to: " + _writer.FilePath);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        _writer?.Dispose();
    }

    public static void Log(
        string eventType,
        string driverId,
        float trackT,
        int lane,
        float speed,
        float v1 = 0f,
        float v2 = 0f,
        float v3 = 0f,
        string note = "")
    {
        if (Instance == null || !Instance.enableLogging) return;
        if (Instance._writer == null) return;

        float t = TelemetrySessionContext.TimeSinceStart();

        Instance._writer.AppendRow(
            t.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            TelemetrySessionContext.SessionId,
            driverId ?? "",
            eventType ?? "",
            trackT.ToString("F5", System.Globalization.CultureInfo.InvariantCulture),
            lane.ToString(),
            speed.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            v1.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            v2.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            v3.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            note ?? ""
        );
    }

    private int GuessPlayerCount()
    {
        if (PlayerManager.Instance != null && PlayerManager.Instance.Passengers != null)
            return PlayerManager.Instance.Passengers.Count;

        return -1;
    }
}