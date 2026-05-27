using System;
using System.IO;
using UnityEngine;

public static class TelemetrySessionContext
{
    public static string SessionId { get; private set; } = "";
    public static int PlayerCount { get; private set; } = -1;

    public static float StartRealtime { get; private set; } = -1f;
    public static string StartedUtcIso { get; private set; } = "";

    private const string ExportFolderName = "DriveMeCrazy_TelemetryLogs_To_Send";

    public static string BaseFolder
    {
        get
        {
            string downloads = GetDownloadsFolderPathSafe();
            string finalPath = Path.Combine(downloads, ExportFolderName);
            Directory.CreateDirectory(finalPath);
            return finalPath;
        }
    }

    public static bool IsActive => !string.IsNullOrEmpty(SessionId);

    public static void StartNew(int playerCount)
    {
        PlayerCount = playerCount;

        SessionId = Guid.NewGuid().ToString("N");
        StartRealtime = Time.realtimeSinceStartup;
        StartedUtcIso = DateTime.UtcNow.ToString("O");

        Directory.CreateDirectory(BaseFolder);

        Debug.Log("[TelemetrySessionContext] Session started.");
        Debug.Log("[TelemetrySessionContext] SessionId: " + SessionId);
        Debug.Log("[TelemetrySessionContext] BaseFolder: " + BaseFolder);
    }
    public static void EndSession()
    {
        SessionId = "";
        PlayerCount = -1;
        StartRealtime = -1f;
        Debug.Log("[TelemetrySessionContext] Session reset for a new match.");
    }
    public static float TimeSinceStart()
    {
        if (StartRealtime < 0f) return 0f;
        return Mathf.Max(0f, Time.realtimeSinceStartup - StartRealtime);
    }

    public static string MakeFilePath(string fileTag)
    {
        string safeTag = string.IsNullOrWhiteSpace(fileTag) ? "log" : fileTag;
        string name = $"session_{SessionId}_{safeTag}.csv";
        return Path.Combine(BaseFolder, name);
    }

    private static string GetDownloadsFolderPathSafe()
    {
        try
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                string downloads = Path.Combine(userProfile, "Downloads");
                if (Directory.Exists(downloads) || EnsureDirectory(downloads))
                    return downloads;
            }

            string home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrWhiteSpace(home))
            {
                string downloads = Path.Combine(home, "Downloads");
                if (Directory.Exists(downloads) || EnsureDirectory(downloads))
                    return downloads;
            }

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrWhiteSpace(desktop))
                return desktop;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TelemetrySessionContext] Failed to resolve Downloads folder: " + e.Message);
        }

        return Application.persistentDataPath;
    }

    private static bool EnsureDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}