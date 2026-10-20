using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorApp.Shared.PhotoShoot
{
    public class GearLine
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public int Quantity { get; set; } = 1;
        public EquipmentCategory Category { get; set; }
        public EquipmentSupplier Supplier { get; set; }
        public bool Optional { get; set; }
        public string Preparation { get; set; } = "";
        public string Notes { get; set; } = "";
        public bool Packed { get; set; }
        public bool Custom { get; set; }
        public List<string> ShotIds { get; set; } = new List<string>();
        public List<string> ShotTitles { get; set; } = new List<string>();
    }

    public class PackingCommand
    {
        public string Action { get; set; } = "";
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string Notes { get; set; } = "";
        public int Quantity { get; set; } = 1;
        public EquipmentCategory Category { get; set; }
        public EquipmentSupplier Supplier { get; set; } = EquipmentSupplier.Bring;
    }

    /// <summary>
    /// Reads props that the shot and its setup already name. It does not invent gear from inspiration photos.
    /// </summary>
    public static class PhotoShootGear
    {
        private sealed class Def
        {
            public string Key = "";
            public string Name = "";
            public EquipmentCategory Category;
            public EquipmentSupplier Supplier;
            public string[] Needles = Array.Empty<string>();
            public string Preparation = "";
        }

        private static readonly Def[] Catalog =
        {
            new Def { Key = "cards", Name = "Playing cards", Category = EquipmentCategory.Magic, Supplier = EquipmentSupplier.Bring, Needles = new[] { "playing card", "deck of" }, Preparation = "One fresh deck is enough unless a shot asks for a jumbo card too." },
            new Def { Key = "jumbo-card", Name = "Jumbo playing card", Category = EquipmentCategory.Magic, Supplier = EquipmentSupplier.Bring, Needles = new[] { "jumbo" } },
            new Def { Key = "wand", Name = "Magic wand", Category = EquipmentCategory.Magic, Supplier = EquipmentSupplier.Bring, Needles = new[] { "magic wand", "trick wand", "bent trick wand" } },
            new Def { Key = "flower-wand", Name = "Wilting flower wand", Category = EquipmentCategory.Magic, Supplier = EquipmentSupplier.Bring, Needles = new[] { "wilting flower" } },
            new Def { Key = "silk", Name = "Silk handkerchief", Category = EquipmentCategory.Magic, Supplier = EquipmentSupplier.Bring, Needles = new[] { "silk handkerchief", "handkerchiefs" }, Preparation = "Bring burgundy, gold, and teal when a shot uses all three colors." },
            new Def { Key = "streamer", Name = "Silk streamer", Category = EquipmentCategory.Magic, Supplier = EquipmentSupplier.Bring, Needles = new[] { "streamer" } },
            new Def { Key = "rope", Name = "Magician's rope", Category = EquipmentCategory.Magic, Supplier = EquipmentSupplier.Bring, Needles = new[] { "rope" } },
            new Def { Key = "hat", Name = "Top hat", Category = EquipmentCategory.Magic, Supplier = EquipmentSupplier.Bring, Needles = new[] { "top hat" } },
            new Def { Key = "cups", Name = "Cups and balls set", Category = EquipmentCategory.Magic, Supplier = EquipmentSupplier.Bring, Needles = new[] { "cups and balls" } },
            new Def { Key = "balls", Name = "Sponge balls", Category = EquipmentCategory.Magic, Supplier = EquipmentSupplier.Bring, Needles = new[] { "sponge ball" } },
            new Def { Key = "chicken", Name = "Rubber chicken", Category = EquipmentCategory.Magic, Supplier = EquipmentSupplier.Bring, Needles = new[] { "rubber chicken" } },
            new Def { Key = "cleaver", Name = "Foam cleaver headband", Category = EquipmentCategory.Magic, Supplier = EquipmentSupplier.Bring, Needles = new[] { "cleaver" }, Preparation = "Soft foam only. It should read as fake." },
            new Def { Key = "stool", Name = "Stool", Category = EquipmentCategory.Furniture, Supplier = EquipmentSupplier.Confirm, Needles = new[] { "stool" }, Preparation = "Ask the photographer before assuming a stool is in the studio." },
            new Def { Key = "table", Name = "Small table", Category = EquipmentCategory.Furniture, Supplier = EquipmentSupplier.Confirm, Needles = new[] { "table" }, Preparation = "Ask the photographer before assuming a table is in the studio." },
            new Def { Key = "apple-box", Name = "Apple box", Category = EquipmentCategory.Furniture, Supplier = EquipmentSupplier.Confirm, Needles = new[] { "apple box" } },
            new Def { Key = "whiteboard", Name = "Whiteboard", Category = EquipmentCategory.Miscellaneous, Supplier = EquipmentSupplier.Bring, Needles = new[] { "whiteboard" } },
            new Def { Key = "sign", Name = "Blank sign", Category = EquipmentCategory.Miscellaneous, Supplier = EquipmentSupplier.Bring, Needles = new[] { "foam-core", "foam core", "blank sign" } },
            new Def { Key = "marker", Name = "Dry-erase marker", Category = EquipmentCategory.Miscellaneous, Supplier = EquipmentSupplier.Bring, Needles = new[] { "dry-erase marker", "marker" } },
            new Def { Key = "jacket", Name = "Dark jacket", Category = EquipmentCategory.Wardrobe, Supplier = EquipmentSupplier.Bring, Needles = new[] { "jacket" } }
        };

        public static bool SpecifyFromExistingText(PhotoShootDocument doc)
        {
            var changed = false;
            foreach (var shot in doc.Shots)
            {
                if (shot.EquipmentSpecified)
                {
                    continue;
                }

                shot.Equipment = ReadShot(doc, shot);
                shot.EquipmentSpecified = true;
                changed = true;
            }

            return changed;
        }

        public static List<EquipmentNeed> ReadShot(PhotoShootDocument doc, PhotoShot shot)
        {
            var own = Join(shot.Props, shot.Title, shot.Summary, shot.HandPosition, shot.PhotographerInstructions, shot.PerformerInstructions, shot.BodyPosition);
            var setup = PhotoShootPlanner.FindSetup(doc, shot.SetupId);
            var inherit = SuppressSetupProps(own) || setup == null ? "" : Join(setup.Props);
            return Match(own + " " + inherit);
        }

        public static IReadOnlyList<GearLine> ForSetup(PhotoShootDocument doc, string? setupId)
        {
            var shots = doc.Shots.Where(s => s.SetupId == (setupId ?? "")).ToList();
            return Consolidate(doc, shots).Where(line => !line.Custom).ToList();
        }

        public static IReadOnlyList<GearLine> PackingList(PhotoShootDocument doc) => Consolidate(doc, doc.Shots);

        public static string ChangeNote(PhotoShootDocument doc, PhotoShot? from, PhotoShot? to)
        {
            if (from == null || to == null)
            {
                return "";
            }

            var have = new HashSet<string>(from.Equipment.Select(item => item.Key), StringComparer.Ordinal);
            var needed = to.Equipment.Where(item => !have.Contains(item.Key)).Select(item => item.Name).ToList();
            return needed.Count == 0 ? "No equipment changes needed." : "Next shot requires: " + string.Join(", ", needed);
        }

        public static string ApplyCommand(PhotoShootDocument doc, PackingCommand? command)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.Action))
            {
                return "Invalid packing change.";
            }

            doc.Packing ??= new List<PackingNote>();
            var action = command.Action.Trim().ToLowerInvariant();
            if (action == "add")
            {
                var name = (command.Name ?? "").Trim();
                if (name.Length == 0)
                {
                    return "Name the item.";
                }

                var key = "custom:" + name.ToLowerInvariant();
                if (doc.Packing.Any(note => note.Key == key))
                {
                    return "That item is already on the list.";
                }

                doc.Packing.Add(new PackingNote
                {
                    Key = key,
                    Name = name,
                    Custom = true,
                    Quantity = command.Quantity < 1 ? 1 : command.Quantity,
                    Category = command.Category,
                    Supplier = command.Supplier,
                    Notes = command.Notes ?? ""
                });
                return "Added to the packing list.";
            }

            var note = doc.Packing.FirstOrDefault(item => item.Key == command.Key);
            if (action == "remove")
            {
                if (note == null || !note.Custom)
                {
                    return "Only items you added by hand can be removed.";
                }

                doc.Packing.Remove(note);
                return "Removed from the packing list.";
            }

            if (note == null)
            {
                note = new PackingNote { Key = command.Key ?? "" };
                doc.Packing.Add(note);
            }

            if (action == "pack") note.Packed = true;
            else if (action == "unpack") note.Packed = false;
            else if (action == "note") note.Notes = command.Notes ?? "";
            else if (action == "quantity") note.Quantity = command.Quantity < 1 ? 1 : command.Quantity;
            else return "Invalid packing change.";

            return "Packing list updated.";
        }

        private static List<GearLine> Consolidate(PhotoShootDocument doc, IEnumerable<PhotoShot> shots)
        {
            var lines = new Dictionary<string, GearLine>(StringComparer.Ordinal);
            foreach (var shot in shots)
            {
                foreach (var item in shot.Equipment ?? new List<EquipmentNeed>())
                {
                    if (string.IsNullOrWhiteSpace(item.Key))
                    {
                        continue;
                    }

                    if (!lines.TryGetValue(item.Key, out var line))
                    {
                        line = new GearLine
                        {
                            Key = item.Key,
                            Name = item.Name,
                            Category = item.Category,
                            Supplier = item.Supplier,
                            Preparation = item.Preparation,
                            Optional = true
                        };
                        lines[item.Key] = line;
                    }

                    line.Quantity = Math.Max(line.Quantity, item.Quantity);
                    line.Optional = line.Optional && item.Optional;
                    if (!line.ShotIds.Contains(shot.Id))
                    {
                        line.ShotIds.Add(shot.Id);
                        line.ShotTitles.Add(shot.Title);
                    }
                }
            }

            foreach (var custom in doc.Packing.Where(note => note.Custom))
            {
                lines[custom.Key] = new GearLine
                {
                    Key = custom.Key,
                    Name = custom.Name,
                    Quantity = custom.Quantity < 1 ? 1 : custom.Quantity,
                    Category = custom.Category,
                    Supplier = custom.Supplier,
                    Custom = true,
                    Notes = custom.Notes,
                    Packed = custom.Packed
                };
            }

            foreach (var saved in doc.Packing.Where(note => !note.Custom))
            {
                if (!lines.TryGetValue(saved.Key, out var line))
                {
                    continue;
                }

                line.Packed = saved.Packed;
                line.Notes = saved.Notes ?? "";
                if (saved.Quantity > 0)
                {
                    line.Quantity = saved.Quantity;
                }
            }

            return lines.Values
                .OrderBy(line => line.Supplier)
                .ThenBy(line => line.Category)
                .ThenBy(line => line.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<EquipmentNeed> Match(string text)
        {
            var hay = (text ?? "").ToLowerInvariant();
            var found = new List<EquipmentNeed>();
            foreach (var def in Catalog)
            {
                if (!def.Needles.Any(needle => hay.Contains(needle)))
                {
                    continue;
                }

                if (def.Key == "wand" && hay.Contains("wilting flower") && !hay.Contains("magic wand") && !hay.Contains("bent trick wand"))
                {
                    continue;
                }

                var quantity = 1;
                if (def.Key == "silk" && hay.Contains("burgundy") && hay.Contains("gold") && hay.Contains("teal"))
                {
                    quantity = 3;
                }

                found.Add(new EquipmentNeed
                {
                    Key = def.Key,
                    Name = def.Name,
                    Quantity = quantity,
                    Category = def.Category,
                    Supplier = def.Supplier,
                    Optional = LineIsOptional(hay, def.Needles),
                    Preparation = def.Preparation
                });
            }

            return found;
        }

        private static bool LineIsOptional(string hay, string[] needles)
        {
            foreach (var line in hay.Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (needles.Any(needle => line.Contains(needle)) && line.Contains("optional"))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SuppressSetupProps(string own) =>
            Has(own, "no prop") || Has(own, "empty hand") || Has(own, "prop-free");

        private static bool Has(string hay, string needle) =>
            hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string Join(params object?[] parts)
        {
            var bits = new List<string>();
            foreach (var part in parts)
            {
                if (part is IEnumerable<string> list)
                {
                    bits.AddRange(list);
                }
                else if (part is string text && text.Length > 0)
                {
                    bits.Add(text);
                }
            }

            return string.Join(" ", bits);
        }
    }
}
