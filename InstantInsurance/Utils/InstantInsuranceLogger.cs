using InstantInsurance.Configuration;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;

namespace InstantInsurance.Utils;

[Injectable(InjectionType.Singleton)]
public class InstantInsuranceLogger(ISptLogger<InstantInsurance> logger, InstantInsuranceConfig config)
{
    private const string LogPrefix = $"[{nameof(InstantInsurance)}] ";

    public void Debug(object message)
    {
        if (config.DebugLogs)
        {
            logger.Debug(LogPrefix + message);
        }
    }

    public void Success(object message)
    {
        logger.Success(LogPrefix + message);
    }

    public void Info(object message)
    {
        logger.Info(LogPrefix + message);
    }

    public void Warning(object message)
    {
        logger.Warning(LogPrefix + message);
    }

    public void Error(object message)
    {
        logger.Error(LogPrefix + message);
    }
}
