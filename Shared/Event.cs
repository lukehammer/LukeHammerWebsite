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
    }

    public class BaseBallGames
    {
        public static List<Event> Games => GetGames();
        public static IEnumerable<Event> UpComingGames => Games.Where(x => x.DateTime > DateTime.Now.AddDays(-1));

        private static List<Event> GetGames()
        {

            var d = "vs.HDLL - NL Dodgers";
            var m = "vs.HDLL - NL Marlins";
            var p = "vs.HDLL - NL Phillies";




            var games = new List<Event>();
            games.Add(Event.CreateBaseBallGame("4/1/2024 10:00 am", "Should not display"));
            games.Add(Event.CreateBaseBallGame("4/20/2024 10:00 am", p));
            games.Add(Event.CreateBaseBallGame("4/25/2024 6:00 pm,", d));
            games.Add(Event.CreateBaseBallGame("4/29/2024 6:00 pm", p));
            games.Add(Event.CreateBaseBallGame("5/2/2024 1:00 pm", p));
            games.Add(Event.CreateBaseBallGame("5/4/2024 1:00 pm", "vs. SCLL Majors Giants 2024"));
            games.Add(Event.CreateBaseBallGame("5/6/2024 6:00 pm", m));
            games.Add(Event.CreateBaseBallGame("5/9/2024 6:00 pm", m));
            games.Add(Event.CreateBaseBallGame("5/11/2024 6:00 pm", d));
            games.Add(Event.CreateBaseBallGame("5/14/2024 6:00 pm", p));
            games.Add(Event.CreateBaseBallGame("5/18/2024 6:00 pm", d));
            games.Add(Event.CreateBaseBallGame("5/23/2024 6:00 pm", m));
            games.Add(Event.CreateBaseBallGame("5/30/2024 6:00 pm", p));

            return games;
        }
    }
}
