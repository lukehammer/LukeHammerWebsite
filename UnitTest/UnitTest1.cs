using FluentAssertions;

namespace UnitTest
{
    public class UnitTest1
    {
        [Fact]
        public void Test1()
        {

            BlazorApp.Shared.Matches.Contests.Count().Should().Be(6);

            BlazorApp.Shared.Matches.UpComingContests.Count().Should().Be(6);
                        

        }
    }
}