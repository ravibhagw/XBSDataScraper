using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Http;
using XBS.Core;
using HtmlAgilityPack;
using System.IO;
using System.Linq;

namespace XBSDataScraper
{
    public class Parser
    {
        public bool IsEagerMode { get; }
        public string TargetSeason { get; }
        public bool LoadTeamStatistics { get;  }

        public int[] SeasonStatsToPull { get; }

        private int _minimumSeasonThreshold; 

        public Parser(string targetSeason, int[] seasonStatsToPull, bool isEagerMode = true, bool loadTeamStats = true)
        {
            this.IsEagerMode = isEagerMode;
            this.TargetSeason = targetSeason;
            this.LoadTeamStatistics = loadTeamStats;
            this.SeasonStatsToPull = seasonStatsToPull;

            _minimumSeasonThreshold = SeasonStatsToPull.Min();

        }

        public void Parse()
        { 
            if (IsEagerMode)
            {
                Console.WriteLine("Fetching players in eager load mode.  This may take a while.");
            }


            var cookies = new CookieContainer();
            Login(cookies);
            string html;
            using (var httpClient = new HttpClient())
            {
                var values = new Dictionary<string, string>
                {
                    { "season", TargetSeason },
                    { "button", "view+season" }
                };

                var content = new System.Net.Http.FormUrlEncodedContent(values);
                var response = httpClient.PostAsync("http://xbox-sports.com/leagues/xbshl/index.php?mode=league_rosters", content).GetAwaiter().GetResult();
                html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }

            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(html);

            // Get all table_1a, the tables that has each player's data
            var playerTables = document.DocumentNode.Descendants().Where(x => x.HasClass("table_1a"));
            //var playerNames = document.DocumentNode.SelectNodes("//a[contains(@class,'color_2a') and contains(@href,'?mode=player')]").Where(x => !String.IsNullOrEmpty(x.InnerText));
            var rosterItems = document.DocumentNode.SelectNodes("//td[@colspan='2' and @align='center']"); // Selects the cell that has the details

            List<Player> players = new List<Player>();
            foreach (var player in rosterItems)
            {
                var detailTable = player.ChildNodes.Where(x => x.Name == "table").ToList(); // select the nested table with position header and stars
                var rows = detailTable[0].ChildNodes.Where(x => x.Name == "tr").ToList(); // select the rows of that table
                var preference = rows[1];


                var previousPlayerContainer = player;
                var currentPlayerContainer = player;
                int count = 0;
                // move up the tree until we find a link.
                while (currentPlayerContainer.Descendants().Where(x => x.Name == "a").Count() == 0)
                {
                    previousPlayerContainer = currentPlayerContainer;
                    currentPlayerContainer = currentPlayerContainer.ParentNode;
                    count++;

                    if (count > 15) //arbitrarily decide to quit
                        throw new Exception("Too high up the tree.  Probably couldn't find it.");
                }
                var numberofLinks = currentPlayerContainer.Descendants().Where(x => x.Name == "a").Count();
                // Sometimes we move too far up the tree and get an entire gd table.  With this logic if that happens
                // we can move back down the tree and then to a previous sibling that would have what we need
                var targetPlayerContainer = numberofLinks == 1 ? currentPlayerContainer : previousPlayerContainer.PreviousSibling.PreviousSibling;
                var playerCell = targetPlayerContainer.Descendants().Where(x => x.Name == "a").FirstOrDefault();

                string gamertag = playerCell.InnerText;


                var uri = playerCell.Attributes.SingleOrDefault(x => x.Name == "href");

                //uses order C, LW, RW, D, G
                var playerObj = new Player();

                playerObj.Gamertag = gamertag;
                playerObj.C = preference.ChildNodes[0].ChildNodes.Count(x => x.OuterHtml.Contains("icon_star_2"));
                playerObj.LW = preference.ChildNodes[1].ChildNodes.Count(x => x.OuterHtml.Contains("icon_star_2"));
                playerObj.RW = preference.ChildNodes[2].ChildNodes.Count(x => x.OuterHtml.Contains("icon_star_2"));
                playerObj.D = preference.ChildNodes[3].ChildNodes.Count(x => x.OuterHtml.Contains("icon_star_2"));
                playerObj.G = preference.ChildNodes[4].ChildNodes.Count(x => x.OuterHtml.Contains("icon_star_2"));
                if (uri != null)
                {
                    playerObj.ForumId = uri.DeEntitizeValue.Split('=')[2];
                }
                playerObj.PlayerAnalytics = new XBS.Core.PlayerAnalytics(playerObj.ForumId);
                if (IsEagerMode)
                {
                    PopulateWingData(playerObj.ForumId, playerObj.PlayerAnalytics);
                    PopulateCenterData(playerObj.ForumId, playerObj.PlayerAnalytics);
                    PopulateDefenseData(playerObj.ForumId, playerObj.PlayerAnalytics);
                    PopulateGoalieData(playerObj.ForumId, playerObj.PlayerAnalytics);
                }
                players.Add(playerObj);


            }

            using (FileStream file = new FileStream("c:\\testoutputdraft\\players_staging.xml", FileMode.Create, FileAccess.Write))
            {
                System.Xml.Serialization.XmlSerializer ser = new System.Xml.Serialization.XmlSerializer(typeof(List<Player>));
                ser.Serialize(file, players);
            }


            // Pulls team stats
            if (LoadTeamStatistics)
            {
                string teamHtml;
                List<StatsTeam> statsTeams = new List<StatsTeam>();

                foreach (var seasonId in SeasonStatsToPull)
                {
                    using (var httpClient = new HttpClient())
                    {
                        var values = new Dictionary<string, string>
                        {
                            { "season", seasonId.ToString() },
                            { "stage", "1" },
                            { "button", "view+season" }
                        };

                        var content = new System.Net.Http.FormUrlEncodedContent(values);
                        var response = httpClient.PostAsync("http://xbox-sports.com/leagues/xbshl/index.php?mode=standings", content).GetAwaiter().GetResult();
                        teamHtml = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    }

                    var teamDoc = new HtmlDocument();
                    teamDoc.LoadHtml(teamHtml);


                    var teamTable = teamDoc.DocumentNode.Descendants().Single(x => x.HasClass("table_1") && x.Id == "sort_table");
                    var statRows = teamTable.ChildNodes[3].ChildNodes.Where(x => x.Name == "tr");


                    foreach (var statRow in statRows)
                    {
                        var cells = statRow.ChildNodes.Where(x => x.Name == "td").ToArray();

                        statsTeams.Add(new StatsTeam()
                        {
                            TeamAbbreviation = TeamNameHelper.GetTeamAbbreviation(cells[0].ChildNodes[1].InnerText),
                            TeamName = cells[0].ChildNodes[1].InnerText,
                            GamesPlayed = Int32.Parse(cells[1].InnerText.Replace(",", "")),
                            Wins = Int32.Parse(cells[2].InnerText.Replace(",", "")),
                            Loss = Int32.Parse(cells[3].InnerText.Replace(",", "")),
                            OTL = Int32.Parse(cells[4].InnerText.Replace(",", "")),
                            Points = Int32.Parse(cells[5].InnerText.Replace(",", "")),
                            GoalsFor = Int32.Parse(cells[7].InnerText.Replace(",", "")),
                            GoalsAgainst = Int32.Parse(cells[8].InnerText.Replace(",", "")),
                            Hits = Int32.Parse(cells[9].InnerText.Replace(",", "")),
                            Shots = Int32.Parse(cells[10].InnerText.Replace(",", "")),
                            PIM = Int32.Parse(cells[11].InnerText.Replace(",", "")),
                            Season = seasonId

                        });
                    }
                }



                using (FileStream file = new FileStream("c:\\testoutputdraft\\teams_stats.xml", FileMode.Create, FileAccess.Write))
                {
                    System.Xml.Serialization.XmlSerializer ser = new System.Xml.Serialization.XmlSerializer(typeof(List<StatsTeam>));
                    ser.Serialize(file, statsTeams);
                }
            }


            Console.WriteLine($"Scrape successful. Found {players.Count} players.");
            Console.WriteLine("Press Enter to exit...");
            Console.Read();

        }

