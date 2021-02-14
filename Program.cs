using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using XBS.Core;
using XBS.Core.Parser;

namespace XBSDataScraper
{
    class Program
    {

        // The target season for which data will primarily be pulled
        static string TARGET_SEASON = "30";
        // If team statistics should be pulled
        static bool PULL_TEAM_STATISTICS = true;


        // Stats to pull from 
        static int[] PULL_TEAM_STATISTCS_SEASON = { 30 };
        // Helper item
        static int MIN_SEASON_THRESHOLD = PULL_TEAM_STATISTCS_SEASON.Min();

        // Eager load mode attempts to pull all stats for a player by season immediately
        // Most of the other projects depend on leaving this on.
        static bool EAGER_LOAD_DATA_MODE = true;

        public static void Main(string[] args)
        {

            if (args.Length != 0)
            {
                throw new NotImplementedException("Command line arguments not yet supported");
            }

            var parser = new Parser(
                targetSeason: "30",
                seasonStatsToPull: new int[] { 30 }
                );

            var players = parser.GetPlayers();
            var statsTeams = parser.GetTeams();

            using (FileStream file = new FileStream("c:\\testoutputdraft\\teams_stats.xml", FileMode.Create, FileAccess.Write))
            {
                System.Xml.Serialization.XmlSerializer ser = new System.Xml.Serialization.XmlSerializer(typeof(List<StatsTeam>));
                ser.Serialize(file, statsTeams);
            }

            using (FileStream file = new FileStream("c:\\testoutputdraft\\players_staging.xml", FileMode.Create, FileAccess.Write))
            {
                System.Xml.Serialization.XmlSerializer ser = new System.Xml.Serialization.XmlSerializer(typeof(List<Player>));
                ser.Serialize(file, players);
            }
        }


    }
}
