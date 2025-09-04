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
        public static Event CreateContest(string dateTime, string name, string location)
        {

            return new Event { DateTime = DateTime.Parse(dateTime), Name = name, Location = location};
            
        }
    }

    public class Matches
    {
        public static List<Event> Contests => GetContest();
        public static IEnumerable<Event> UpComingContests => Contests.Where(x => x.DateTime > DateTime.Now.AddDays(-1));

        private static List<Event> GetContest()
        {

            var games = new List<Event>
            {
                Event.CreateContest("9/7/2025 14:00", "Jr. Hawks 12U Fallball 12U", "Heritage High School")
            };

            return games;
        }
    }
}
