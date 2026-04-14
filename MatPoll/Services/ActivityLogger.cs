using Serilog;

namespace MatPoll.Services;

// ActivityLogger writes structured log entries for every important server event.
// All logs go to the Serilog pipeline → console + rolling file.
//
// Each log entry captures:
//   TypeMID     — which device
//   ReqTime     — when request arrived
//   ResTime     — when response was sent
//   Duration    — ms taken
//   Action      — what happened (LOGIN, POLL, ACK, RESTORE, REFRESH, ERROR)
//   Detail      — extra info (rows sent, error message, etc.)

public class ActivityLogger
{
    public void LogLogin(string typeMid, decimal deviceId, string deviceName,
        bool success, string detail, long durationMs)
    {
        if (success)
            Log.Information(
                "[LOGIN] TypeMID:{TypeMID} DeviceID:{DeviceID} Name:{Name} " +
                "Result:SUCCESS Duration:{Dur}ms",
                typeMid, deviceId, deviceName, durationMs);
        else
            Log.Warning(
                "[LOGIN] TypeMID:{TypeMID} DeviceID:{DeviceID} " +
                "Result:FAILED Reason:{Detail} Duration:{Dur}ms",
                typeMid, deviceId, detail, durationMs);
    }

    public void LogPoll(string typeMid, decimal deviceId,
        bool hasData, bool needAckFirst, int rowsSent, int totalPending,
        DateTime reqTime, long durationMs)
    {
        if (needAckFirst)
            Log.Warning(
                "[POLL ] TypeMID:{TypeMID} DeviceID:{DeviceID} " +
                "Result:NEED_ACK_FIRST TrnStat1RowsExist:true " +
                "ReqTime:{ReqTime} Duration:{Dur}ms",
                typeMid, deviceId,
                reqTime.ToString("HH:mm:ss.fff"), durationMs);
        else if (hasData)
            Log.Information(
                "[POLL ] TypeMID:{TypeMID} DeviceID:{DeviceID} " +
                "Result:DATA_SENT RowsSent:{Rows} TotalPending:{Pending} " +
                "ReqTime:{ReqTime} Duration:{Dur}ms",
                typeMid, deviceId, rowsSent, totalPending,
                reqTime.ToString("HH:mm:ss.fff"), durationMs);
        else
            Log.Information(
                "[POLL ] TypeMID:{TypeMID} DeviceID:{DeviceID} " +
                "Result:NO_DATA TotalPending:{Pending} " +
                "ReqTime:{ReqTime} Duration:{Dur}ms",
                typeMid, deviceId, totalPending,
                reqTime.ToString("HH:mm:ss.fff"), durationMs);
    }

    public void LogAck(string typeMid, decimal deviceId,
        List<decimal> trnIds, int updatedCount, bool success,
        DateTime reqTime, long durationMs)
    {
        if (success)
            Log.Information(
                "[ACK  ] TypeMID:{TypeMID} DeviceID:{DeviceID} " +
                "TrnIDsSent:{Count} TrnIDsUpdated:{Updated} " +
                "TrnIDs:[{IDs}] " +
                "ReqTime:{ReqTime} Duration:{Dur}ms",
                typeMid, deviceId,
                trnIds.Count, updatedCount,
                string.Join(",", trnIds),
                reqTime.ToString("HH:mm:ss.fff"), durationMs);
        else
            Log.Warning(
                "[ACK  ] TypeMID:{TypeMID} DeviceID:{DeviceID} " +
                "Result:FAILED ReqTime:{ReqTime} Duration:{Dur}ms",
                typeMid, deviceId,
                reqTime.ToString("HH:mm:ss.fff"), durationMs);
    }

    public void LogRestore(string typeMid, decimal deviceId,
        int restoredCount, DateTime reqTime, long durationMs)
    {
        Log.Warning(
            "[REST ] TypeMID:{TypeMID} DeviceID:{DeviceID} " +
            "RestoredRows:{Count} ReqTime:{ReqTime} Duration:{Dur}ms",
            typeMid, deviceId, restoredCount,
            reqTime.ToString("HH:mm:ss.fff"), durationMs);
    }

    public void LogRefresh(string typeMid, decimal deviceId,
        bool success, long durationMs)
    {
        if (success)
            Log.Information(
                "[RFSH ] TypeMID:{TypeMID} DeviceID:{DeviceID} " +
                "Result:SUCCESS Duration:{Dur}ms",
                typeMid, deviceId, durationMs);
        else
            Log.Warning(
                "[RFSH ] TypeMID:{TypeMID} DeviceID:{DeviceID} " +
                "Result:FAILED Duration:{Dur}ms",
                typeMid, deviceId, durationMs);
    }

    public void LogError(string action, string typeMid,
        decimal deviceId, string error)
    {
        Log.Error(
            "[ERROR] Action:{Action} TypeMID:{TypeMID} DeviceID:{DeviceID} " +
            "Error:{Error}",
            action, typeMid, deviceId, error);
    }
}
