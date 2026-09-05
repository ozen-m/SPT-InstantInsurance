using InstantInsurance.Configuration;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Utils.Collections;

namespace InstantInsurance.Utils;

/// <summary>
///     Modified <see cref="InsuranceController"/>
///       - Insured items to delete is processed from a list of items instead of per insurance package.
///       - MongoId traderId params are replaced with Dictionary (K:MongoId, V:MongoId) tradersMap.
/// </summary>
[Injectable]
public class TakeItemsHelper(
    InstantInsuranceLogger L,
    InstantInsuranceConfig config,
    RandomUtil randomUtil,
    ItemHelper itemHelper,
    ServerLocalisationService serverLocalisationService,
    InsuranceConfig insuranceConfig,
    InsuranceController insuranceController,
    ICloner cloner
)
{
    /// <summary>
    ///     Finds items that should be deleted based on the given list of Insured Items
    /// </summary>
    /// <param name="insuredItems">The list of insured items to evaluate for deletion</param>
    /// <param name="tradersMap">Trader ID each item is insured by (Key: Item ID, Value: Trader ID)</param>
    /// <returns>A set containing the IDs of items that should be deleted</returns>
    public HashSet<MongoId> FindItemsToDelete(List<Item> insuredItems, Dictionary<MongoId, MongoId> tradersMap)
    {
        if (insuredItems.Count != tradersMap.Count)
        {
            L.Warning("Insured items count does not match traders map count!");
        }

        var toDelete = new HashSet<MongoId>();
        
        // Populate a Map object of items for quick lookup by their ID and use it to populate a Map of main-parent items
        // and each of their attachments. For example, a gun mapped to each of its attachments.
        var itemsMap = insuredItems.GenerateItemsMap();
        var parentAttachmentsMap = PopulateParentAttachmentsMap(insuredItems, itemsMap);

        // Process all items that are not attached, attachments; those are handled separately, by value.
        ProcessRegularItems(insuredItems, tradersMap, toDelete, parentAttachmentsMap);

        // Process attached, attachments, by value, only if there are any.
        if (parentAttachmentsMap.Count > 0)
        {
            // Remove attachments that can not be moddable in-raid from the parentAttachmentsMap. We only want to
            // process moddable attachments from here on out.
            parentAttachmentsMap = insuranceController.RemoveNonModdableAttachments(parentAttachmentsMap, itemsMap);

            ProcessAttachments(parentAttachmentsMap, tradersMap, itemsMap, toDelete);
        }

        // Log the number of items marked for deletion, if any
        if (config.DebugLogs)
        {
            if (toDelete.Count != 0)
            {
                L.Debug($"Marked {toDelete.Count} items for deletion from insurance.");
            }
        }

        return toDelete;
    }

    /// <summary>
    ///     Initialize a dictionary that holds main-parents to all of their attachments. Note that "main-parent" in this
    ///     context refers to the parent item that an attachment is attached to. For example, a suppressor attached to a gun,
    ///     not the backpack that the gun is located in (the gun's parent).
    /// </summary>
    /// <param name="insuredItems">The list of insured items to evaluate for deletion</param>
    /// <param name="itemsMap">A Dictionary for quick item look-up by item ID</param>
    /// <returns>A dictionary containing parent item IDs to arrays of their attachment items</returns>
    protected Dictionary<MongoId, List<Item>> PopulateParentAttachmentsMap(List<Item> insuredItems, Dictionary<MongoId, Item> itemsMap)
    {
        var mainParentToAttachmentsMap = new Dictionary<MongoId, List<Item>>();
        foreach (var insuredItem in insuredItems)
        {
            // Use the parent ID from the item to get the parent item.
            var parentItem = insuredItems.FirstOrDefault(item => item.Id == (insuredItem.ParentId ?? string.Empty));

            // Is a root item, skip.
            if (parentItem is null)
            {
                continue;
            }

            // Not attached to parent, skip
            if (!itemHelper.IsAttachmentAttached(insuredItem))
            {
                continue;
            }

            // Make sure the template for the item exists.
            if (!itemHelper.GetItem(insuredItem.Template).Key)
            {
                L.Warning(
                    serverLocalisationService.GetText(
                        "insurance-unable_to_find_attachment_in_db",
                        new { insuredItemId = insuredItem.Id, insuredItemTpl = insuredItem.Template }
                    )
                );

                continue;
            }

            // Get the main parent of this attachment. (e.g., The gun that this suppressor is attached to.)
            var mainParent = itemHelper.GetAttachmentMainParent(insuredItem.Id, itemsMap);
            if (mainParent is null)
            {
                // Odd. The parent couldn't be found. Skip this attachment and warn.
                L.Warning(
                    serverLocalisationService.GetText(
                        "insurance-unable_to_find_main_parent_for_attachment",
                        new
                        {
                            insuredItemId = insuredItem.Id,
                            insuredItemTpl = insuredItem.Template,
                            parentId = insuredItem.ParentId,
                        }
                    )
                );

                continue;
            }

            // Update (or add to) the main-parent to attachments map.
            if (!mainParentToAttachmentsMap.TryGetValue(mainParent.Id, out var attachments))
            {
                attachments = [];
                mainParentToAttachmentsMap[mainParent.Id] = attachments;
            }
            
            attachments.Add(insuredItem);
        }

        return mainParentToAttachmentsMap;
    }

    /// <summary>
    ///     Process "regular" insurance items. Any insured item that is not an attached, attachment is considered a "regular"
    ///     item. This method iterates over them, preforming item deletion rolls to see if they should be deleted. If so,
    ///     they (and their attached, attachments, if any) are marked for deletion in the toDelete Dictionary
    /// </summary>
    /// <param name="insuredItems">The list of insured items to evaluate for deletion</param>
    /// <param name="tradersMap">Trader ID each item is insured by (Key: Item ID, Value: Trader ID)</param>
    /// <param name="toDelete">Hashset to keep track of items marked for deletion</param>
    /// <param name="parentAttachmentsMap">Dictionary containing parent item IDs to arrays of their attachment items</param>
    protected void ProcessRegularItems(
        List<Item> insuredItems,
        Dictionary<MongoId, MongoId> tradersMap,
        HashSet<MongoId> toDelete,
        Dictionary<MongoId, List<Item>> parentAttachmentsMap
    )
    {
        foreach (var insuredItem in insuredItems)
        {
            // Skip if the item is an attachment. These are handled separately.
            if (itemHelper.IsAttachmentAttached(insuredItem))
            {
                continue;
            }

            // Odd, can't find trader id for insured item, add to toDelete
            if (!tradersMap.TryGetValue(insuredItem.Id, out var traderId))
            {
                L.Warning($"Could not find Trader ID for Insured Item = {insuredItem.Id}, Template = {insuredItem.Template}");
                toDelete.Add(insuredItem.Id);
                continue;
            }

            // Roll for item deletion
            var itemRoll = insuranceController.RollForDelete(traderId, insuredItem);
            if (itemRoll ?? false)
            {
                // Check to see if this item is a parent in the parentAttachmentsMap. If so, do a look-up for *all* of
                // its children and mark them for deletion as well. Also remove parent (and its children)
                // from the parentAttachmentsMap so that it's children are not rolled for later in the process.
                if (parentAttachmentsMap.ContainsKey(insuredItem.Id))
                {
                    // This call will also return the parent item itself, queueing it for deletion as well.
                    var itemAndChildren = insuredItems.GetItemWithChildren(insuredItem.Id, true);
                    foreach (var item in itemAndChildren)
                    {
                        toDelete.Add(item.Id);
                    }

                    // Remove the parent (and its children) from the parentAttachmentsMap.
                    parentAttachmentsMap.Remove(insuredItem.Id);
                }
                else
                {
                    // This item doesn't have any children. Simply mark it for deletion.
                    toDelete.Add(insuredItem.Id);
                }
            }
        }
    }

    /// <summary>
    ///     Process parent items and their attachments, updating the toDelete Set accordingly
    /// </summary>
    /// <param name="mainParentToAttachmentsMap">Dictionary containing parent item IDs to arrays of their attachment items</param>
    /// <param name="tradersMap">Trader ID each item is insured by (Key: Item ID, Value: Trader ID)</param>
    /// <param name="itemsMap">Dictionary for quick item look-up by item ID</param>
    /// <param name="toDelete">Tracked attachment ids to be removed</param>
    protected void ProcessAttachments(
        Dictionary<MongoId, List<Item>> mainParentToAttachmentsMap,
        Dictionary<MongoId, MongoId> tradersMap,
        Dictionary<MongoId, Item> itemsMap,
        HashSet<MongoId> toDelete
    )
    {
        foreach (var (key, attachments) in mainParentToAttachmentsMap)
        {
            // Skip processing if parentId is already marked for deletion, as all attachments for that parent will
            // already be marked for deletion as well.
            if (toDelete.Contains(key))
            {
                continue;
            }

            // Log the parent item's name.
            itemsMap.TryGetValue(key, out var parentItem);
            var parentName = itemHelper.GetItemName(parentItem!.Template);
            if (config.DebugLogs)
            {
                L.Debug($"Processing attachments of parent {parentName}");
            }

            // Process the attachments for this individual parent item.
            ProcessAttachmentByParent(attachments, tradersMap, toDelete);
        }
    }

    /// <summary>
    ///     Takes an array of attachment items that belong to the same main-parent item, sorts them in descending order by
    ///     their maximum price. For each attachment, a roll is made to determine if a deletion should be made. Once the
    ///     number of deletions has been counted, the attachments are added to the toDelete Set, starting with the most
    ///     valuable attachments first
    /// </summary>
    /// <param name="attachments">Array of attachment items to sort, filter, and roll</param>
    /// <param name="tradersMap">Trader ID each item is insured by (Key: Item ID, Value: Trader ID)</param>
    /// <param name="toDelete">array that accumulates the IDs of the items to be deleted</param>
    protected void ProcessAttachmentByParent(List<Item> attachments, Dictionary<MongoId, MongoId> tradersMap, HashSet<MongoId> toDelete)
    {
        // Create dict of item ids + their flea/handbook price (highest is chosen)
        var weightedAttachmentByPrice = insuranceController.WeightAttachmentsByPrice(attachments);

        // Get how many attachments we want to pull off parent
        var countOfAttachmentsToRemove = GetAttachmentCountToRemove(weightedAttachmentByPrice, tradersMap);

        // Create prob array and add all attachments with rouble price as the weight
        var attachmentsProbabilityArray = new ProbabilityObjectArray<MongoId, double?>(cloner);
        foreach (var (itemTpl, price) in weightedAttachmentByPrice)
        {
            attachmentsProbabilityArray.Add(new ProbabilityObject<MongoId, double?>(itemTpl, price, null));
        }

        // Draw x attachments from weighted array to remove from parent, remove from pool after being picked
        var attachmentIdsToRemove = attachmentsProbabilityArray.DrawAndRemove((int)countOfAttachmentsToRemove);
        foreach (var attachmentId in attachmentIdsToRemove)
        {
            toDelete.Add(attachmentId);
        }

        insuranceController.LogAttachmentsBeingRemoved(attachmentIdsToRemove, attachments, weightedAttachmentByPrice);

        if (config.DebugLogs)
        {
            L.Debug($"Number of attachments to be deleted: {attachmentIdsToRemove.Count}");
        }
    }

    /// <summary>
    ///     Get count of items to remove from weapon (take into account trader + price of attachment)
    /// </summary>
    /// <param name="weightedAttachmentByPrice">Dict of item Tpls and their rouble price</param>
    /// <param name="tradersMap">Trader ID each item is insured by (Key: Item ID, Value: Trader ID)</param>
    /// <returns>Attachment count to remove</returns>
    protected double GetAttachmentCountToRemove(
        Dictionary<MongoId, double> weightedAttachmentByPrice,
        Dictionary<MongoId, MongoId> tradersMap
    )
    {
        const int removeCount = 0;

        if (randomUtil.GetChance100(insuranceConfig.ChanceNoAttachmentsTakenPercent))
        {
            return removeCount;
        }

        // Get attachments count above or equal to price set in config
        // Mark to delete if trader id is not found
        return weightedAttachmentByPrice
            .Where(attachment => attachment.Value >= insuranceConfig.MinAttachmentRoublePriceToBeTaken)
            .Count(attachment => insuranceController.RollForDelete(tradersMap.TryGetValue(attachment.Key, out var id) ? id : MongoId.Empty()) ?? true);
    }
}
