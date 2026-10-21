using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using ApiIsolated.Notifications;
using BlazorApp.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ApiIsolated
{
    public class FamilyEventsFunction
    {
        private const int MaxTitleLength = 120;
        private const int MaxTextLength = 500;
        private const int MaxPostLength = 2000;
        private const int MaxDescriptionLength = 4000;

        private readonly ILogger _logger;

        public FamilyEventsFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<FamilyEventsFunction>();
        }

        private sealed class FamilyEventsHttpException : Exception
        {
            public FamilyEventsHttpException(HttpStatusCode status, string message) : base(message)
            {
                Status = status;
            }

            public HttpStatusCode Status { get; }
        }

        [Function("GetFamilyEvents")]
        public Task<HttpResponseData> Get(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "family-events")] HttpRequestData httpRequest) =>
            Run(httpRequest, "load family events", async () => await FamilyEventsStorage.LoadAsync());

        [Function("CreateFamilyEvent")]
        public async Task<HttpResponseData> Create(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events")] HttpRequestData httpRequest)
        {
            var request = await httpRequest.ReadFromJsonAsync<FamilyEventDetailsRequest>();
            return await Run(httpRequest, "create family event", () =>
            {
                RequireLuke(request?.SubmittedBy);
                var details = ValidateDetails(request!);
                details.Id = Guid.NewGuid().ToString("N");

                return FamilyEventsStorage.UpdateAsync(data =>
                {
                    var potluck = new PotluckSurveyState();
                    foreach (var label in (request!.ThemeOptions ?? new List<string>())
                                 .Select(theme => theme?.Trim() ?? "")
                                 .Where(theme => theme.Length > 0)
                                 .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        potluck.Options.Add(new PotluckOptionDefinition
                        {
                            Id = PotluckOptionIds.ToCustomOptionId(label),
                            Label = label,
                            IsBuiltIn = true,
                            CreatedBy = "Luke",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    details.Potluck = potluck;
                    details.SignupLists = MergeSignupLists(new List<FamilyEventSignupList>(), request.SignupLists);
                    details.LastModifiedAt = WashingtonScheduleTime.Now;
                    data.Events.Add(details);
                    return data;
                });
            });
        }

        [Function("UpdateFamilyEvent")]
        public async Task<HttpResponseData> Update(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "family-events/{id}")] HttpRequestData httpRequest,
            string id)
        {
            var request = await httpRequest.ReadFromJsonAsync<FamilyEventDetailsRequest>();
            return await Run(httpRequest, "update family event", () =>
            {
                RequireLuke(request?.SubmittedBy);
                var details = ValidateDetails(request!);

                return MutateEventAsync(id, familyEvent =>
                {
                    familyEvent.Title = details.Title;
                    familyEvent.Date = details.Date;
                    familyEvent.EndDate = details.EndDate;
                    familyEvent.StartTime = details.StartTime;
                    familyEvent.Location = details.Location;
                    familyEvent.Description = details.Description;
                    familyEvent.ThemeVotingEnabled = details.ThemeVotingEnabled;
                    familyEvent.BringPrompt = details.BringPrompt;
                    familyEvent.RsvpEnabled = details.RsvpEnabled;
                    familyEvent.RsvpGoal = details.RsvpGoal;
                    familyEvent.RsvpDeadline = details.RsvpDeadline;
                    familyEvent.SignupLists = MergeSignupLists(familyEvent.SignupLists, request!.SignupLists);
                });
            });
        }

        [Function("DeleteFamilyEvent")]
        public Task<HttpResponseData> Delete(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "family-events/{id}")] HttpRequestData httpRequest,
            string id) =>
            Run(httpRequest, "delete family event", () =>
            {
                RequireLuke(HttpUtility.ParseQueryString(httpRequest.Url.Query)["submittedBy"]);

                return FamilyEventsStorage.UpdateAsync(data =>
                {
                    var familyEvent = FindUnlockedEvent(data, id);
                    data.Events.Remove(familyEvent);
                    return data;
                });
            });

        [Function("SaveFamilyEventParticipant")]
        public async Task<HttpResponseData> SaveParticipant(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/participant")] HttpRequestData httpRequest,
            string id)
        {
            var request = await httpRequest.ReadFromJsonAsync<PotluckParticipantRequest>();
            return await Run(httpRequest, "save family event participant", () =>
            {
                var name = RequireName(request?.Name);
                var bringing = Truncate(request!.Bringing?.Trim() ?? "", MaxTextLength);

                return MutateAndNotifyAsync(id, name, familyEvent =>
                {
                    var participant = GetOrAddParticipant(familyEvent.Potluck, name);
                    participant.Bringing = bringing;
                    participant.SubmittedAt = DateTime.UtcNow;
                    return bringing.Length == 0 ? "cleared what they're bringing." : $"is bringing {Quote(bringing)}.";
                });
            });
        }

        [Function("RateFamilyEventOption")]
        public async Task<HttpResponseData> RateOption(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/rate")] HttpRequestData httpRequest,
            string id)
        {
            var request = await httpRequest.ReadFromJsonAsync<PotluckRateRequest>();
            return await Run(httpRequest, "save family event vote", () =>
            {
                var name = RequireName(request?.Name);
                if (string.IsNullOrWhiteSpace(request!.OptionId))
                {
                    throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "Option id is required.");
                }

                if (request.Rating is not (1 or -1 or 0))
                {
                    throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "Rating must be 1, -1, or 0.");
                }

                var optionId = request.OptionId.Trim().ToLowerInvariant();

                return MutateAndNotifyAsync(id, name, familyEvent =>
                {
                    RequireThemeVoting(familyEvent);
                    var option = familyEvent.Potluck.Options.FirstOrDefault(existingOption => string.Equals(existingOption.Id, optionId, StringComparison.OrdinalIgnoreCase))
                        ?? throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "Option not found.");

                    var participant = GetOrAddParticipant(familyEvent.Potluck, name);
                    if (request.Rating == 0)
                    {
                        participant.Ratings.Remove(optionId);
                    }
                    else
                    {
                        participant.Ratings[optionId] = request.Rating;
                    }

                    participant.SubmittedAt = DateTime.UtcNow;
                    return request.Rating switch
                    {
                        1 => $"upvoted {Quote(option.Label)}.",
                        -1 => $"downvoted {Quote(option.Label)}.",
                        _ => $"cleared their vote on {Quote(option.Label)}."
                    };
                });
            });
        }

        [Function("AddFamilyEventOption")]
        public async Task<HttpResponseData> AddOption(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/options")] HttpRequestData httpRequest,
            string id)
        {
            var request = await httpRequest.ReadFromJsonAsync<PotluckAddOptionRequest>();
            return await Run(httpRequest, "add family event option", () =>
            {
                var label = Truncate(request?.Label?.Trim() ?? "", MaxTitleLength);
                if (label.Length == 0)
                {
                    throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "Option label is required.");
                }

                var optionId = PotluckOptionIds.ToCustomOptionId(label);

                var createdBy = request!.CreatedBy?.Trim() ?? "";
                return MutateAndNotifyAsync(id, createdBy.Length == 0 ? "Someone" : createdBy, familyEvent =>
                {
                    RequireThemeVoting(familyEvent);
                    if (familyEvent.Potluck.Options.Any(existingOption => string.Equals(existingOption.Id, optionId, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "That option already exists.");
                    }

                    familyEvent.Potluck.Options.Add(new PotluckOptionDefinition
                    {
                        Id = optionId,
                        Label = label,
                        IsBuiltIn = false,
                        CreatedBy = createdBy,
                        CreatedAt = DateTime.UtcNow
                    });
                    return $"suggested the potluck theme {Quote(label)}.";
                });
            });
        }

        [Function("RemoveFamilyEventOption")]
        public async Task<HttpResponseData> RemoveOption(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/options/remove")] HttpRequestData httpRequest,
            string id)
        {
            var request = await httpRequest.ReadFromJsonAsync<PotluckRemoveOptionRequest>();
            return await Run(httpRequest, "remove family event option", () =>
            {
                if (string.IsNullOrWhiteSpace(request?.OptionId))
                {
                    throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "Option id is required.");
                }

                var optionId = request!.OptionId.Trim();

                return MutateAndNotifyAsync(id, "Someone", familyEvent =>
                {
                    var option = familyEvent.Potluck.Options.FirstOrDefault(existingOption =>
                        string.Equals(existingOption.Id, optionId, StringComparison.OrdinalIgnoreCase))
                        ?? throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "Option not found.");

                    if (option.IsBuiltIn)
                    {
                        throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "Built-in options cannot be removed.");
                    }

                    familyEvent.Potluck.Options.Remove(option);
                    foreach (var participant in familyEvent.Potluck.Participants)
                    {
                        participant.Ratings.Remove(option.Id);
                    }

                    return $"removed the potluck theme {Quote(option.Label)}.";
                });
            });
        }

        [Function("AskFamilyEventQuestion")]
        public async Task<HttpResponseData> AskQuestion(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/questions")] HttpRequestData httpRequest,
            string id)
        {
            var request = await httpRequest.ReadFromJsonAsync<FamilyEventPostRequest>();
            return await Run(httpRequest, "ask family event question", () =>
            {
                var name = RequireName(request?.Name);
                var text = RequirePostText(request!.Text, "Question");

                return MutateAndNotifyAsync(id, name, familyEvent =>
                {
                    familyEvent.Questions.Add(new FamilyEventQuestion
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        AskedBy = name,
                        Text = text,
                        AskedAt = WashingtonScheduleTime.Now
                    });
                    return $"asked: {Quote(text)}";
                });
            });
        }

        [Function("AnswerFamilyEventQuestion")]
        public async Task<HttpResponseData> AnswerQuestion(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/questions/{questionId}/answers")] HttpRequestData httpRequest,
            string id,
            string questionId)
        {
            var request = await httpRequest.ReadFromJsonAsync<FamilyEventPostRequest>();
            return await Run(httpRequest, "answer family event question", () =>
            {
                var name = RequireName(request?.Name);
                var text = RequirePostText(request!.Text, "Answer");

                return MutateAndNotifyAsync(id, name, familyEvent =>
                {
                    var question = FindQuestion(familyEvent, questionId);
                    question.Answers.Add(new FamilyEventAnswer
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        AnsweredBy = name,
                        Text = text,
                        AnsweredAt = WashingtonScheduleTime.Now
                    });
                    return $"answered {question.AskedBy}'s question {Quote(question.Text)}: {Quote(text)}";
                });
            });
        }

        [Function("DeleteFamilyEventQuestion")]
        public async Task<HttpResponseData> DeleteQuestion(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/questions/{questionId}/delete")] HttpRequestData httpRequest,
            string id,
            string questionId)
        {
            var request = await httpRequest.ReadFromJsonAsync<FamilyEventDeletePostRequest>();
            return await Run(httpRequest, "delete family event question", () =>
                MutateAndNotifyAsync(id, request?.Name ?? "", familyEvent =>
                {
                    var question = FindQuestion(familyEvent, questionId);
                    RequirePostOwner(question.AskedBy, request?.Name);
                    familyEvent.Questions.Remove(question);
                    return $"deleted {question.AskedBy}'s question {Quote(question.Text)}.";
                }));
        }

        [Function("DeleteFamilyEventAnswer")]
        public async Task<HttpResponseData> DeleteAnswer(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/questions/{questionId}/answers/{answerId}/delete")] HttpRequestData httpRequest,
            string id,
            string questionId,
            string answerId)
        {
            var request = await httpRequest.ReadFromJsonAsync<FamilyEventDeletePostRequest>();
            return await Run(httpRequest, "delete family event answer", () =>
                MutateAndNotifyAsync(id, request?.Name ?? "", familyEvent =>
                {
                    var question = FindQuestion(familyEvent, questionId);
                    var answer = question.Answers.FirstOrDefault(existingAnswer => string.Equals(existingAnswer.Id, answerId, StringComparison.OrdinalIgnoreCase))
                        ?? throw new FamilyEventsHttpException(HttpStatusCode.NotFound, "Answer not found.");
                    RequirePostOwner(answer.AnsweredBy, request?.Name);
                    question.Answers.Remove(answer);
                    return $"deleted {answer.AnsweredBy}'s answer {Quote(answer.Text)}.";
                }));
        }

        [Function("SaveFamilyEventRsvp")]
        public async Task<HttpResponseData> SaveRsvp(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/rsvp")] HttpRequestData httpRequest,
            string id)
        {
            var request = await httpRequest.ReadFromJsonAsync<FamilyEventRsvpRequest>();
            return await Run(httpRequest, "save family event RSVP", () =>
            {
                var name = RequireName(request?.Name);
                var status = request!.Status?.Trim().ToLowerInvariant() ?? "";
                if (status.Length > 0 && !FamilyRsvpStatus.IsValid(status))
                {
                    throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "RSVP must be going, maybe, or no.");
                }

                if (request.Headcount is < 0 or > 50)
                {
                    throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "Headcount must be between 0 and 50.");
                }

                var attendees = CleanAttendees(request.Attendees, request.Headcount ?? 0);

                return MutateAndNotifyAsync(id, name, familyEvent =>
                {
                    if (!familyEvent.RsvpEnabled)
                    {
                        throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "This event is not taking RSVPs.");
                    }

                    var existing = familyEvent.Rsvps.FirstOrDefault(rsvp => string.Equals(rsvp.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (status.Length == 0)
                    {
                        if (existing != null)
                        {
                            familyEvent.Rsvps.Remove(existing);
                        }

                        return "removed their RSVP.";
                    }

                    if (existing == null)
                    {
                        existing = new FamilyEventRsvp { Name = name };
                        familyEvent.Rsvps.Add(existing);
                    }

                    existing.Status = status;
                    existing.Headcount = request.Headcount is > 0 ? request.Headcount : null;
                    existing.Attendees = attendees;
                    existing.Note = Truncate(request.Note?.Trim() ?? "", MaxTextLength);
                    existing.UpdatedAt = WashingtonScheduleTime.Now;

                    var summary = FamilyEventRules.SummarizeRsvps(familyEvent.Rsvps);
                    var answer = status switch
                    {
                        FamilyRsvpStatus.Going => "We're in!",
                        FamilyRsvpStatus.Maybe => "Maybe",
                        _ => "Can't make it"
                    };
                    var who = string.Join(", ", attendees
                        .Select(attendee => string.Join(" ", new[] { attendee.Name, FamilyAttendeeAge.Label(attendee.Age) }.Where(part => part.Length > 0)))
                        .Where(description => description.Length > 0));
                    var people = existing.Headcount is int count
                        ? $" ({count} {(count == 1 ? "person" : "people")}{(who.Length > 0 ? ": " + who : "")})"
                        : "";
                    var note = existing.Note.Length > 0 ? $" Note: {Quote(existing.Note)}" : "";
                    var goal = familyEvent.RsvpGoal is int goalFamilies ? $" {summary.GoingFamilies} of {goalFamilies} families going." : "";
                    return $"RSVP'd: {answer}{people}.{note}{goal}";
                });
            });
        }

        [Function("SaveFamilyEventSignup")]
        public async Task<HttpResponseData> SaveSignup(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/signups/{listId}")] HttpRequestData httpRequest,
            string id,
            string listId)
        {
            var request = await httpRequest.ReadFromJsonAsync<FamilyEventPostRequest>();
            return await Run(httpRequest, "save family event sign-up", () =>
            {
                var name = RequireName(request?.Name);
                var text = Truncate(request!.Text?.Trim() ?? "", MaxTextLength);

                return MutateAndNotifyAsync(id, name, familyEvent =>
                {
                    var list = familyEvent.SignupLists.FirstOrDefault(signupList => string.Equals(signupList.Id, listId, StringComparison.OrdinalIgnoreCase))
                        ?? throw new FamilyEventsHttpException(HttpStatusCode.NotFound, "Sign-up list not found.");

                    var entry = list.Entries.FirstOrDefault(existingEntry => string.Equals(existingEntry.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (text.Length == 0)
                    {
                        if (entry != null)
                        {
                            list.Entries.Remove(entry);
                        }

                        return $"removed their sign-up for {Quote(list.Title)}.";
                    }

                    if (entry == null)
                    {
                        entry = new FamilyEventSignupEntry { Name = name };
                        list.Entries.Add(entry);
                    }

                    entry.Text = text;
                    entry.UpdatedAt = WashingtonScheduleTime.Now;
                    return $"signed up for {Quote(list.Title)}: {Quote(text)}";
                });
            });
        }

        private static void RequireThemeVoting(FamilyEvent familyEvent)
        {
            if (!familyEvent.ThemeVotingEnabled)
            {
                throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "Theme voting is turned off for this event.");
            }
        }

        /// <summary>Keeps entries for lists whose title is unchanged; lists left out of the request are removed.</summary>
        private static List<FamilyEventSignupList> MergeSignupLists(
            List<FamilyEventSignupList> existing,
            List<FamilyEventSignupList>? requested)
        {
            var merged = new List<FamilyEventSignupList>();
            foreach (var item in requested ?? new List<FamilyEventSignupList>())
            {
                var title = Truncate(item?.Title?.Trim() ?? "", MaxTitleLength);
                if (title.Length == 0 || merged.Any(mergedList => string.Equals(mergedList.Title, title, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var match = existing.FirstOrDefault(existingList => string.Equals(existingList.Title, title, StringComparison.OrdinalIgnoreCase));
                merged.Add(new FamilyEventSignupList
                {
                    Id = match?.Id ?? Guid.NewGuid().ToString("N"),
                    Title = title,
                    Prompt = Truncate(item!.Prompt?.Trim() ?? "", MaxTextLength),
                    Entries = match?.Entries ?? new List<FamilyEventSignupEntry>()
                });
            }

            return merged;
        }

        private static FamilyEventQuestion FindQuestion(FamilyEvent familyEvent, string questionId) =>
            familyEvent.Questions.FirstOrDefault(question => string.Equals(question.Id, questionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new FamilyEventsHttpException(HttpStatusCode.NotFound, "Question not found.");

        private static void RequirePostOwner(string author, string? requestedBy)
        {
            if (!FamilyEventRules.CanDeletePost(author, requestedBy ?? ""))
            {
                throw new FamilyEventsHttpException(HttpStatusCode.Forbidden, "Only the person who wrote this (or Luke) can delete it.");
            }
        }

        private static string RequirePostText(string? text, string label)
        {
            var trimmed = Truncate(text?.Trim() ?? "", MaxPostLength);
            if (trimmed.Length == 0)
            {
                throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, $"{label} text is required.");
            }

            return trimmed;
        }

        /// <summary>
        /// Applies a change and, once it is saved, notifies Luke. <paramref name="change"/> returns the summary
        /// shown after the actor's name (e.g. "asked: ..."); it may run more than once if a save is retried.
        /// </summary>
        private async Task<FamilyEventsData> MutateAndNotifyAsync(string id, string actor, Func<FamilyEvent, string> change)
        {
            string summary = "";
            string title = "";
            var data = await MutateEventAsync(id, familyEvent =>
            {
                summary = change(familyEvent);
                title = familyEvent.Title;
            });

            var displayActor = string.Equals(actor.Trim(), ScheduleSubmitters.LukeAuthCode, StringComparison.Ordinal)
                ? "Luke"
                : actor.Trim();

            await NotificationDispatcher.NotifyChangeAsync(_logger, displayActor, new NotificationMessage
            {
                Title = title,
                Body = $"{displayActor} {summary}",
                ClickUrl = NotificationDispatcher.SiteUrl($"FamilyEvents/{Uri.EscapeDataString(id)}")
            });

            return data;
        }

        private static string Quote(string text) =>
            text.Length <= 140 ? $"\"{text}\"" : $"\"{text.Substring(0, 140)}…\"";

        private static Task<FamilyEventsData> MutateEventAsync(string id, Action<FamilyEvent> mutate) =>
            FamilyEventsStorage.UpdateAsync(data =>
            {
                var familyEvent = FindUnlockedEvent(data, id);
                mutate(familyEvent);
                if (FamilyEventRules.IsLocked(familyEvent, WashingtonScheduleTime.PacificToday))
                {
                    throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, FamilyEventRules.LockedDateMessage);
                }

                familyEvent.LastModifiedAt = WashingtonScheduleTime.Now;
                return data;
            });

        private static FamilyEvent FindUnlockedEvent(FamilyEventsData data, string id)
        {
            var familyEvent = data.Events.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? throw new FamilyEventsHttpException(HttpStatusCode.NotFound, "Event not found.");

            if (FamilyEventRules.IsLocked(familyEvent, WashingtonScheduleTime.PacificToday))
            {
                throw new FamilyEventsHttpException(HttpStatusCode.Conflict, FamilyEventRules.LockedMessage);
            }

            return familyEvent;
        }

        private static PotluckParticipant GetOrAddParticipant(PotluckSurveyState potluck, string name)
        {
            var participant = potluck.Participants.FirstOrDefault(existingParticipant =>
                string.Equals(existingParticipant.Name, name, StringComparison.OrdinalIgnoreCase));

            if (participant == null)
            {
                participant = new PotluckParticipant { Name = name };
                potluck.Participants.Add(participant);
            }

            return participant;
        }

        private static void RequireLuke(string? submittedBy)
        {
            if (!string.Equals(submittedBy?.Trim(), ScheduleSubmitters.LukeAuthCode, StringComparison.Ordinal))
            {
                throw new FamilyEventsHttpException(HttpStatusCode.Forbidden, "Only Luke can create or edit events.");
            }
        }

        private static string RequireName(string? name)
        {
            var trimmed = Truncate(name?.Trim() ?? "", MaxTitleLength);
            if (trimmed.Length == 0)
            {
                throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "Name is required.");
            }

            return trimmed;
        }

        private static List<FamilyEventAttendee> CleanAttendees(List<FamilyEventAttendee>? attendees, int headcount)
        {
            var cleaned = new List<FamilyEventAttendee>();
            foreach (var attendee in (attendees ?? new List<FamilyEventAttendee>()).Take(headcount))
            {
                var age = attendee?.Age?.Trim().ToLowerInvariant() ?? "";
                if (!FamilyAttendeeAge.IsValid(age))
                {
                    throw new FamilyEventsHttpException(
                        HttpStatusCode.BadRequest,
                        $"Age must be \"adult\" or a kid's age from 0 to {FamilyAttendeeAge.MaxKidAge}.");
                }

                cleaned.Add(new FamilyEventAttendee
                {
                    Name = Truncate(attendee?.Name?.Trim() ?? "", MaxTitleLength),
                    Age = age
                });
            }

            return cleaned;
        }

        private static FamilyEvent ValidateDetails(FamilyEventDetailsRequest request)
        {
            var title = Truncate(request.Title?.Trim() ?? "", MaxTitleLength);
            if (title.Length == 0)
            {
                throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "Event title is required.");
            }

            if (request.Date == default)
            {
                throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "Event date is required.");
            }

            var date = request.Date.Date;
            var endDate = request.EndDate?.Date;
            if (endDate == date)
            {
                endDate = null;
            }

            if (endDate is { } end && (end < date || end > date.AddDays(FamilyEventRules.MaxEventDays)))
            {
                throw new FamilyEventsHttpException(
                    HttpStatusCode.BadRequest,
                    $"End date must be on or after the start date and within {FamilyEventRules.MaxEventDays} days.");
            }

            if (FamilyEventRules.IsLocked(endDate ?? date, WashingtonScheduleTime.PacificToday))
            {
                throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, FamilyEventRules.LockedDateMessage);
            }

            if (request.StartTime is { } start && (start < TimeSpan.Zero || start >= TimeSpan.FromDays(1)))
            {
                throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "Start time is not valid.");
            }

            if (request.RsvpGoal is < 1 or > 100)
            {
                throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "RSVP goal must be between 1 and 100 families.");
            }

            return new FamilyEvent
            {
                Title = title,
                Date = date,
                EndDate = endDate,
                StartTime = request.StartTime,
                Location = Truncate(request.Location?.Trim() ?? "", MaxTitleLength),
                Description = Truncate(request.Description?.Trim() ?? "", MaxDescriptionLength),
                ThemeVotingEnabled = request.ThemeVotingEnabled,
                BringPrompt = Truncate(request.BringPrompt?.Trim() ?? "", MaxTitleLength),
                RsvpEnabled = request.RsvpEnabled,
                RsvpGoal = request.RsvpEnabled ? request.RsvpGoal : null,
                RsvpDeadline = request.RsvpEnabled ? request.RsvpDeadline?.Date : null
            };
        }

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value.Substring(0, max);

        private async Task<HttpResponseData> Run(HttpRequestData httpRequest, string action, Func<Task<FamilyEventsData>> work)
        {
            try
            {
                var data = await work();
                var response = httpRequest.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new FamilyEventsResponse
                {
                    Events = data.Events,
                    Today = WashingtonScheduleTime.PacificToday
                });
                return response;
            }
            catch (FamilyEventsHttpException ex)
            {
                return await TextResponse(httpRequest, ex.Status, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to {Action}.", action);
                return await TextResponse(httpRequest, HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        private static async Task<HttpResponseData> TextResponse(HttpRequestData httpRequest, HttpStatusCode status, string message)
        {
            var response = httpRequest.CreateResponse(status);
            await response.WriteStringAsync(message);
            return response;
        }
    }
}
