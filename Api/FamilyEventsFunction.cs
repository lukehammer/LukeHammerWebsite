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
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "family-events")] HttpRequestData req) =>
            Run(req, "load family events", async () => await FamilyEventsStorage.LoadAsync());

        [Function("CreateFamilyEvent")]
        public async Task<HttpResponseData> Create(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events")] HttpRequestData req)
        {
            var request = await req.ReadFromJsonAsync<FamilyEventDetailsRequest>();
            return await Run(req, "create family event", () =>
            {
                RequireLuke(request?.SubmittedBy);
                var details = ValidateDetails(request!);
                details.Id = Guid.NewGuid().ToString("N");

                return FamilyEventsStorage.UpdateAsync(data =>
                {
                    var potluck = new PotluckSurveyState();
                    foreach (var label in (request!.ThemeOptions ?? new List<string>())
                                 .Select(t => t?.Trim() ?? "")
                                 .Where(t => t.Length > 0)
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
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "family-events/{id}")] HttpRequestData req,
            string id)
        {
            var request = await req.ReadFromJsonAsync<FamilyEventDetailsRequest>();
            return await Run(req, "update family event", () =>
            {
                RequireLuke(request?.SubmittedBy);
                var details = ValidateDetails(request!);

                return MutateEventAsync(id, evt =>
                {
                    evt.Title = details.Title;
                    evt.Date = details.Date;
                    evt.EndDate = details.EndDate;
                    evt.StartTime = details.StartTime;
                    evt.Location = details.Location;
                    evt.Description = details.Description;
                    evt.ThemeVotingEnabled = details.ThemeVotingEnabled;
                    evt.BringPrompt = details.BringPrompt;
                    evt.RsvpEnabled = details.RsvpEnabled;
                    evt.RsvpGoal = details.RsvpGoal;
                    evt.RsvpDeadline = details.RsvpDeadline;
                    evt.SignupLists = MergeSignupLists(evt.SignupLists, request!.SignupLists);
                });
            });
        }

        [Function("DeleteFamilyEvent")]
        public Task<HttpResponseData> Delete(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "family-events/{id}")] HttpRequestData req,
            string id) =>
            Run(req, "delete family event", () =>
            {
                RequireLuke(HttpUtility.ParseQueryString(req.Url.Query)["submittedBy"]);

                return FamilyEventsStorage.UpdateAsync(data =>
                {
                    var evt = FindUnlockedEvent(data, id);
                    data.Events.Remove(evt);
                    return data;
                });
            });

        [Function("SaveFamilyEventParticipant")]
        public async Task<HttpResponseData> SaveParticipant(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/participant")] HttpRequestData req,
            string id)
        {
            var request = await req.ReadFromJsonAsync<PotluckParticipantRequest>();
            return await Run(req, "save family event participant", () =>
            {
                var name = RequireName(request?.Name);
                var bringing = Truncate(request!.Bringing?.Trim() ?? "", MaxTextLength);

                return MutateAndNotifyAsync(id, name, evt =>
                {
                    var participant = GetOrAddParticipant(evt.Potluck, name);
                    participant.Bringing = bringing;
                    participant.SubmittedAt = DateTime.UtcNow;
                    return bringing.Length == 0 ? "cleared what they're bringing." : $"is bringing {Quote(bringing)}.";
                });
            });
        }

        [Function("RateFamilyEventOption")]
        public async Task<HttpResponseData> RateOption(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/rate")] HttpRequestData req,
            string id)
        {
            var request = await req.ReadFromJsonAsync<PotluckRateRequest>();
            return await Run(req, "save family event vote", () =>
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

                return MutateAndNotifyAsync(id, name, evt =>
                {
                    RequireThemeVoting(evt);
                    var option = evt.Potluck.Options.FirstOrDefault(o => string.Equals(o.Id, optionId, StringComparison.OrdinalIgnoreCase))
                        ?? throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "Option not found.");

                    var participant = GetOrAddParticipant(evt.Potluck, name);
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
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/options")] HttpRequestData req,
            string id)
        {
            var request = await req.ReadFromJsonAsync<PotluckAddOptionRequest>();
            return await Run(req, "add family event option", () =>
            {
                var label = Truncate(request?.Label?.Trim() ?? "", MaxTitleLength);
                if (label.Length == 0)
                {
                    throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "Option label is required.");
                }

                var optionId = PotluckOptionIds.ToCustomOptionId(label);

                var createdBy = request!.CreatedBy?.Trim() ?? "";
                return MutateAndNotifyAsync(id, createdBy.Length == 0 ? "Someone" : createdBy, evt =>
                {
                    RequireThemeVoting(evt);
                    if (evt.Potluck.Options.Any(o => string.Equals(o.Id, optionId, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "That option already exists.");
                    }

                    evt.Potluck.Options.Add(new PotluckOptionDefinition
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
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/options/remove")] HttpRequestData req,
            string id)
        {
            var request = await req.ReadFromJsonAsync<PotluckRemoveOptionRequest>();
            return await Run(req, "remove family event option", () =>
            {
                if (string.IsNullOrWhiteSpace(request?.OptionId))
                {
                    throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "Option id is required.");
                }

                var optionId = request!.OptionId.Trim();

                return MutateAndNotifyAsync(id, "Someone", evt =>
                {
                    var option = evt.Potluck.Options.FirstOrDefault(o =>
                        string.Equals(o.Id, optionId, StringComparison.OrdinalIgnoreCase))
                        ?? throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "Option not found.");

                    if (option.IsBuiltIn)
                    {
                        throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "Built-in options cannot be removed.");
                    }

                    evt.Potluck.Options.Remove(option);
                    foreach (var participant in evt.Potluck.Participants)
                    {
                        participant.Ratings.Remove(option.Id);
                    }

                    return $"removed the potluck theme {Quote(option.Label)}.";
                });
            });
        }

        [Function("AskFamilyEventQuestion")]
        public async Task<HttpResponseData> AskQuestion(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/questions")] HttpRequestData req,
            string id)
        {
            var request = await req.ReadFromJsonAsync<FamilyEventPostRequest>();
            return await Run(req, "ask family event question", () =>
            {
                var name = RequireName(request?.Name);
                var text = RequirePostText(request!.Text, "Question");

                return MutateAndNotifyAsync(id, name, evt =>
                {
                    evt.Questions.Add(new FamilyEventQuestion
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
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/questions/{questionId}/answers")] HttpRequestData req,
            string id,
            string questionId)
        {
            var request = await req.ReadFromJsonAsync<FamilyEventPostRequest>();
            return await Run(req, "answer family event question", () =>
            {
                var name = RequireName(request?.Name);
                var text = RequirePostText(request!.Text, "Answer");

                return MutateAndNotifyAsync(id, name, evt =>
                {
                    var question = FindQuestion(evt, questionId);
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
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/questions/{questionId}/delete")] HttpRequestData req,
            string id,
            string questionId)
        {
            var request = await req.ReadFromJsonAsync<FamilyEventDeletePostRequest>();
            return await Run(req, "delete family event question", () =>
                MutateAndNotifyAsync(id, request?.Name ?? "", evt =>
                {
                    var question = FindQuestion(evt, questionId);
                    RequirePostOwner(question.AskedBy, request?.Name);
                    evt.Questions.Remove(question);
                    return $"deleted {question.AskedBy}'s question {Quote(question.Text)}.";
                }));
        }

        [Function("DeleteFamilyEventAnswer")]
        public async Task<HttpResponseData> DeleteAnswer(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/questions/{questionId}/answers/{answerId}/delete")] HttpRequestData req,
            string id,
            string questionId,
            string answerId)
        {
            var request = await req.ReadFromJsonAsync<FamilyEventDeletePostRequest>();
            return await Run(req, "delete family event answer", () =>
                MutateAndNotifyAsync(id, request?.Name ?? "", evt =>
                {
                    var question = FindQuestion(evt, questionId);
                    var answer = question.Answers.FirstOrDefault(a => string.Equals(a.Id, answerId, StringComparison.OrdinalIgnoreCase))
                        ?? throw new FamilyEventsHttpException(HttpStatusCode.NotFound, "Answer not found.");
                    RequirePostOwner(answer.AnsweredBy, request?.Name);
                    question.Answers.Remove(answer);
                    return $"deleted {answer.AnsweredBy}'s answer {Quote(answer.Text)}.";
                }));
        }

        [Function("SaveFamilyEventRsvp")]
        public async Task<HttpResponseData> SaveRsvp(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/rsvp")] HttpRequestData req,
            string id)
        {
            var request = await req.ReadFromJsonAsync<FamilyEventRsvpRequest>();
            return await Run(req, "save family event RSVP", () =>
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

                return MutateAndNotifyAsync(id, name, evt =>
                {
                    if (!evt.RsvpEnabled)
                    {
                        throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, "This event is not taking RSVPs.");
                    }

                    var existing = evt.Rsvps.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (status.Length == 0)
                    {
                        if (existing != null)
                        {
                            evt.Rsvps.Remove(existing);
                        }

                        return "removed their RSVP.";
                    }

                    if (existing == null)
                    {
                        existing = new FamilyEventRsvp { Name = name };
                        evt.Rsvps.Add(existing);
                    }

                    existing.Status = status;
                    existing.Headcount = request.Headcount is > 0 ? request.Headcount : null;
                    existing.Note = Truncate(request.Note?.Trim() ?? "", MaxTextLength);
                    existing.UpdatedAt = WashingtonScheduleTime.Now;

                    var summary = FamilyEventRules.SummarizeRsvps(evt.Rsvps);
                    var answer = status switch
                    {
                        FamilyRsvpStatus.Going => "We're in!",
                        FamilyRsvpStatus.Maybe => "Maybe",
                        _ => "Can't make it"
                    };
                    var people = existing.Headcount is int count ? $" ({count} {(count == 1 ? "person" : "people")})" : "";
                    var note = existing.Note.Length > 0 ? $" Note: {Quote(existing.Note)}" : "";
                    var goal = evt.RsvpGoal is int g ? $" {summary.GoingFamilies} of {g} families going." : "";
                    return $"RSVP'd: {answer}{people}.{note}{goal}";
                });
            });
        }

        [Function("SaveFamilyEventSignup")]
        public async Task<HttpResponseData> SaveSignup(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "family-events/{id}/signups/{listId}")] HttpRequestData req,
            string id,
            string listId)
        {
            var request = await req.ReadFromJsonAsync<FamilyEventPostRequest>();
            return await Run(req, "save family event sign-up", () =>
            {
                var name = RequireName(request?.Name);
                var text = Truncate(request!.Text?.Trim() ?? "", MaxTextLength);

                return MutateAndNotifyAsync(id, name, evt =>
                {
                    var list = evt.SignupLists.FirstOrDefault(l => string.Equals(l.Id, listId, StringComparison.OrdinalIgnoreCase))
                        ?? throw new FamilyEventsHttpException(HttpStatusCode.NotFound, "Sign-up list not found.");

                    var entry = list.Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
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

        private static void RequireThemeVoting(FamilyEvent evt)
        {
            if (!evt.ThemeVotingEnabled)
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
                if (title.Length == 0 || merged.Any(l => string.Equals(l.Title, title, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var match = existing.FirstOrDefault(l => string.Equals(l.Title, title, StringComparison.OrdinalIgnoreCase));
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

        private static FamilyEventQuestion FindQuestion(FamilyEvent evt, string questionId) =>
            evt.Questions.FirstOrDefault(q => string.Equals(q.Id, questionId, StringComparison.OrdinalIgnoreCase))
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
            var data = await MutateEventAsync(id, evt =>
            {
                summary = change(evt);
                title = evt.Title;
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
                var evt = FindUnlockedEvent(data, id);
                mutate(evt);
                if (FamilyEventRules.IsLocked(evt, WashingtonScheduleTime.PacificToday))
                {
                    throw new FamilyEventsHttpException(HttpStatusCode.BadRequest, FamilyEventRules.LockedDateMessage);
                }

                evt.LastModifiedAt = WashingtonScheduleTime.Now;
                return data;
            });

        private static FamilyEvent FindUnlockedEvent(FamilyEventsData data, string id)
        {
            var evt = data.Events.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? throw new FamilyEventsHttpException(HttpStatusCode.NotFound, "Event not found.");

            if (FamilyEventRules.IsLocked(evt, WashingtonScheduleTime.PacificToday))
            {
                throw new FamilyEventsHttpException(HttpStatusCode.Conflict, FamilyEventRules.LockedMessage);
            }

            return evt;
        }

        private static PotluckParticipant GetOrAddParticipant(PotluckSurveyState potluck, string name)
        {
            var participant = potluck.Participants.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

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

        private async Task<HttpResponseData> Run(HttpRequestData req, string action, Func<Task<FamilyEventsData>> work)
        {
            try
            {
                var data = await work();
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new FamilyEventsResponse
                {
                    Events = data.Events,
                    Today = WashingtonScheduleTime.PacificToday
                });
                return response;
            }
            catch (FamilyEventsHttpException ex)
            {
                return await TextResponse(req, ex.Status, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to {Action}.", action);
                return await TextResponse(req, HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        private static async Task<HttpResponseData> TextResponse(HttpRequestData req, HttpStatusCode status, string message)
        {
            var response = req.CreateResponse(status);
            await response.WriteStringAsync(message);
            return response;
        }
    }
}
