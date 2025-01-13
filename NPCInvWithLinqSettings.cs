﻿using ExileCore2.Shared.Attributes;
using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json.Serialization;
using System.Numerics;
using ImGuiNET;

namespace NPCInvWithLinq;

public class NPCInvWithLinqSettings : ISettings
{
    public NPCInvWithLinqSettings()
    {
        RuleConfig = new RuleRenderer(this);
    }

    public ToggleNode Enable { get; set; } = new ToggleNode(false);
    public ToggleNode DrawOnTabLabels { get; set; } = new ToggleNode(true);
    public ColorNode TabFrameColor { get; set; } = new ColorNode(Color.Red);
    public RangeNode<int> FrameThickness { get; set; } = new RangeNode<int>(1, 1, 20);

    [JsonIgnore]
    public TextNode FilterTest { get; set; } = new TextNode();

    [JsonIgnore]
    [Menu("Reload/Apply filters")]
    public ButtonNode ReloadFilters { get; set; } = new ButtonNode();

    [Menu("Use a Custom \"\\config\\custom_folder\" folder ")]
    public TextNode CustomConfigDir { get; set; } = new TextNode();

    public List<NPCInvRule> NPCInvRules { get; set; } = new List<NPCInvRule>();

    [JsonIgnore]
    public RuleRenderer RuleConfig { get; set; }

    [Submenu(RenderMethod = nameof(Render))]
    public class RuleRenderer
    {
        private readonly NPCInvWithLinqSettings _parent;

        public RuleRenderer(NPCInvWithLinqSettings parent)
        {
            _parent = parent;
        }

        public void Render(NPCInvWithLinq plugin)
        {
            if (ImGui.Button("Open rule folder"))
            {
                var configDir = plugin.ConfigDirectory;
                var customConfigFileDirectory = !string.IsNullOrEmpty(_parent.CustomConfigDir)
                    ? Path.Combine(Path.GetDirectoryName(plugin.ConfigDirectory), _parent.CustomConfigDir)
                    : null;

                var directoryToOpen = Directory.Exists(customConfigFileDirectory)
                    ? customConfigFileDirectory
                    : configDir;

                Process.Start("explorer.exe", directoryToOpen);
            }

            ImGui.Separator();
            ImGui.BulletText("Select Rules To Load");
            ImGui.BulletText("Ordering rule sets so general items will match first rather than last will improve performance");

            var tempNpcInvRules = new List<NPCInvRule>(_parent.NPCInvRules); // Create a copy
            var reloadRequired = false;

            for (int i = 0; i < tempNpcInvRules.Count; i++)
            {
                if (ImGui.ArrowButton($"##upButton{i}", ImGuiDir.Up) && i > 0)
                {
                    (tempNpcInvRules[i - 1], tempNpcInvRules[i]) = (tempNpcInvRules[i], tempNpcInvRules[i - 1]);
                    reloadRequired = true;
                }

                ImGui.SameLine();
                ImGui.Text(" ");
                ImGui.SameLine();

                if (ImGui.ArrowButton($"##downButton{i}", ImGuiDir.Down) && i < tempNpcInvRules.Count - 1)
                {
                    (tempNpcInvRules[i + 1], tempNpcInvRules[i]) = (tempNpcInvRules[i], tempNpcInvRules[i + 1]);
                    reloadRequired = true;
                }

                ImGui.SameLine();
                ImGui.Text(" - ");
                ImGui.SameLine();

                var refToggle = tempNpcInvRules[i].Enabled;
                if (ImGui.Checkbox($"{tempNpcInvRules[i].Name}##checkbox{i}", ref refToggle))
                {
                    tempNpcInvRules[i].Enabled = refToggle;
                    reloadRequired = true;
                }

                ImGui.SameLine();
                if (tempNpcInvRules[i].Color.DrawPicker($"##color{i}"))
                {
                    plugin.LoadRuleFiles();
                }
            }

            _parent.NPCInvRules = tempNpcInvRules;

            if (reloadRequired)
            {
                plugin.LoadRuleFiles();
            }
        }
    }
}

public class NPCInvRule
{
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public bool Enabled { get; set; } = false;
    public ColorNode Color { get; set; } = new ColorNode(System.Drawing.Color.Red);

    public NPCInvRule(string name, string location, bool enabled)
    {
        Name = name;
        Location = location;
        Enabled = enabled;
    }
}