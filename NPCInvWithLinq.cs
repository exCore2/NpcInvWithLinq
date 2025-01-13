﻿using ExileCore2;
using ExileCore2.PoEMemory;
using ExileCore2.PoEMemory.Elements;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.Shared.Cache;
using ExileCore2.Shared.Helpers;
using ItemFilterLibrary;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using ExileCore2.Shared.Nodes;
using static NPCInvWithLinq.ServerAndStashWindow;
using RectangleF = ExileCore2.Shared.RectangleF;

namespace NPCInvWithLinq;

public class ServerAndStashWindow
{
    public IList<WindowSet> Tabs { get; set; }

    public class WindowSet
    {
        public int Index { get; set; }
        public string Title { get; set; }
        public bool IsVisible { get; set; }
        public Element TabNameElement { get; set; }
        public List<CustomItemData> ServerItems { get; set; }
        public List<CustomItemData> TradeWindowItems { get; set; }
        public ServerInventory Inventory { get; set; }

        public override string ToString()
        {
            return $"Tab({Title}) is Index({Index}) IsVisible({IsVisible}) [ServerItems({ServerItems.Count}), TradeWindowItems({TradeWindowItems.Count})]";
        }
    }
}

public class NPCInvWithLinq : BaseSettingsPlugin<NPCInvWithLinqSettings>
{
    private readonly CachedValue<List<WindowSet>> _storedStashAndWindows;
    private readonly CachedValue<List<CustomItemData>> _rewardItems;
    private readonly CachedValue<List<CustomItemData>> _ritualItems;
    private List<FilterEntry> _itemFilters;
    private PurchaseWindow _purchaseWindowHideout;
    private PurchaseWindow _purchaseWindow;

    private record FilterEntry(ItemFilter<CustomItemData> Filter, Color Color);

    public NPCInvWithLinq()
    {
        Name = "NPC Inv With Linq";
        _storedStashAndWindows = new TimeCache<List<WindowSet>>(CacheUtils.RememberLastValue<List<WindowSet>>(UpdateCurrentTradeWindow), 50);
        _rewardItems = new TimeCache<List<CustomItemData>>(GetRewardItems, 1000);
        _ritualItems = new TimeCache<List<CustomItemData>>(GetRitualItems, 1000);
    }
    public override bool Initialise()
    {
        Settings.ReloadFilters.OnPressed = LoadRuleFiles;
        LoadRuleFiles();
        return true;
    }

    public override void Tick()
    {
        _purchaseWindowHideout = GameController.Game.IngameState.IngameUi.PurchaseWindowHideout;
        _purchaseWindow = GameController.Game.IngameState.IngameUi.PurchaseWindow;
    }

    public override void Render()
    {
        var hoveredItem = GetHoveredItem();
        PerformItemFilterTest(hoveredItem);
        ProcessPurchaseWindow(hoveredItem);
        ProcessRewardsWindow(hoveredItem);
        ProcessRitualWindow(hoveredItem);
    }

    private void ProcessRewardsWindow(Element hoveredItem)
    {
        if (!GameController.IngameState.IngameUi.QuestRewardWindow.IsVisible) return;

        foreach (var reward in _rewardItems?.Value ?? [])
        {
            if (GetItemFilter(reward) is { } filter)
            {
                var frameColor = filter.Color;
                if (hoveredItem != null && hoveredItem.Tooltip.GetClientRectCache.Intersects(reward.ClientRectangle) && hoveredItem.Entity.Address != reward.Entity.Address)
                {
                    frameColor = frameColor.ToImguiVec4(45).ToColor();
                }

                Graphics.DrawFrame(reward.ClientRectangle, frameColor, Settings.FrameThickness);
            }
        }
    }
    private void ProcessRitualWindow(Element hoveredItem)
    {
        if (!GameController.IngameState.IngameUi.RitualWindow.IsVisible) return;

        foreach (var reward in _ritualItems?.Value ?? [])
        {
            if (GetItemFilter(reward) is { } filter)
            {
                var frameColor = filter.Color;
                if (hoveredItem != null && hoveredItem.Tooltip.GetClientRectCache.Intersects(reward.ClientRectangle) && hoveredItem.Entity.Address != reward.Entity.Address)
                {
                    frameColor = frameColor.ToImguiVec4(45).ToColor();
                }

                Graphics.DrawFrame(reward.ClientRectangle, frameColor, Settings.FrameThickness);
            }
        }
    }

    private List<CustomItemData> GetRewardItems() =>
        GameController.IngameState.IngameUi.QuestRewardWindow.GetPossibleRewards()
            .Where(item => item.Item2 is { Address: not 0, IsValid: true })
            .Select(item => new CustomItemData(item.Item1, GameController, EKind.QuestReward, item.Item2.GetClientRectCache))
            .ToList();

