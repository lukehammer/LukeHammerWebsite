using FluentAssertions;

namespace UnitTest
{
    public class UnitTest1
    {
        [Fact]
        public void Test1()
        {

            BlazorApp.Shared.BaseBallGames.Games.Count().Should().Be(13);

            BlazorApp.Shared.BaseBallGames.UpComingGames.Count().Should().Be(10);
                        

        }
    }
}