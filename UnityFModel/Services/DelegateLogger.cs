using System;
using AssetRipper.Import.Logging;

namespace UnityFModel.Services;

/// <summary>Forwards AssetRipper log messages to a delegate so the UI can show progress.</summary>
public sealed class DelegateLogger : ILogger
{
    private readonly Action<LogType, LogCategory, string> _onLog;

    public DelegateLogger(Action<LogType, LogCategory, string> onLog) => _onLog = onLog;

    public void Log(LogType type, LogCategory category, string message) => _onLog(type, category, message);

    public void BlankLine(int numLines)
    {
        for (int i = 0; i < numLines; i++)
            _onLog(LogType.Info, LogCategory.General, string.Empty);
    }
}
