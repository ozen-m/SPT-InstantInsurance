using InstantInsurance.Configuration;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;

namespace InstantInsurance.Utils;

public static class LoggerUtil
{
    private const string LogPrefix = "[Instant Insurance] ";

    public static ISptLogger<InstantInsurance> Logger { get; set; }
    public static ItemHelper ItemHelper { get; set; }
    private static ModConfig ModConfig => InstantInsurance.ModConfig;

    public static void Debug(string message)
    {
        if (ModConfig.DebugLogs)
        {
            Logger?.Debug(LogPrefix + message);
        }
    }

    public static void Success(string message)
    {
        Logger?.Success(LogPrefix + message);
    }

    public static void Info(string message)
    {
        Logger?.Info(LogPrefix + message);
    }

    public static void Warning(string message)
    {
        Logger?.Warning(LogPrefix + message);
    }

    public static void Error(string message)
    {
        Logger?.Error(LogPrefix + message);
    }

    public static string Name(this Item item)
    {
        return ItemHelper?.GetItemName(item.Template);
    }

    public static string ListIdsAndNames(this IEnumerable<Item> items)
    {
        var names = items.Select(i => i.Name());
        var ids = items.Select(i => i.Id);

        return string.Join(", ", names.Zip(ids, (name, id) => $"{name} {id}"));
    }

    public static string ListIdsAndNames(this IEnumerable<MongoId> itemIds, IEnumerable<Item> itemMap)
    {
        var itemMapIds = itemMap.Select(i => i.Id);
        var names = itemMap.Where(item => itemMapIds.Contains(item.Id)).Select(i => i.Name());

        return string.Join(", ", names.Zip(itemIds, (name, id) => $"{name} {id}"));
    }

    public static string ListNames(this IEnumerable<Item> items)
    {
        return string.Join(", ", items.Select(i => i.Name()));
    }

    public static string ListIds(this IEnumerable<Item> items)
    {
        return items.Select(i => i.Id).ListIds();
    }

    public static string ListIds(this IEnumerable<MongoId> ids)
    {
        return string.Join(", ", ids);
    }

    public static string ListTpls(this IEnumerable<Item> items)
    {
        return string.Join(", ", items.Select(i => i.Template));
    }
}
