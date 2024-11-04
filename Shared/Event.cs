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
                Event.CreateContest("11/7/2024 17:00", "Discouvery", "Discouvery Commons"),
                Event.CreateContest("11/13/2024 17:00", "Gaiser", "Gasier Middle School gym"),
                Event.CreateContest("11/20/2024 17:00", "Mac", "Thomas Jefferson Gym"),
                Event.CreateContest("12/4/2024 17:00", "Jason Lee", "Thomas Jefferson Gym"),
                Event.CreateContest("12/11/2024 17:00", "Alki", "Thomas Jefferson Gym"),
                Event.CreateContest("12/14/2024 9:00", "VPS Varsity Tournament" , "Hudson's Bay High School Gym")
            };

            return games;
        }
    }
}