    private List<CustomItemData> GetRitualItems() =>
    GameController.IngameState.IngameUi.RitualWindow.InventoryElement.VisibleInventoryItems
        .Where(item => item.Item is { Address: not 0, IsValid: true })
        .Select(item => new CustomItemData(item.Item, GameController, EKind.RitualReward, item.GetClientRectCache))
        .ToList();
    private void ProcessPurchaseWindow(Element hoveredItem)
    {
        if (!IsPurchaseWindowVisible())
            return;

        List<string> unSeenItems = [];
        ProcessStoredTabs(unSeenItems, hoveredItem);

        PurchaseWindow purchaseWindowItems = GetVisiblePurchaseWindow();
        var serverItemsBox = CalculateServerItemsBox(unSeenItems, purchaseWindowItems);

        DrawServerItems(serverItemsBox, unSeenItems, hoveredItem);
    }

    private void DrawServerItems(RectangleF serverItemsBox, List<string> unSeenItems, Element hoveredItem)
    {
        if (hoveredItem == null || !hoveredItem.Tooltip.GetClientRectCache.Intersects(serverItemsBox))
        {
            var boxColor = Color.FromArgb(150, 0, 0, 0);
            var textColor = Color.FromArgb(230, 255, 255, 255);

            Graphics.DrawBox(serverItemsBox, boxColor);

            for (int i = 0; i < unSeenItems.Count; i++)
            {
                string stringItem = unSeenItems[i];
                var textHeight = Graphics.MeasureText(stringItem);
                var textPadding = 10;

                Graphics.DrawText(stringItem, new Vector2(serverItemsBox.X + textPadding, serverItemsBox.Y + (textHeight.Y * i)), textColor);
            }
        }
    }

    private Element GetHoveredItem()
    {
        return GameController.IngameState.UIHover is { Address: not 0, Entity.IsValid: true } hover ? hover : null;
    }

    private bool IsPurchaseWindowVisible()
    {
        return _purchaseWindowHideout.IsVisible || _purchaseWindow.IsVisible;
    }

    private void ProcessStoredTabs(List<string> unSeenItems, Element hoveredItem)
    {
        foreach (var storedTab in _storedStashAndWindows.Value)
        {
            if (storedTab.IsVisible)
                ProcessVisibleTabItems(storedTab.TradeWindowItems, hoveredItem);
            else
                ProcessHiddenTabItems(storedTab, unSeenItems, hoveredItem);
        }
    }

    private void ProcessVisibleTabItems(IEnumerable<CustomItemData> items, Element hoveredItem)
    {
        foreach (var visibleItem in items.Where(item => item != null))
        {
            DrawItemFrame(visibleItem, hoveredItem);
        }
    }

    private void ProcessHiddenTabItems(WindowSet storedTab, List<string> unSeenItems, Element hoveredItem)
    {
        var tabHadWantedItem = false;
        foreach (var hiddenItem in storedTab.ServerItems)
        {
            if (hiddenItem == null) continue;
            if (ItemInFilter(hiddenItem))
            {
                ProcessUnseenItems(unSeenItems, storedTab, hoveredItem);
                unSeenItems.Add($"\t{hiddenItem.Name}");
                tabHadWantedItem = true;
            }
        }

        if (tabHadWantedItem)
            unSeenItems.Add("");
    }

    private void ProcessUnseenItems(List<string> unSeenItems, WindowSet storedTab, Element hoveredItem)
    {
        if (!unSeenItems.Contains($"Tab [{storedTab.Title}]"))
        {
            unSeenItems.Add($"Tab [{storedTab.Title}]");
            if (Settings.DrawOnTabLabels)
            {
                DrawTabNameElementFrame(storedTab.TabNameElement, hoveredItem);
            }
        }
    }

    private void DrawItemFrame(CustomItemData item, Element hoveredItem)
    {
        if (GetItemFilter(item) is { } filter)
        {
            var frameColor = filter.Color;
            if (hoveredItem != null && hoveredItem.Tooltip.GetClientRectCache.Intersects(item.ClientRectangle) && hoveredItem.Entity.Address != item.Entity.Address)
            {
                frameColor = frameColor.ToImguiVec4(45).ToColor();
            }

            Graphics.DrawFrame(item.ClientRectangle, frameColor, Settings.FrameThickness);
        }
    }

    private void DrawTabNameElementFrame(Element tabNameElement, Element hoveredItem)
    {
        var frameColor = Settings.TabFrameColor;
        if (hoveredItem == null || !hoveredItem.Tooltip.GetClientRectCache.Intersects(tabNameElement.GetClientRectCache))
        {
            Graphics.DrawFrame(tabNameElement.GetClientRectCache, frameColor, Settings.FrameThickness);
        }
        else
        {
            Graphics.DrawFrame(tabNameElement.GetClientRectCache, frameColor.Value.ToImguiVec4(45).ToColor(), Settings.FrameThickness);
        }
    }

    private RectangleF CalculateServerItemsBox(List<string> unSeenItems, PurchaseWindow purchaseWindowItems)
    {
        var startingPoint = purchaseWindowItems.TabContainer.GetClientRectCache.TopRight;
        startingPoint.X += 15;

        var longestText = unSeenItems.MaxBy(s => s.Length);
        var textHeight = Graphics.MeasureText(longestText);
        var textPadding = 10;

        return new RectangleF
        {
            Height = textHeight.Y * unSeenItems.Count,
            Width = textHeight.X + (textPadding * 2),
            X = startingPoint.X,
            Y = startingPoint.Y
        };
    }

