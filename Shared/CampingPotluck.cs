using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorApp.Shared
{
    public static class PotluckOptionIds
    {
        public const string Mexican = "mexican";
        public const string Bbq = "bbq";
        public const string Other = "other";
        public const string FreeForAll = "free-for-all";
        public const string CustomPrefix = "custom:";

        public static string ToCustomOptionId(string text) =>
            CustomPrefix + text.Trim().ToLowerInvariant();

        public static bool IsCustomOptionId(string optionId) =>
            optionId.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public class PotluckOptionDefinition
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public bool IsBuiltIn { get; set; }
        public string CreatedBy { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public class PotluckParticipant
    {
        public string Name { get; set; } = "";
        public string Bringing { get; set; } = "";
        public Dictionary<string, int> Ratings { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public DateTime SubmittedAt { get; set; }
    }

    public class PotluckRateRequest
    {
        public string Name { get; set; } = "";
        public string OptionId { get; set; } = "";
        public int Rating { get; set; }
    }

    public class PotluckAddOptionRequest
    {
        public string Label { get; set; } = "";
        public string CreatedBy { get; set; } = "";
    }

    public class PotluckRemoveOptionRequest
    {
        public string OptionId { get; set; } = "";
    }

    public class PotluckParticipantRequest
    {
        public string Name { get; set; } = "";
        public string Bringing { get; set; } = "";
    }

    // Legacy shape kept for migration from earlier saves.
    public class PotluckVote
    {
        public string Name { get; set; } = "";
        public List<string> SelectedOptions { get; set; } = new List<string>();
        public string OtherText { get; set; } = "";
        public string Bringing { get; set; } = "";
        public DateTime SubmittedAt { get; set; }
    }

    public class PotluckSurveyState
    {
        public List<PotluckOptionDefinition> Options { get; set; } = new List<PotluckOptionDefinition>();
        public List<PotluckParticipant> Participants { get; set; } = new List<PotluckParticipant>();
        public List<PotluckVote> Votes { get; set; } = new List<PotluckVote>();

        public static PotluckSurveyState CreateDefault()
        {
            return new PotluckSurveyState
            {
                Options = new List<PotluckOptionDefinition>
                {
                    BuiltInOption(PotluckOptionIds.Mexican, "Mexican"),
                    BuiltInOption(PotluckOptionIds.Bbq, "BBQ"),
                    BuiltInOption(PotluckOptionIds.FreeForAll, "Free for all bring whatever you want")
                }
            };
        }

        public PotluckOptionScore GetOptionScore(string optionId)
        {
            var upvoters = new List<string>();
            var downvoters = new List<string>();

            foreach (var participant in Participants)
            {
                if (!participant.Ratings.TryGetValue(optionId, out var rating))
                {
                    continue;
                }

                if (rating > 0)
                {
                    upvoters.Add(participant.Name);
                }
                else if (rating < 0)
                {
                    downvoters.Add(participant.Name);
                }
            }

            upvoters.Sort(StringComparer.OrdinalIgnoreCase);
            downvoters.Sort(StringComparer.OrdinalIgnoreCase);

            return new PotluckOptionScore(
                optionId,
                upvoters.Count - downvoters.Count,
                upvoters,
                downvoters);
        }

        public IEnumerable<PotluckOptionRow> GetSortedOptionRows()
        {
            return Options
                .Select(option =>
                {
                    var score = GetOptionScore(option.Id);
                    return new PotluckOptionRow(option, score.Score, score.Upvoters, score.Downvoters);
                })
                .OrderByDescending(row => row.Score)
                .ThenBy(row => GetTieBreakOrder(row.Option.Id))
                .ThenBy(row => row.Option.Label, StringComparer.OrdinalIgnoreCase);
        }

        private static int GetTieBreakOrder(string optionId)
        {
            if (string.Equals(optionId, PotluckOptionIds.Mexican, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (string.Equals(optionId, PotluckOptionIds.Bbq, StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            return 2;
        }

        public int? GetParticipantRating(string participantName, string optionId)
        {
            var participant = Participants.FirstOrDefault(p =>
                string.Equals(p.Name, participantName, StringComparison.OrdinalIgnoreCase));

            if (participant == null || !participant.Ratings.TryGetValue(optionId, out var rating))
            {
                return null;
            }

            return rating;
        }

        private static PotluckOptionDefinition BuiltInOption(string id, string label) =>
            new PotluckOptionDefinition
            {
                Id = id,
                Label = label,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            };
    }

    public class PotluckOptionScore
    {
        public PotluckOptionScore(string optionId, int score, List<string> upvoters, List<string> downvoters)
        {
            OptionId = optionId;
            Score = score;
            Upvoters = upvoters;
            Downvoters = downvoters;
        }

        public string OptionId { get; }
        public int Score { get; }
        public List<string> Upvoters { get; }
        public List<string> Downvoters { get; }
    }

    public class PotluckOptionRow
    {
        public PotluckOptionRow(
            PotluckOptionDefinition option,
            int score,
            List<string> upvoters,
            List<string> downvoters)
        {
            Option = option;
            Score = score;
            Upvoters = upvoters;
            Downvoters = downvoters;
        }

        public PotluckOptionDefinition Option { get; }
        public int Score { get; }
        public List<string> Upvoters { get; }
        public List<string> Downvoters { get; }
    }
}
