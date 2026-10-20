using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorApp.Shared.PhotoShoot
{
    /// <summary>One shooting block as it should be labeled: a plural name and a separate shot count.</summary>
    public class PoseBlock
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public List<SetupGroup> Sections { get; set; } = new List<SetupGroup>();
        public int ShotCount => Sections.Sum(section => section.Shots.Count);
    }

    /// <summary>Labels for the browse menu, the dashboard, and the upload list. Shooting order is unchanged.</summary>
    public static class PhotoShootDisplay
    {
        public static IReadOnlyList<PoseBlock> Blocks(PhotoShootDocument doc)
        {
            var blocks = new List<PoseBlock>();
            foreach (var group in PhotoShootPlanner.Groups(doc, includeEmptySetups: false))
            {
                var number = group.Setup?.ShootingBlock ?? 0;
                var key = number > 0 ? "block-" + number : "setup-" + (group.Setup?.Id ?? "unassigned");
                var block = number > 0 ? blocks.FirstOrDefault(item => item.Key == key) : null;
                if (block == null)
                {
                    block = new PoseBlock { Key = key, Title = BlockTitle(group) };
                    blocks.Add(block);
                }

                block.Sections.Add(group);
            }

            return blocks;
        }

        public static string BlockTitle(SetupGroup group)
        {
            if (group.Setup == null)
            {
                return "Unassigned";
            }

            if (!string.IsNullOrWhiteSpace(group.Setup.BlockName))
            {
                return group.Setup.BlockName.Trim();
            }

            return StripWhite(group.Setup.Name);
        }

        /// <summary>The part of a setup name that is not already said by the block heading.</summary>
        public static string SectionTitle(PhotoSetup? setup, string blockTitle)
        {
            if (setup == null)
            {
                return "";
            }

            var name = StripWhite(setup.Name);
            if (name.Length == 0 || string.Equals(name, blockTitle, StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            return name;
        }

        public static string StripWhite(string? name)
        {
            var text = (name ?? "").Trim();
            const string prefix = "White Seamless - ";
            const string words = "White Seamless";
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(prefix.Length).Trim();
            }
            else if (text.StartsWith(words, StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(words.Length).Trim(' ', '-');
            }

            return text.Length == 0 ? (name ?? "").Trim() : text;
        }
    }
}
