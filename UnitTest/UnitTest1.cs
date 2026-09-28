using FluentAssertions;



namespace UnitTest

{

    public class UnitTest1

    {

        private static IReadOnlyList<BlazorApp.Shared.Event> SeedEvents()

        {

            var seedPath = Path.Combine(AppContext.BaseDirectory, "data", "sports-schedules.json");

            var json = File.ReadAllText(seedPath);

            return BlazorApp.Shared.SportsSchedules.ParseJson(json).Events;

        }



        [Fact]

        public void Baseball_schedule_is_upcoming_only_and_ordered()

        {

            var contests = BlazorApp.Shared.SportsSchedules.GetUpcoming(
                SeedEvents(), BlazorApp.Shared.SportNames.Baseball, "Levi");



            contests.Should().NotBeEmpty();

            contests.Should().OnlyContain(e => e.Date >= DateTime.Today);

            contests.Should().OnlyContain(e => e.Sport == BlazorApp.Shared.SportNames.Baseball);

            contests.Should().OnlyContain(e => e.Kids.Count == 1 && e.Kids[0] == "Levi");

            contests.Select(e => e.Date).Should().BeInAscendingOrder();

        }



        [Fact]

        public void WoodlandFootball_Jv_schedule_is_upcoming_only_and_ordered()

        {

            var games = BlazorApp.Shared.SportsSchedules.GetUpcoming(SeedEvents(), BlazorApp.Shared.SportNames.Football);



            games.Should().OnlyContain(e => e.Date >= DateTime.Today);

            games.Should().OnlyContain(e => e.Sport == BlazorApp.Shared.SportNames.Football);

            games.Should().OnlyContain(e => e.Kids.Count == 1 && e.Kids[0] == "Choen");

            games.Select(e => e.Date).Should().BeInAscendingOrder();

            games.Should().OnlyContain(e => e.Name.StartsWith("vs "));

        }



        [Fact]

        public void BainSoccer_schedule_is_upcoming_only_and_ordered()

        {

            var games = BlazorApp.Shared.SportsSchedules.GetUpcoming(SeedEvents(), BlazorApp.Shared.SportNames.Soccer);



            games.Should().OnlyContain(e => e.Date >= DateTime.Today);

            games.Should().OnlyContain(e => e.Sport == BlazorApp.Shared.SportNames.Soccer);

            games.Should().OnlyContain(e => e.Kids.Count == 1 && e.Kids[0] == "June");

            games.Select(e => e.Date).Should().BeInAscendingOrder();

            games.Should().OnlyContain(e => e.Name.StartsWith("vs ") || e.Name.StartsWith("@ "));

            games.Should().OnlyContain(e => e.StartTime.HasValue);

        }



        [Fact]

        public void Combined_upcoming_is_ordered_by_date_then_time()

        {

            var combined = BlazorApp.Shared.SportsSchedules.GetUpcoming(SeedEvents());



            combined.Should().NotBeEmpty();

            combined.Should().OnlyContain(e => e.Date >= DateTime.Today);

            combined.Should().OnlyContain(e => e.Kids.Count > 0);



            var ordered = combined

                .OrderBy(e => e.Date)

                .ThenBy(e => e.StartTime ?? TimeSpan.MaxValue)

                .ToList();



            combined.Should().Equal(ordered);

        }



        [Fact]

        public void All_scheduled_entries_have_sport_and_kid_set()

        {

            var all = SeedEvents();



            all.Should().NotBeEmpty();

            all.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.Sport));

            all.Should().OnlyContain(e => e.Kids.Count > 0);

        }

    }

}

