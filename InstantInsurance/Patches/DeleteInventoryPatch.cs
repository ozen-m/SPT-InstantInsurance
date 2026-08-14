using System.Collections.Frozen;
using System.Reflection;
using HarmonyLib;
using InstantInsurance.Configuration;
using InstantInsurance.Utils;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.InRaid;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;
using Insurance = SPTarkov.Server.Core.Models.Eft.Profile.Insurance;

namespace InstantInsurance.Patches;

[Injectable]
public class DeleteInventoryPatch : AbstractPatch
{
    private static InstantInsuranceLogger L = null!;
    private static InstantInsuranceConfig _config = null!;

    private static InsuranceConfig _insuranceConfig = null!;
    private static InsuranceController _insuranceController = null!;
    private static ItemHelper _itemHelper = null!;
    private static TraderHelper _traderHelper = null!;
    private static TimeUtil _timeUtil = null!;
    private static TradersTable _tradersTable = null!;
    private static LocationTable _locationTable = null!;
    private static RandomUtil _randomUtil = null!;

    public static string MapId { get; set; } = string.Empty;

    public DeleteInventoryPatch(
        InstantInsuranceLogger logger,
        InstantInsuranceConfig modConfig,
        InsuranceConfig insuranceConfig,
        InsuranceController insuranceController,
        ItemHelper itemHelper,
        TraderHelper traderHelper,
        TimeUtil timeUtil,
        TradersTable tradersTable,
        LocationTable locationTable,
        RandomUtil randomUtil
    )
    {
        L = logger;
        _config = modConfig;
        _insuranceConfig = insuranceConfig;
        _insuranceController = insuranceController;
        _itemHelper = itemHelper;
        _traderHelper = traderHelper;
        _timeUtil = timeUtil;
        _tradersTable = tradersTable;
        _locationTable = locationTable;
        _randomUtil = randomUtil;
    }

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(InRaidHelper), nameof(InRaidHelper.DeleteInventory));
    }

    [PatchPrefix]
    public static bool Prefix(InRaidHelper __instance, PmcData pmcData, MongoId sessionId)
    {
        if (pmcData.Inventory is null)
        {
            L.Error("PmcData.Inventory is null when trying to process inventory, falling back to SPT");
            return true;
        }
        if (pmcData.Inventory.Items is null)
        {
            L.Error("PmcData.Inventory.Items is null when trying to process inventory, falling back to SPT");
            return true;
        }

        Dictionary<MongoId, Insurance> insuranceTraders = [];
        HashSet<Item> itemsProcessed = [];
        HashSet<Item> ammoToKeep = [];
        HashSet<MongoId> itemsToUninsure = [];
        HashSet<MongoId> itemsToDelete = [];
        var itemsKeptByInsurance = 0;
        var itemsSentByMail = 0;

        // Get inventory item ids to remove from players profile
        var itemsLostOnDeath = __instance.GetInventoryItemsLostOnDeath(pmcData);
        foreach (var item in itemsLostOnDeath)
        {
            var itemAndChildrenLostOnDeath = pmcData.Inventory.Items.GetItemWithChildren(item.Id);
            foreach (var child in itemAndChildrenLostOnDeath)
            {
                itemsProcessed.Add(child);
                var insuredItem = GetInsuredItem(pmcData, child.Id);
                if (insuredItem is not null)
                {
                    if (!insuranceTraders.TryGetValue(insuredItem.TId, out _))
                    {
                        // Create new insurance package for trader
                        insuranceTraders[insuredItem.TId] = new Insurance { TraderId = insuredItem.TId, Items = [], };
                    }

                    insuranceTraders[insuredItem.TId].Items!.Add(child);
                    if (_config.LoseInsuranceOnItemAfterDeath)
                    {
                        itemsToUninsure.Add(child.Id);
                    }

                    continue;
                }

                if (ShouldKeepAmmo(child))
                {
                    ammoToKeep.Add(child);
                }

                itemsToDelete.Add(child.Id);
            }
        }

        var equipmentId = pmcData.Inventory.Equipment.ToString();
        if (equipmentId is null)
        {
            L.Error("PmcData.Inventory.Equipment is null when trying to process inventory, falling back to SPT");
            return true;
        }

        // Get all items to delete before processing insurance package to be sent, which will check if an item's parent will be deleted
        if (_config.SimulateItemsBeingTaken)
        {
            foreach (var (_, insurance) in insuranceTraders)
            {
                // Find items that could be taken by another player off the players body, using SPT's method
                var foundItemsToDelete = _insuranceController.FindItemsToDelete(equipmentId, insurance);
                itemsToDelete.UnionWith(foundItemsToDelete);
            }
        }

        var itemsMap = itemsProcessed.GenerateItemsMap();
        foreach (var (_, insurance) in insuranceTraders)
        {
            // Remove items from the insured items that should not be returned to the player
            insurance.Items = [.. insurance.Items!.Where(i => !itemsToDelete.Contains(i.Id))];

            // Get ammo from magazines still existing, add it to the insurance package
            var itemIds = insurance.Items.Select(i => i.Id).ToHashSet();
            var ammoWithParents = ammoToKeep.Where(a => itemIds.Contains(a.ParentId ?? MongoId.Empty())).ToArray();

            // Add it to insurance and keep ammo items from being deleted
            insurance.Items.AddRange(ammoWithParents);
            itemsToDelete.ExceptWith(ammoWithParents.Select(a => a.Id));

            itemsKeptByInsurance += insurance.Items.Count;

            // Let the mail handle insurance message for disabled maps, so it is apparent that insurance is disabled on that map.
            // Else, replace insurance package with items (whose parents are deleted) to be sent by mail.
            if (IsLocationInsuranceDisabled(MapId))
            {
                itemsToDelete.UnionWith(insurance.Items.Select(i => i.Id));
            }
            else
            {
                insurance.Items =
                [
                    .. insurance.Items.Where(item =>
                        {
                            // Get item parents, then check if any will be removed, if so send the item by mail
                            var parentsIds = GetItemParentsIds(item.Id, itemsMap);
                            return itemsToDelete.Any(parentsIds.Contains);
                        }
                    ),
                ];
            }

            itemsSentByMail += SendItemsByMail(insurance, sessionId);
        }

        // Remove itemsToDelete from inventory
        pmcData.Inventory.Items = [.. pmcData.Inventory.Items.Where(i => !itemsToDelete.Contains(i.Id))];

        // Remove items from insurance
        if (_config.LoseInsuranceOnItemAfterDeath && pmcData.InsuredItems is not null)
        {
            pmcData.InsuredItems =
                [.. pmcData.InsuredItems.Where(insuredItem => !itemsToUninsure.Contains(insuredItem.ItemId.GetValueOrDefault()))];
        }

        L.Info("--------");
        L.Info($"Player: {pmcData.Info!.Nickname}"); // Fika
        L.Info($"Map: {MapId}");
        L.Info($"Items processed: {itemsProcessed.Count}");
        L.Info($"Items kept: {itemsKeptByInsurance}");
        L.Info($"Items removed: {itemsToDelete.Count}");
        L.Info($"Items uninsured: {itemsToUninsure.Count}");
        L.Info($"Items sent by mail: {itemsSentByMail}");
        L.Info("--------");

        // Remove contents of fast panel
        pmcData.Inventory.FastPanel = [];

        return false;
    }

    private static int SendItemsByMail(Insurance insurance, MongoId sessionId)
    {
        if (insurance.Items is null || insurance.Items.Count == 0)
        {
            return 0;
        }

        // Create a new root parent ID for the message we'll be sending the player
        var mailRootItemParentId = new MongoId();

        var traderBase = _traderHelper.GetTrader(insurance.TraderId, sessionId);
        var maxInsuranceStorageTime = _insuranceConfig.StorageTimeOverrideSeconds > 0
            ? _insuranceConfig.StorageTimeOverrideSeconds
            : _timeUtil.GetHoursAsSeconds((int)traderBase!.Insurance!.MaxStorageTime!);
        var systemData = new SystemData
        {
            Date = _timeUtil.GetBsgDateMailFormat(), Time = _timeUtil.GetBsgTimeMailFormat(), Location = MapId,
        };
        var dialogueTemplates = _tradersTable.GetTrader(insurance.TraderId)!.Dialogue;

        insurance.MaxStorageTime = (int)maxInsuranceStorageTime;
        insurance.SystemData = systemData;
        insurance.MessageType = MessageType.InsuranceReturn;
        insurance.MessageTemplateId = _randomUtil.GetArrayValue(dialogueTemplates["insuranceFound"]!);
        insurance.Items = insurance.Items.AdoptOrphanedItems(mailRootItemParentId);

        _insuranceController.SendMail(sessionId, insurance);
        return insurance.Items.Count;
    }

    private static bool ShouldKeepAmmo(Item item)
    {
        return !_config.LoseAmmoInMagazines
               && _itemHelper.IsOfBaseclass(item.Template, BaseClasses.AMMO)
               && MagazineSlotIds.Contains(item.SlotId ?? string.Empty);
    }

    /// <summary>
    /// Check if insurance is allowed for the current map.
    /// Fallbacks to <c>false</c> if the map's insurance setting is not found
    /// </summary>
    public static bool IsLocationInsuranceDisabled(string mapId)
    {
        return !(_locationTable.GetLocation(mapId)?.Base.Insurance ?? true);
    }

    /// <summary>
    /// Return the player's insured item from <seealso cref="BotBase.InsuredItems"/>
    /// </summary>
    /// <param name="data">PmcData</param>
    /// <param name="lostItemId">Insured item id to look for</param>
    /// <returns>The insured item</returns>
    public static InsuredItem? GetInsuredItem(BotBase data, MongoId lostItemId)
    {
        return data.InsuredItems?.FirstOrDefault(insuredItem => insuredItem.ItemId == lostItemId);
    }

    /// <summary>
    /// Modified <seealso cref="ItemHelper.GetEquipmentParent"/> to return a list of parents<br></br><br></br>
    /// 
    /// Retrieves the equipment parent item for a given item.<br></br><br></br>
    ///
    /// This method traverses up the hierarchy of items starting from a given `itemId`, until it finds the equipment
    /// parent item. In other words, if you pass it an item id of a suppressor, it will traverse up the muzzle brake,
    /// barrel, upper receiver, gun, nested backpack, and finally return the backpack Item that is equipped.<br></br><br></br>
    ///
    /// It's important to note that traversal is expensive, so this method requires that you pass it a Dictionary of the items
    /// to traverse, where the keys are the item IDs and the values are the corresponding Item objects. This alleviates
    /// some of the performance concerns, as it allows for quick lookups of items by ID.
    /// </summary>
    /// <param name="itemId">The unique identifier of the item for which to find the equipment parent.</param>
    /// <param name="itemsMap">A Dictionary containing item IDs mapped to their corresponding Item objects for quick lookup.</param>
    /// <returns>A list of parents item ids</returns>
    public static HashSet<MongoId> GetItemParentsIds(MongoId itemId, Dictionary<MongoId, Item> itemsMap)
    {
        var parentResults = new HashSet<MongoId>();
        var currentItem = itemsMap.GetValueOrDefault(itemId);

        while (currentItem is not null && !EquipmentSlotsAsStrings.Contains(currentItem.SlotId ?? string.Empty))
        {
            currentItem = itemsMap.GetValueOrDefault(currentItem.ParentId ?? string.Empty);
            if (currentItem is null)
            {
                break;
            }

            parentResults.Add(currentItem.Id);
        }

        return parentResults;
    }

    public static readonly FrozenSet<string> MagazineSlotIds =
        ["cartridges", "patron_in_weapon", "patron_in_weapon_000", "patron_in_weapon_001"];

    public static readonly FrozenSet<string> EquipmentSlotsAsStrings =
    [
        nameof(EquipmentSlots.Headwear),
        nameof(EquipmentSlots.Earpiece),
        nameof(EquipmentSlots.FaceCover),
        nameof(EquipmentSlots.ArmorVest),
        nameof(EquipmentSlots.Eyewear),
        nameof(EquipmentSlots.ArmBand),
        nameof(EquipmentSlots.TacticalVest),
        nameof(EquipmentSlots.Pockets),
        nameof(EquipmentSlots.Backpack),
        nameof(EquipmentSlots.SecuredContainer),
        nameof(EquipmentSlots.FirstPrimaryWeapon),
        nameof(EquipmentSlots.SecondPrimaryWeapon),
        nameof(EquipmentSlots.Holster),
        nameof(EquipmentSlots.Scabbard),
    ];
}