    private PurchaseWindow GetVisiblePurchaseWindow()
    {
        return _purchaseWindowHideout.IsVisible ? _purchaseWindowHideout : _purchaseWindow.IsVisible ? _purchaseWindow : null;
    }

    private void PerformItemFilterTest(Element hoveredItem)
    {
        if (Settings.FilterTest.Value is { Length: > 0 } && hoveredItem != null)
        {
            var filter = ItemFilter.LoadFromString(Settings.FilterTest);
            var matched = filter.Matches(new ItemData(hoveredItem.Entity, GameController));
            DebugWindow.LogMsg($"Debug item match on hover: {matched}");
        }
    }

    internal void LoadRuleFiles()
    {
        var pickitConfigFileDirectory = ConfigDirectory;
        var existingRules = Settings.NPCInvRules;

        if (!string.IsNullOrEmpty(Settings.CustomConfigDir))
        {
            var customConfigFileDirectory = Path.Combine(Path.GetDirectoryName(ConfigDirectory), Settings.CustomConfigDir);

            if (Directory.Exists(customConfigFileDirectory))
            {
                pickitConfigFileDirectory = customConfigFileDirectory;
            }
            else
            {
                DebugWindow.LogError("[NPC Inventory] custom config folder does not exist.", 15);
            }
        }

        try
        {
            var newRules = new DirectoryInfo(pickitConfigFileDirectory).GetFiles("*.ifl")
                .Select(x => new NPCInvRule(x.Name, Path.GetRelativePath(pickitConfigFileDirectory, x.FullName), false))
                .ExceptBy(existingRules.Select(x => x.Location), x => x.Location)
                .ToList();
            foreach (var groundRule in existingRules)
            {
                var fullPath = Path.Combine(pickitConfigFileDirectory, groundRule.Location);
                if (File.Exists(fullPath))
                {
                    newRules.Add(groundRule);
                }
                else
                {
                    LogError($"File '{groundRule.Name}' not found.");
                }
            }

            _itemFilters = newRules
                .Where(rule => rule.Enabled)
                .Select(rule => new FilterEntry(ItemFilter.LoadFromPath<CustomItemData>(Path.Combine(pickitConfigFileDirectory, rule.Location)), rule.Color))
                .ToList();

            Settings.NPCInvRules = newRules;
        }
        catch (Exception e)
        {
            LogError($"An error occurred while loading rule files: {e.Message}");
        }
    }

    private List<WindowSet> UpdateCurrentTradeWindow(List<WindowSet> previousValue)
    {
        var previousDict = previousValue?.ToDictionary(x => (x.Title, x.Inventory.Address, x.Inventory.ServerRequestCounter, x.IsVisible, x.TradeWindowItems.Count));
        var purchaseWindowItems = (_purchaseWindowHideout, _purchaseWindow) switch
        {
            ({ IsVisible: true } w, _) => w,
            (_, { IsVisible: true } w) => w,
            _ => null,
        };

        if (purchaseWindowItems == null)
            return [];

        return purchaseWindowItems.TabContainer.Inventories.Select((inventory, i) =>
        {
            var uiInventory = inventory?.Inventory;
            if (uiInventory == null) return null;
            var serverInventory = uiInventory.ServerInventory;
            if (serverInventory == null)
            {
                DebugWindow.LogError($"Server inventory for ui inventory {uiInventory} ({uiInventory.InvType}) is missing");
            }

            var isVisible = uiInventory.IsVisible;
            var visibleValidUiItems = uiInventory.VisibleInventoryItems
                .Where(x => x.Item?.Path != null).ToList();
            var title = $"-{i + 1}-";
            if (previousDict?.TryGetValue((title, serverInventory?.Address ?? 0, serverInventory?.ServerRequestCounter ?? -1, isVisible, visibleValidUiItems.Count),
                    out var previousSet) == true)
            {
                return previousSet;
            }

            var newTab = new WindowSet
            {
                Inventory = serverInventory,
                Index = i,
                ServerItems = serverInventory?.Items.Where(x => x?.Path != null).Select(x => new CustomItemData(x, GameController, EKind.Shop)).ToList() ?? [],
                TradeWindowItems = visibleValidUiItems
                    .Select(x => new CustomItemData(x.Item, GameController, EKind.Shop, x.GetClientRectCache))
                    .ToList(),
                Title = title,
                IsVisible = isVisible,
                TabNameElement = inventory.TabButton
            };

            return newTab;
        }).Where(x => x != null).ToList();
    }

    private bool ItemInFilter(CustomItemData item) => GetItemFilter(item) != null;

    private FilterEntry GetItemFilter(CustomItemData item) => _itemFilters?.FirstOrDefault(filter => filter.Filter.Matches(item));
}
