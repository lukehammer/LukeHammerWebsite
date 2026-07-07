using System;
using System.Collections.Generic;
using System.Linq;
using BlazorApp.Shared;

namespace ApiIsolated
{
    internal static class CampingPotluckMigration
    {
        public static PotluckSurveyState Normalize(PotluckSurveyState state)
        {
            state.Options ??= new List<PotluckOptionDefinition>();
            state.Participants ??= new List<PotluckParticipant>();
            state.Votes ??= new List<PotluckVote>();

            if (state.Options.Count == 0)
            {
                state = state.Votes.Count > 0 ? MigrateFromLegacyVotes(state) : PotluckSurveyState.CreateDefault();
            }

            EnsureBuiltInOptions(state);

            if (state.Votes.Count > 0 && state.Participants.Count == 0)
            {
                state = MigrateFromLegacyVotes(state);
            }

            state.Votes.Clear();
            return state;
        }

        private static PotluckSurveyState MigrateFromLegacyVotes(PotluckSurveyState state)
        {
            var migrated = PotluckSurveyState.CreateDefault();
            var optionLabels = migrated.Options.ToDictionary(option => option.Id, option => option, StringComparer.OrdinalIgnoreCase);

            foreach (var vote in state.Votes)
            {
                var participant = new PotluckParticipant
                {
                    Name = vote.Name,
                    Bringing = vote.Bringing ?? "",
                    SubmittedAt = vote.SubmittedAt == default ? DateTime.UtcNow : vote.SubmittedAt
                };

                foreach (var selected in vote.SelectedOptions ?? new List<string>())
                {
                    var optionId = selected;
                    var label = selected;

                    if (string.Equals(selected, PotluckOptionIds.Other, StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(vote.OtherText))
                        {
                            continue;
                        }

                        optionId = PotluckOptionIds.ToCustomOptionId(vote.OtherText);
                        label = vote.OtherText.Trim();
                    }
                    else if (PotluckOptionIds.IsCustomOptionId(selected))
                    {
                        label = vote.OtherText?.Trim();
                        if (string.IsNullOrWhiteSpace(label))
                        {
                            label = selected.Substring(PotluckOptionIds.CustomPrefix.Length);
                        }
                    }
                    else if (string.Equals(selected, PotluckOptionIds.Mexican, StringComparison.OrdinalIgnoreCase))
                    {
                        label = "Mexican";
                    }
                    else if (string.Equals(selected, PotluckOptionIds.Bbq, StringComparison.OrdinalIgnoreCase))
                    {
                        label = "BBQ";
                    }
                    else if (string.Equals(selected, PotluckOptionIds.FreeForAll, StringComparison.OrdinalIgnoreCase))
                    {
                        label = "Free for all bring whatever you want";
                    }

                    if (!optionLabels.ContainsKey(optionId))
                    {
                        var customOption = new PotluckOptionDefinition
                        {
                            Id = optionId,
                            Label = label,
                            IsBuiltIn = false,
                            CreatedBy = vote.Name,
                            CreatedAt = vote.SubmittedAt == default ? DateTime.UtcNow : vote.SubmittedAt
                        };
                        migrated.Options.Add(customOption);
                        optionLabels[optionId] = customOption;
                    }

                    participant.Ratings[optionId] = 1;
                }

                if (participant.Ratings.Count > 0 || !string.IsNullOrWhiteSpace(participant.Bringing))
                {
                    migrated.Participants.Add(participant);
                }
            }

            foreach (var option in state.Options)
            {
                if (!optionLabels.ContainsKey(option.Id))
                {
                    migrated.Options.Add(option);
                    optionLabels[option.Id] = option;
                }
            }

            return migrated;
        }

        private static void EnsureBuiltInOptions(PotluckSurveyState state)
        {
            AddBuiltInIfMissing(state, PotluckOptionIds.Mexican, "Mexican");
            AddBuiltInIfMissing(state, PotluckOptionIds.Bbq, "BBQ");
            AddBuiltInIfMissing(state, PotluckOptionIds.FreeForAll, "Free for all bring whatever you want");
        }

        private static void AddBuiltInIfMissing(PotluckSurveyState state, string id, string label)
        {
            if (state.Options.Any(option => string.Equals(option.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            state.Options.Add(new PotluckOptionDefinition
            {
                Id = id,
                Label = label,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            });
        }
    }
}
