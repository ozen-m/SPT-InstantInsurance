using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace InstantInsurance.Utils;

public static class CommonExtensions
{
    private static ItemHelper _itemHelper = null!;

    public static void SetItemHelper(ItemHelper itemHelper)
    {
        _itemHelper = itemHelper;
    }

    public static string Name(this Item item)
    {
        return _itemHelper.GetItemName(item.Template);
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
