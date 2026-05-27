using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class CsvLogWriter : IDisposable
{
    private readonly StreamWriter _sw;
    private readonly StringBuilder _sb = new StringBuilder(512);
    private readonly CultureInfo _inv = CultureInfo.InvariantCulture;

    private float _nextFlushTime = 0f;
    private readonly float _flushEverySeconds;

    public string FilePath { get; }

    public CsvLogWriter(string filePath, string[] metaLines, string headerLine, float flushEverySeconds = 1.0f)
    {
        FilePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? Application.persistentDataPath);

        _sw = new StreamWriter(filePath, append: false, encoding: Encoding.UTF8);
        _sw.NewLine = "\n";

        if (metaLines != null)
        {
            for (int i = 0; i < metaLines.Length; i++)
                _sw.WriteLine(metaLines[i]);
        }

        if (!string.IsNullOrWhiteSpace(headerLine))
            _sw.WriteLine(headerLine);

        _flushEverySeconds = Mathf.Max(0.1f, flushEverySeconds);
        _nextFlushTime = Time.realtimeSinceStartup + _flushEverySeconds;
    }

    public void AppendLineRaw(string line)
    {
        _sw.WriteLine(line);
        AutoFlushIfNeeded();
    }

    public void AppendRow(params string[] cols)
    {
        _sb.Clear();
        for (int i = 0; i < cols.Length; i++)
        {
            if (i > 0) _sb.Append(',');
            _sb.Append(cols[i] ?? "");
        }
        _sw.WriteLine(_sb.ToString());
        AutoFlushIfNeeded();
    }

    public void AppendRow(
        float t, string sessionId, string driverId, float trackT, int lane, float speed,
        float speedLimit, float overshootRatio,
        int isDrifting, int isGrounded,
        float cornerAngle, float curvature,
        float spawnPressure, float precision, float punishment, float rewardBias,
        float cornerSmooth01, float laneJitter01, float overspeedControl01
    )
    {
        _sb.Clear();
        _sb.Append(t.ToString("F3", _inv)).Append(',');
        _sb.Append(sessionId).Append(',');
        _sb.Append(Escape(driverId)).Append(',');
        _sb.Append(trackT.ToString("F5", _inv)).Append(',');
        _sb.Append(lane).Append(',');
        _sb.Append(speed.ToString("F3", _inv)).Append(',');
        _sb.Append(speedLimit.ToString("F3", _inv)).Append(',');
        _sb.Append(overshootRatio.ToString("F4", _inv)).Append(',');
        _sb.Append(isDrifting).Append(',');
        _sb.Append(isGrounded).Append(',');
        _sb.Append(cornerAngle.ToString("F2", _inv)).Append(',');
        _sb.Append(curvature.ToString("F5", _inv)).Append(',');
        _sb.Append(spawnPressure.ToString("F3", _inv)).Append(',');
        _sb.Append(precision.ToString("F3", _inv)).Append(',');
        _sb.Append(punishment.ToString("F3", _inv)).Append(',');
        _sb.Append(rewardBias.ToString("F3", _inv)).Append(',');
        _sb.Append(cornerSmooth01.ToString("F3", _inv)).Append(',');
        _sb.Append(laneJitter01.ToString("F3", _inv)).Append(',');
        _sb.Append(overspeedControl01.ToString("F3", _inv));

        _sw.WriteLine(_sb.ToString());
        AutoFlushIfNeeded();
    }

    public void Flush() => _sw.Flush();

    private void AutoFlushIfNeeded()
    {
        if (Time.realtimeSinceStartup >= _nextFlushTime)
        {
            _sw.Flush();
            _nextFlushTime = Time.realtimeSinceStartup + _flushEverySeconds;
        }
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // Minimal CSV escaping
        if (s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r"))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    public void Dispose()
    {
        try { _sw.Flush(); } catch { /* ignore */ }
        try { _sw.Dispose(); } catch { /* ignore */ }
    }
}
