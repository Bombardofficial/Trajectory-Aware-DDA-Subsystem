using UnityEngine;

public class DriverSwapTelemetryLogger : MonoBehaviour
{
    private void OnEnable()
    {
        PlayerManager.OnDriverChanged += HandleDriverChanged;
    }

    private void OnDisable()
    {
        PlayerManager.OnDriverChanged -= HandleDriverChanged;
    }

    private void HandleDriverChanged(Passenger oldDriver, Passenger newDriver)
    {
        if (!TelemetrySessionContext.IsActive) return;
        if (EventTelemetryLogger.Instance == null) return;

        int oldNum = oldDriver ? oldDriver.PlayerNumber : -1;
        int newNum = newDriver ? newDriver.PlayerNumber : -1;
        int newPoints = newDriver ? newDriver.Points : 0;

        string newDriverId = newDriver ? newDriver.name : "unknown_driver";
        string note = $"old={oldNum};new={newNum}";

        EventTelemetryLogger.Log(
            "driver_swap",
            newDriverId,
            0f,
            -1,
            0f,
            oldNum,
            newNum,
            newPoints,
            note
        );
    }
}