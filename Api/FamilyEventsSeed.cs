using System;
using System.Collections.Generic;
using BlazorApp.Shared;

namespace ApiIsolated
{
    /// <summary>Built-in events added to storage once; after that they are edited like any other event.</summary>
    internal static class FamilyEventsSeed
    {
        public static IReadOnlyList<FamilyEvent> Create() => new[]
        {
            new FamilyEvent
            {
                Id = "christmas-party-2026",
                Title = "Christmas Party",
                Date = new DateTime(2026, 12, 12),
                StartTime = new TimeSpan(17, 0, 0),
                Description =
                    "Hello Family! I'm going to host the Christmas party at my house again.\n\n" +
                    "It's a potluck and White Santa gift exchange. I'm making ham and fresh green beans this year, " +
                    "so I'm looking for any dishes that go with that meal. Go ahead and post what you'd like to bring!",
                ThemeVotingEnabled = false,
                BringPrompt = "What dish are you bringing to go with the ham and green beans?",
                RsvpEnabled = true,
                SignupLists = new List<FamilyEventSignupList>
                {
                    new FamilyEventSignupList
                    {
                        Id = "white-santa",
                        Title = "White Santa gift exchange",
                        Prompt = "Joining the gift exchange? Add your name and how many gifts your family is bringing."
                    }
                }
            },
            new FamilyEvent
            {
                Id = "mayfield-lake-camping-2027",
                Title = "Mayfield Lake Group Camping",
                Date = new DateTime(2027, 7, 15),
                EndDate = new DateTime(2027, 7, 18),
                Location = "Mayfield Lake group campsite",
                Description =
                    "I booked the group camping site at Mayfield Lake! Yea! Couldn't believe I got it.\n\n" +
                    "We have July 15, 16 and 17, and I can still add the 18th until October 20. We need at least 6 families " +
                    "to commit. Leneigh, Vickie and us are three, so if three more want to come I will book it. " +
                    "I need to know by October 20!\n\n" +
                    "New family members are welcome. With the group site we have plenty of room. It will cost $50 a day " +
                    "or less for each family. No one has to pay now; I just need to know who wants to go. Around May " +
                    "I'll let everyone know how much they owe.\n\n" +
                    "We're planning a potluck Saturday night and omelets in a bag Saturday morning. Since we have a big " +
                    "covered area we can do more together, so we'd love to have skits, maybe one from each family! " +
                    "A skit, joke, poem, or song. Show us a part of you and make us laugh 😃\n\n" +
                    "We have our own bathroom and a swing set. There's a great swimming area, and 5 of us have kayaks " +
                    "the kids can use. Let the fun begin!",
                ThemeVotingEnabled = false,
                BringPrompt = "What are you bringing to the Saturday night potluck?",
                RsvpEnabled = true,
                RsvpGoal = 6,
                RsvpDeadline = new DateTime(2026, 10, 20),
                Rsvps = new List<FamilyEventRsvp>
                {
                    new FamilyEventRsvp { Name = "Leneigh", Status = FamilyRsvpStatus.Going, UpdatedAt = WashingtonScheduleTime.Now },
                    new FamilyEventRsvp { Name = "Vickie", Status = FamilyRsvpStatus.Going, UpdatedAt = WashingtonScheduleTime.Now }
                },
                SignupLists = new List<FamilyEventSignupList>
                {
                    new FamilyEventSignupList
                    {
                        Id = "skits",
                        Title = "Skits & talent",
                        Prompt = "What is your family doing? A skit, joke, poem, or song!"
                    },
                    new FamilyEventSignupList
                    {
                        Id = "omelets",
                        Title = "Omelets in a bag (Saturday breakfast)",
                        Prompt = "What omelet fixings can you bring? Eggs, cheese, ham, peppers..."
                    }
                }
            }
        };
    }
}
