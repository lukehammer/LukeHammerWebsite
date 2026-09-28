using BlazorApp.Shared;
using FluentAssertions;

namespace UnitTest
{
    public class ScheduleLocationSuggestionsTests
    {
        [Fact]
        public void Build_orders_by_frequency_and_includes_recent()
        {
            var events = new[]
            {
                new Event { Location = "Woodland HS", LastModifiedAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(-7)) },
                new Event { Location = "Woodland HS" },
                new Event { Location = "Kelso HS", LastModifiedAt = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.FromHours(-7)) },
                new Event { Location = "RORC — Field 5A" },
            };

            var suggestions = ScheduleLocationSuggestions.Build(events);

            suggestions.Should().Contain("Woodland HS");
            suggestions.Should().Contain("Kelso HS");
            suggestions[0].Should().Be("Woodland HS");
        }

        [Fact]
        public void BuildAutocompleteList_returns_distinct_locations_by_frequency()
        {
            var events = new[]
            {
                new Event { Location = "Woodland HS" },
                new Event { Location = "Woodland HS" },
                new Event { Location = "Kelso HS" },
            };

            ScheduleLocationSuggestions.BuildAutocompleteList(events)
                .Should().Equal("Woodland HS", "Kelso HS");
        }
    }
}