        private void Login(CookieContainer cookieContainer)
        {
            var req = WebRequest.CreateHttp("http://forum.xbox-sports.com/login.php?do=login");
            var formData = ""; // scrubbed for upload.  TODO: add configuration file.  If you have an active session/cookie you may not even need this.
            formData = String.Empty;
            var data = Encoding.ASCII.GetBytes(formData);

            req.ContentType = "application/x-www-form-urlencoded";
            req.ContentLength = data.Length;
            req.Headers = new WebHeaderCollection();
            req.Headers.Add("Cache-Control", "max-age=0");
            req.Headers.Add("Upgrade-Insecure-Requests", "1");
            req.CookieContainer = cookieContainer;
            req.Method = "POST";

            using (var stream = req.GetRequestStream())
            {
                stream.Write(data, 0, data.Length);
            }

            var response = (HttpWebResponse)req.GetResponse();

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception("Could not authenticate with XBS");
            }

        }

        private void PopulateCenterData(string forumId, PlayerAnalytics analytics)
        {
            string html;
            using (var httpClient = new HttpClient())
            {
                var values = new Dictionary<string, string>
                {
                    { "season", TargetSeason },
                    { "button", "view+season" }
                };

                var content = new System.Net.Http.FormUrlEncodedContent(values);
                var response = httpClient.PostAsync("http://xbox-sports.com/leagues/xbshl/index.php?mode=player&filter_pos=center&player_id=" + forumId, content).GetAwaiter().GetResult();
                html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(html);

            var statsItems = document.DocumentNode.Descendants().Where(x => x.HasClass("row_1g_h") || x.HasClass("row_1f_h"));

            foreach (var item in statsItems)
            {
                int season = Int32.Parse(item.Descendants().FirstOrDefault(x => x.HasClass("color_2a")).InnerText.Split(' ')[1]);

                if (season < _minimumSeasonThreshold) // Who cares about older seasons
                {
                    continue;
                }
                var childCells = item.ChildNodes.Where(x => x.Name == "td").ToArray();
                //1 = GP, 2= G, 3 = A, 4=pts, 5=pim, 6 = hits, 7 = shots, 8 = gwgm 9 =2619 fo%, 10 = +\-, 11=corsi, 12 = wins, 13 = losses, 14 = OTL
                var statsObj = new CenterStatistics();
                statsObj.Season = season;

                var teamNames = childCells[0].Descendants().Where(x => x.Name == "a");
                foreach (var name in teamNames)
                {
                    statsObj.TeamNames.Add(name.InnerText);
                }

                statsObj.GamesPlayed = Int32.Parse(childCells[1].InnerText);
                statsObj.Goals = Int32.Parse(childCells[2].InnerText);
                statsObj.Assists = Int32.Parse(childCells[3].InnerText);
                //statsObj.Points = statsObj.Goals + statsObj.Assists;
                statsObj.PIM = Int32.Parse(childCells[5].InnerText);
                statsObj.Hits = Int32.Parse(childCells[6].InnerText);
                statsObj.Shots = Int32.Parse(childCells[7].InnerText);
                statsObj.GWG = Int32.Parse(childCells[8].InnerText);
                statsObj.FaceoffPercentage = decimal.Parse(childCells[9].InnerText);
                statsObj.PlusMinus = Int32.Parse(childCells[10].InnerText);
                statsObj.Wins = Int32.Parse(childCells[12].InnerText);
                statsObj.Loss = Int32.Parse(childCells[13].InnerText);
                statsObj.OTL = Int32.Parse(childCells[14].InnerText);

                analytics.CenterStats.Add(statsObj);
            }
            //this.grd_center.ItemsSource = _centerStatistics;
        }

        private void PopulateDefenseData(string forumId, PlayerAnalytics analytics)
        {
            string html;
            using (var httpClient = new HttpClient())
            {
                var values = new Dictionary<string, string>
                {
                    { "season", TargetSeason },
                    { "button", "view+season" }
                };

                var content = new System.Net.Http.FormUrlEncodedContent(values);
                var response = httpClient.PostAsync("http://xbox-sports.com/leagues/xbshl/index.php?mode=player&filter_pos=defense&player_id=" + forumId, content).GetAwaiter().GetResult();
                html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(html);

            var statsItems = document.DocumentNode.Descendants().Where(x => x.HasClass("row_1g_h") || x.HasClass("row_1f_h"));

            foreach (var item in statsItems)
            {
                int season = Int32.Parse(item.Descendants().FirstOrDefault(x => x.HasClass("color_2a")).InnerText.Split(' ')[1]);

                if (season < _minimumSeasonThreshold) // Who cares about older seasons
                {
                    continue;
                }
                var childCells = item.ChildNodes.Where(x => x.Name == "td").ToArray();
                //1 = GP, 2= G, 3 = A, 4=pts, 5=pim, 6 = hits, 7 = shots, 8 = gwgm 9 = fo%, 10 = +\-, 11=corsi, 12 = wins, 13 = losses, 14 = OTL
                var statsObj = new DefenseStatistics();
                statsObj.Season = season;

                var teamNames = childCells[0].Descendants().Where(x => x.Name == "a");
                foreach (var name in teamNames)
                {
                    statsObj.TeamNames.Add(name.InnerText);
                }

                statsObj.GamesPlayed = Int32.Parse(childCells[1].InnerText);
                statsObj.Goals = Int32.Parse(childCells[2].InnerText);
                statsObj.Assists = Int32.Parse(childCells[3].InnerText);
                //statsObj.Points = statsObj.Goals + statsObj.Assists;
                statsObj.PIM = Int32.Parse(childCells[5].InnerText);
                statsObj.Hits = Int32.Parse(childCells[6].InnerText);
                statsObj.Shots = Int32.Parse(childCells[7].InnerText);
                statsObj.GWG = Int32.Parse(childCells[8].InnerText);
                statsObj.DefenseRating = decimal.Parse(childCells[9].InnerText);
                statsObj.PlusMinus = Int32.Parse(childCells[10].InnerText);
                statsObj.Wins = Int32.Parse(childCells[12].InnerText);
                statsObj.Loss = Int32.Parse(childCells[13].InnerText);
                statsObj.OTL = Int32.Parse(childCells[14].InnerText);

                analytics.DefenseStats.Add(statsObj);
            }

            //this.grd_defense.ItemsSource = _defenseStatistics;
        }

        private void PopulateWingData(string forumId, PlayerAnalytics analytics)
        {
            string html;
            using (var httpClient = new HttpClient())
            {
                var values = new Dictionary<string, string>
                {
                    { "season", TargetSeason },
                    { "button", "view+season" }
                };

                var content = new System.Net.Http.FormUrlEncodedContent(values);
                var response = httpClient.PostAsync("http://xbox-sports.com/leagues/xbshl/index.php?mode=player&filter_pos=wing&player_id=" + forumId, content).GetAwaiter().GetResult();
                html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(html);

            var statsItems = document.DocumentNode.Descendants().Where(x => x.HasClass("row_1g_h") || x.HasClass("row_1f_h"));

            foreach (var item in statsItems)
            {
                int season = Int32.Parse(item.Descendants().FirstOrDefault(x => x.HasClass("color_2a")).InnerText.Split(' ')[1]);

                if (season < _minimumSeasonThreshold) // Who cares about older seasons
                {
                    continue;
                }
                var childCells = item.ChildNodes.Where(x => x.Name == "td").ToArray();
                //1 = GP, 2= G, 3 = A, 4=pts, 5=pim, 6 = hits, 7 = shots, 8 = gwgm 9 = fo%, 10 = +\-, 11=corsi, 12 = wins, 13 = losses, 14 = OTL
                var statsObj = new WingerStatistics();
                statsObj.Season = season;

                var teamNames = childCells[0].Descendants().Where(x => x.Name == "a");
                foreach (var name in teamNames)
                {
                    statsObj.TeamNames.Add(name.InnerText);
                }

                statsObj.GamesPlayed = Int32.Parse(childCells[1].InnerText);
                statsObj.Goals = Int32.Parse(childCells[2].InnerText);
                statsObj.Assists = Int32.Parse(childCells[3].InnerText);
                //statsObj.Points = statsObj.Goals + statsObj.Assists;
                statsObj.PIM = Int32.Parse(childCells[5].InnerText);
                statsObj.Hits = Int32.Parse(childCells[6].InnerText);
                statsObj.Shots = Int32.Parse(childCells[7].InnerText);
                statsObj.GWG = Int32.Parse(childCells[8].InnerText);
                //statsObj.FaceoffPercentage = decimal.Parse(childCells[9].InnerText);
                statsObj.PlusMinus = Int32.Parse(childCells[9].InnerText);
                statsObj.Wins = Int32.Parse(childCells[11].InnerText);
                statsObj.Loss = Int32.Parse(childCells[12].InnerText);
                statsObj.OTL = Int32.Parse(childCells[13].InnerText);

                analytics.WingerStats.Add(statsObj);
            }
        }
        private void PopulateGoalieData(string forumId, PlayerAnalytics analytics)
        {
            string html;
            using (var httpClient = new HttpClient())
            {
                var values = new Dictionary<string, string>
                {
                    { "season", TargetSeason },
                    { "button", "view+season" }
                };

                var content = new System.Net.Http.FormUrlEncodedContent(values);
                var response = httpClient.PostAsync("http://xbox-sports.com/leagues/xbshl/index.php?mode=player&filter_pos=goalie&player_id=" + forumId, content).GetAwaiter().GetResult();
                html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(html);

            var statsItems = document.DocumentNode.Descendants().Where(x => x.HasClass("row_1g_h") || x.HasClass("row_1f_h"));

            foreach (var item in statsItems)
            {
                int season = Int32.Parse(item.Descendants().FirstOrDefault(x => x.HasClass("color_2a")).InnerText.Split(' ')[1]);

                if (season < _minimumSeasonThreshold) // Who cares about older seasons
                {
                    continue;
                }
                var childCells = item.ChildNodes.Where(x => x.Name == "td").ToArray();
                //1 = GP, 2= G, 3 = A, 4=pts, 5=pim, 6 = hits, 7 = shots, 8 = gwgm 9 =2619 fo%, 10 = +\-, 11=corsi, 12 = wins, 13 = losses, 14 = OTL
                var statsObj = new GoalieStatistics();
                statsObj.Season = season;

                var teamNames = childCells[0].Descendants().Where(x => x.Name == "a");
                foreach (var name in teamNames)
                {
                    statsObj.TeamNames.Add(name.InnerText);
                }

                statsObj.GamesPlayed = Int32.Parse(childCells[1].InnerText);
                statsObj.Wins = Int32.Parse(childCells[2].InnerText);
                statsObj.Loss = Int32.Parse(childCells[3].InnerText);
                //statsObj.Points = statsObj.Goals + statsObj.Assists;
                statsObj.OTL = Int32.Parse(childCells[4].InnerText);
                //statsObj.WinPercentage = Int32.Parse(childCells[6].InnerText);
                statsObj.GoalsAgainst = Int32.Parse(childCells[6].InnerText);
                statsObj.Saves = Int32.Parse(childCells[7].InnerText);
                statsObj.GAA = decimal.Parse(childCells[8].InnerText);

                statsObj.SavesPerGoalAgainst = decimal.Parse(childCells[10].InnerText);
                statsObj.SavePercentage = decimal.Parse(childCells[9].InnerText);
                statsObj.Shutouts = Int32.Parse(childCells[11].InnerText);

                analytics.GoalieStats.Add(statsObj);
            }
            //this.grd_center.ItemsSource = _centerStatistics;
        }


    }
}
