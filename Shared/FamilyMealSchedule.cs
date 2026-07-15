using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorApp.Shared
{
    public static class FamilyIds
    {
        public const string Parents = "parents";
        public const string LukeToni = "luke-toni";
        public const string Alaina = "alaina";
        public const string LenighCasey = "lenigh-casey";
        public const string Stevie = "stevie";
    }

    public static class MealTypes
    {
        public const string Breakfast = "breakfast";
        public const string Lunch = "lunch";
        public const string Dinner = "dinner";
        public const string SpecialTreat = "special-treat";
    }

    public static class MealDates
    {
        public const string Friday = "2026-07-17";
        public const string Saturday = "2026-07-18";
        public const string Sunday = "2026-07-19";
    }

    public class FamilyDefinition
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
    }

    public class MealSlotDefinition
    {
        public string Id { get; set; } = "";
        public string Date { get; set; } = "";
        public string DateLabel { get; set; } = "";
        public string MealType { get; set; } = "";
        public string MealLabel { get; set; } = "";

        public string DisplayLabel => $"{DateLabel} · {MealLabel}";
    }

    public class MealAssignment
    {
        public string SlotId { get; set; } = "";
        public string CookFamilyId { get; set; } = "";
        public string Dish { get; set; } = "";
        public List<string> CookForFamilyIds { get; set; } = new List<string>();
        public DateTime UpdatedAt { get; set; }

        public bool HasCook => !string.IsNullOrWhiteSpace(CookFamilyId);
    }

    public class FamilyMealScheduleState
    {
        public List<MealAssignment> Assignments { get; set; } = new List<MealAssignment>();

        public static IReadOnlyList<FamilyDefinition> Families { get; } = new List<FamilyDefinition>
        {
            new FamilyDefinition { Id = FamilyIds.Parents, Label = "Steve & Laura Hammer (Parents)" },
            new FamilyDefinition { Id = FamilyIds.LukeToni, Label = "Luke & Toni Hammer" },
            new FamilyDefinition { Id = FamilyIds.Alaina, Label = "Alaina Bott" },
            new FamilyDefinition { Id = FamilyIds.LenighCasey, Label = "Lenigh & Casey Holt" },
            new FamilyDefinition { Id = FamilyIds.Stevie, Label = "Stevie Hammer" }
        };

        public static IReadOnlyList<MealSlotDefinition> MealSlots { get; } = BuildMealSlots();

        public MealAssignment GetOrCreateAssignment(string slotId)
        {
            var assignment = Assignments.FirstOrDefault(a =>
                string.Equals(a.SlotId, slotId, StringComparison.OrdinalIgnoreCase));

            if (assignment == null)
            {
                assignment = new MealAssignment { SlotId = slotId };
                Assignments.Add(assignment);
            }

            return assignment;
        }

        public bool HasFamilyCooked(string familyId)
        {
            if (string.IsNullOrWhiteSpace(familyId))
            {
                return false;
            }

            return Assignments.Any(a =>
                string.Equals(a.CookFamilyId, familyId, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<string> GetFamiliesEatingWithoutCooking()
        {
            var eaters = Assignments
                .SelectMany(a => a.CookForFamilyIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return eaters
                .Where(id => !HasFamilyCooked(id))
                .Select(GetFamilyLabel)
                .ToList();
        }

        public static string GetFamilyLabel(string familyId)
        {
            return Families.FirstOrDefault(f =>
                    string.Equals(f.Id, familyId, StringComparison.OrdinalIgnoreCase))
                ?.Label
                ?? familyId;
        }

        public static FamilyMealScheduleState CreateDefault()
        {
            return new FamilyMealScheduleState
            {
                Assignments = MealSlots
                    .Select(slot => new MealAssignment { SlotId = slot.Id })
                    .ToList()
            };
        }

        public FamilyMealScheduleState Normalize()
        {
            var bySlot = Assignments
                .Where(a => !string.IsNullOrWhiteSpace(a.SlotId))
                .GroupBy(a => a.SlotId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var normalized = new List<MealAssignment>();
            foreach (var slot in MealSlots)
            {
                if (!bySlot.TryGetValue(slot.Id, out var assignment))
                {
                    assignment = new MealAssignment { SlotId = slot.Id };
                }

                assignment.CookFamilyId = assignment.CookFamilyId?.Trim() ?? "";
                assignment.Dish = assignment.Dish?.Trim() ?? "";
                assignment.CookForFamilyIds = (assignment.CookForFamilyIds ?? new List<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(id => Families.Any(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (!string.IsNullOrWhiteSpace(assignment.CookFamilyId)
                    && !Families.Any(f => string.Equals(f.Id, assignment.CookFamilyId, StringComparison.OrdinalIgnoreCase)))
                {
                    assignment.CookFamilyId = "";
                }

                if (!assignment.HasCook)
                {
                    assignment.Dish = "";
                }

                normalized.Add(assignment);
            }

            Assignments = normalized;
            return this;
        }

        private static List<MealSlotDefinition> BuildMealSlots()
        {
            var dates = new[]
            {
                (MealDates.Friday, "Fri 7/17"),
                (MealDates.Saturday, "Sat 7/18"),
                (MealDates.Sunday, "Sun 7/19")
            };

            var meals = new[]
            {
                (MealTypes.Breakfast, "Breakfast"),
                (MealTypes.Lunch, "Lunch"),
                (MealTypes.Dinner, "Dinner"),
                (MealTypes.SpecialTreat, "desert")
            };

            var slots = new List<MealSlotDefinition>();
            foreach (var (date, dateLabel) in dates)
            {
                foreach (var (mealType, mealLabel) in meals)
                {
                    slots.Add(new MealSlotDefinition
                    {
                        Id = $"{date}:{mealType}",
                        Date = date,
                        DateLabel = dateLabel,
                        MealType = mealType,
                        MealLabel = mealLabel
                    });
                }
            }

            return slots;
        }
    }

    public class FamilyMealAssignmentRequest
    {
        public string SlotId { get; set; } = "";
        public string CookFamilyId { get; set; } = "";
        public string Dish { get; set; } = "";
        public List<string> CookForFamilyIds { get; set; } = new List<string>();
    }
}
