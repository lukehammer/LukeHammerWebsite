using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlazorApp.Shared
{
    public class Event
    {
        public DateTime DateTime { get; set; }
        public string Name { get; set; }
        public string Location { get; set; }

        public static Event CreateBaseBallGame(string dateTime, string name)
        {
            return new Event { DateTime = DateTime.Parse(dateTime), Name = name, Location = "H.B. Fuller Company Park" };
        }
        public static Event CreateBaseBallGame(string dateTime, string name, string location)
        {

            return new Event { DateTime = DateTime.Parse(dateTime), Name = name, Location = location};
            
        }
    }

    public class BaseBallGames
    {
        public static List<Event> Games => GetGames();
        public static IEnumerable<Event> UpComingGames => Games.Where(x => x.DateTime > DateTime.Now.AddDays(-1));

        private static List<Event> GetGames()
        {

            var r = "vs.Bananas - Ridgefeild LL";
            var s = "vs.Bananas - Salmon Creek Little League";

            var games = new List<Event>
            {
                Event.CreateBaseBallGame("9/29/2024 13:00", r, "Abrams Park Ridgefeild"),
                Event.CreateBaseBallGame("10/6/2024 13:00", s, "Gasier Middle School"),
                Event.CreateBaseBallGame("10/20/2024 13:00", r, "Abrams Park Ridgefeild"),
                Event.CreateBaseBallGame("10/27/2024 13:00", s, "Last Game Gaiser")
            };

            return games;
        }
    }
}
