using System;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Newtonsoft.Json;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using MopsBot.Data.Tracker.APIResults.Osu;
using MopsBot.Api;

namespace MopsBot.Data.Tracker
{
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreExtraElements]
    public class OsuTracker : BaseTracker
    {
        public Dictionary<string, double> AllPP;
        public double PPThreshold;

        public OsuTracker() : base(60000, ExistingTrackers * 2000)
        {
            AllPP = new Dictionary<string, double>();
            AllPP.Add("m=0", 0);
            AllPP.Add("m=1", 0);
            AllPP.Add("m=2", 0);
            AllPP.Add("m=3", 0);
        }

        public OsuTracker(Dictionary<string, string> args) : base(60000, 60000){
            base.SetBaseValues(args, true);
            
            //Check if Name ist valid
            try{
                new OsuTracker(Name).Dispose();
            } catch (Exception e){
                this.Dispose();
                throw e;
            }

            AllPP = new Dictionary<string, double>();
            AllPP.Add("m=0", 0);
            AllPP.Add("m=1", 0);
            AllPP.Add("m=2", 0);
            AllPP.Add("m=3", 0);
            PPThreshold = double.Parse(args["PPThreshold"]);

            if(StaticBase.Trackers[TrackerType.Osu].GetTrackers().ContainsKey(Name)){
                this.Dispose();

                args["Id"] = Name;
                var curTracker = StaticBase.Trackers[TrackerType.Osu].GetTrackers()[Name];
                curTracker.ChannelMessages[ulong.Parse(args["Channel"].Split(":")[1])] = args["Notification"];
                StaticBase.Trackers[TrackerType.Osu].UpdateContent(new Dictionary<string, Dictionary<string, string>>{{"NewValue", args}, {"OldValue", args}}).Wait();

                throw new ArgumentException($"Tracker for {args["_Name"]} existed already, updated instead!");
            }
        }

        public OsuTracker(string name) : base(60000)
        {
            Name = name;
            AllPP = new Dictionary<string, double>();
            AllPP.Add("m=0", 0);
            AllPP.Add("m=1", 0);
            AllPP.Add("m=2", 0);
            AllPP.Add("m=3", 0);
            PPThreshold = 0.1;

            //Check if person exists by forcing Exceptions if not.
            try
            {
                var checkExists = fetchUser().Result;
                var test = checkExists.username;
            }
            catch (Exception)
            {
                Dispose();
                throw new Exception($"Player {TrackerUrl()} could not be found on Osu!");
            }
        }

        protected async override void CheckForChange_Elapsed(object stateinfo)
        {
            try
            {
                foreach (var pp in AllPP.ToList())
                {
                    OsuResult userInformation = await fetchUser(pp.Key);
                    if (userInformation == null) return;

                    if (pp.Value > 0 && pp.Value + PPThreshold <= double.Parse(userInformation.pp_raw, CultureInfo.InvariantCulture))
                    {
                        var recentScores = await fetchRecent(pp.Key);

                        if (recentScores == null)
                        {
                            AllPP[pp.Key] = double.Parse(userInformation.pp_raw ?? "0", CultureInfo.InvariantCulture);
                            await StaticBase.Trackers[TrackerType.Osu].UpdateDBAsync(this);
                            return;
                        }

                        RecentScore scoreInformation = recentScores.First(x => !x.rank.Equals("F"));

                        Beatmap beatmapInformation = await fetchBeatmap(scoreInformation.beatmap_id, pp.Key);

                        foreach (ulong channel in ChannelMessages.Keys.ToList())
                        {
                            await OnMajorChangeTracked(channel, createEmbed(userInformation, beatmapInformation, await fetchScore(scoreInformation.beatmap_id, pp.Key),
                                                       Math.Round(double.Parse(userInformation.pp_raw, CultureInfo.InvariantCulture) - pp.Value, 2), pp.Key), ChannelMessages[channel]);
                        }
                    }

                    if(pp.Value != double.Parse(userInformation.pp_raw ?? "0", CultureInfo.InvariantCulture)){
                        AllPP[pp.Key] = double.Parse(userInformation.pp_raw ?? "0", CultureInfo.InvariantCulture);
                        await StaticBase.Trackers[TrackerType.Osu].UpdateDBAsync(this);
                    }
                }
            }
            catch (Exception e)
            {
                if (!e.StackTrace.StartsWith("The read operation failed"))
                    await Program.Client_Log(new LogMessage(LogSeverity.Error, this.GetType().Name + "." + System.Reflection.MethodBase.GetCurrentMethod().Name, $" error by {Name}", e));
            }
        }

        public async Task<OsuResult> fetchUser(string mode = "m=0")
        {
            string query = await MopsBot.Module.Information.GetURLAsync($"https://osu.ppy.sh/api/get_user?u={Name}&{mode}&event_days=31&k={Program.Config["Osu"]}");

            return JsonConvert.DeserializeObject<OsuResult>(query.Substring(1, query.Length - 2));
        }

        public async Task<List<RecentScore>> fetchRecent(string mode = "m=0")
        {
            return await FetchJSONDataAsync<List<RecentScore>>($"https://osu.ppy.sh/api/get_user_recent?u={Name}&limit=50&{mode}&k={Program.Config["Osu"]}");
        }

        public async Task<Score> fetchScore(string beatmapID, string mode = "m=0")
        {
            var tmpResult = await FetchJSONDataAsync<List<Score>>($"https://osu.ppy.sh/api/get_scores?b={beatmapID}&{mode}&u={Name}&limit=1&k={Program.Config["Osu"]}");

            return tmpResult.OrderByDescending(x => DateTime.Parse(x.date)).FirstOrDefault();
        }

        public async Task<Beatmap> fetchBeatmap(string beatmapID, string mode = "m=0")
        {
            string query = await MopsBot.Module.Information.GetURLAsync($"https://osu.ppy.sh/api/get_beatmaps?b={beatmapID}&{mode}&a=1&k={Program.Config["Osu"]}");

            return JsonConvert.DeserializeObject<Beatmap>(query.Substring(1, query.Length - 2));
        }

        private Embed createEmbed(OsuResult userInformation, Beatmap beatmapInformation,
                                        Score scoreInformation, double ppChange, string mode = "m=0")
        {
            EmbedBuilder e = new EmbedBuilder();
            e.Color = new Color(255, 87, 156);
            e.Title = $"{beatmapInformation.artist} - {beatmapInformation.title} [{beatmapInformation.version}]";
            e.Url = $"https://osu.ppy.sh/b/{beatmapInformation.beatmap_id}&m={beatmapInformation.mode}";
            e.Description = Math.Round(double.Parse(beatmapInformation.difficultyrating, CultureInfo.InvariantCulture), 2) + "*\n" + (Mods)int.Parse(scoreInformation.enabled_mods);
            e.Timestamp = DateTime.Parse(scoreInformation.date).AddHours(1);

            EmbedAuthorBuilder author = new EmbedAuthorBuilder();
            author.Name = Name;
            author.Url = $"https://osu.ppy.sh/u/{userInformation.user_id}";
            author.IconUrl = $"https://a.ppy.sh/{userInformation.user_id}_0.png";
            e.Author = author;

            EmbedFooterBuilder footer = new EmbedFooterBuilder();
            footer.IconUrl = "https://vignette.wikia.nocookie.net/cytus/images/5/51/Osu_icon.png";
            footer.Text = "Osu!";
            e.Footer = footer;

            switch (mode)
            {
                case "m=0":
                    e.ThumbnailUrl = "https://lemmmy.pw/osusig/img/osu.png";
                    break;
                case "m=1":
                    e.ThumbnailUrl = "https://lemmmy.pw/osusig/img/taiko.png";
                    break;
                case "m=2":
                    e.ThumbnailUrl = "https://lemmmy.pw/osusig/img/ctb.png";
                    break;
                case "m=3":
                    e.ThumbnailUrl = "https://lemmmy.pw/osusig/img/mania.png";
                    break;
            }
            e.ImageUrl = $"https://b.ppy.sh/thumb/{beatmapInformation.beatmapset_id}l.jpg";

            e.AddField("Score", scoreInformation.score + $" ({scoreInformation.maxcombo}/{beatmapInformation.max_combo}x)", true);
            e.AddField("Acc", calcAcc(scoreInformation, int.Parse(beatmapInformation.mode)) + $"% {scoreInformation.rank}", true);
            e.AddField("PP for play", Math.Round(double.Parse(scoreInformation.pp ?? "NaN", CultureInfo.InvariantCulture), 2) + $" (+{ppChange})", true);
            e.AddField("Rank", userInformation.pp_rank, true);

            return e.Build();
        }

        private double calcAcc(Score scoreInformation, int mode)
        {
            double pointsOfHits = 0, numberOfHits = 0, accuracy = 0, fruitsCaught = 0, numberOfFruits = 0;
            int count50 = int.Parse(scoreInformation.count50);
            int count100 = int.Parse(scoreInformation.count100);
            int count300 = int.Parse(scoreInformation.count300);
            int countmiss = int.Parse(scoreInformation.countmiss);
            int countkatu = int.Parse(scoreInformation.countkatu);
            int countgeki = int.Parse(scoreInformation.countgeki);

            switch (mode)
            {
                case 3:
                    pointsOfHits = count50 * 50 + count100 * 100 + count300 * 300 + countkatu * 200 + countgeki * 300;
                    numberOfHits = count50 + count100 + count300 + countmiss + countkatu + countgeki;
                    accuracy = ((double)pointsOfHits / (numberOfHits * 300));
                    return Math.Round(accuracy * 100, 2);
                case 2:
                    fruitsCaught = count50 + count100 + count300;
                    numberOfFruits = count50 + count100 + count300 + countkatu + countmiss;
                    accuracy = (numberOfFruits / (double)fruitsCaught);
                    return Math.Round(accuracy * 100, 2);
                case 1:
                    pointsOfHits = (count100 * 0.5 + count300) * 300;
                    numberOfHits = countmiss + count100 + count300;
                    accuracy = ((double)pointsOfHits / (numberOfHits * 300));
                    return Math.Round(accuracy * 100, 2);
                case 0:
                    pointsOfHits = (count50 * 50) + (count100 * 100) + (count300 * 300);
                    numberOfHits = count50 + count100 + count300 + countmiss;
                    accuracy = ((double)pointsOfHits / (numberOfHits * 300));
                    return Math.Round(accuracy * 100, 2);
            }
            return 0;
        }

        [Flags]
        public enum Mods
        {
            NoMod = 0,
            NoFail = 1,
            Easy = 2,
            TouchDevice = 4,
            Hidden = 8,
            HardRock = 16,
            SuddenDeath = 32,
            DoubleTime = 64,
            Relax = 128,
            HalfTime = 256,
            Nightcore = 512, // Only set along with DoubleTime. i.e: NC only gives 576
            Flashlight = 1024,
            Autoplay = 2048,
            SpunOut = 4096,
            Relax2 = 8192,  // Autopilot
            Perfect = 16384, // Only set along with SuddenDeath. i.e: PF only gives 16416  
            Key4 = 32768,
            Key5 = 65536,
            Key6 = 131072,
            Key7 = 262144,
            Key8 = 524288,
            FadeIn = 1048576,
            Random = 2097152,
            Cinema = 4194304,
            Target = 8388608,
            Key9 = 16777216,
            KeyCoop = 33554432,
            Key1 = 67108864,
            Key3 = 134217728,
            Key2 = 268435456,
            ScoreV2 = 536870912,
            LastMod = 1073741824,
            KeyMod = Key1 | Key2 | Key3 | Key4 | Key5 | Key6 | Key7 | Key8 | Key9 | KeyCoop,
            FreeModAllowed = NoFail | Easy | Hidden | HardRock | SuddenDeath | Flashlight | FadeIn | Relax | Relax2 | SpunOut | KeyMod,
            ScoreIncreaseMods = Hidden | HardRock | DoubleTime | Flashlight | FadeIn
        }

        public override string TrackerUrl()
        {
            return "https://osu.ppy.sh/u/" + Name;
        }

        public override Dictionary<string, object> GetParameters(ulong guildId)
        {
            var parentParameters = base.GetParameters(guildId);
            (parentParameters["Parameters"] as Dictionary<string, object>)["PPThreshold"] = "0,1";
            return parentParameters;
        }

        public override object GetAsScope(ulong channelId){
            return new ContentScope(){
                Id = this.Name,
                _Name = this.Name,
                Notification = this.ChannelMessages[channelId],
                Channel = "#" + ((SocketGuildChannel)Program.Client.GetChannel(channelId)).Name + ":" + channelId,
                PPThreshold = this.PPThreshold
            };
        }

        public override void Update(Dictionary<string, Dictionary<string, string>> args){
            base.Update(args);
            PPThreshold = double.Parse(args["NewValue"]["PPThreshold"]);
        }

        public new struct ContentScope
        {
            public string Id;
            public string _Name;
            public string Notification;
            public string Channel;
            public double PPThreshold;
        }
    }
}
