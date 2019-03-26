﻿using System;
using Discord;
using Discord.WebSocket;
using Discord.Commands;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using MopsBot.Data;
using MopsBot.Data.Tracker;
using MopsBot.Data.Interactive;
using Tweetinvi;
using NewsAPI;
using SharprWowApi;
using MongoDB.Driver;
using MongoDB.Bson.Serialization.Options;
using MongoDB.Bson.Serialization.Attributes;
using DiscordBotsList.Api;
using DiscordBotsList.Api.Objects;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;

namespace MopsBot
{
    public class StaticBase
    {
        public static MongoClient DatabaseClient = new MongoClient($"{Program.Config["DatabaseURL"]}");
        public static IMongoDatabase Database = DatabaseClient.GetDatabase("Mops");
        public static readonly System.Net.Http.HttpClient HttpClient = new System.Net.Http.HttpClient();
        public static AuthDiscordBotListApi DiscordBotList = new AuthDiscordBotListApi(305398845389406209, Program.Config["DiscordBotListKey"]);
        public static Random ran = new Random();
        public static Gfycat.GfycatClient gfy;
        public static List<string> Playlist = new List<string>();
        [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
        public static Dictionary<ulong, Data.Entities.WelcomeMessage> WelcomeMessages;
        public static Dictionary<ulong, Data.Entities.CustomCommands> CustomCommands;
        public static ReactionGiveaway ReactGiveaways;
        public static ReactionRoleJoin ReactRoleJoin;
        public static ReactionPoll Poll;
        //public static Crosswords Crosswords;
        public static Dictionary<BaseTracker.TrackerType, TrackerWrapper> Trackers;
        public static NewsApiClient NewsClient;

        public static bool init = false;

        /// <summary>
        /// Initialises and loads all trackers
        /// </summary>
        public static void initTracking()
        {
            if (!init)
            {
                HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MSIE 9.0; Windows NT 6.1; Trident/5.0)");
                ServicePointManager.ServerCertificateValidationCallback = (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => {return true;};
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls|SecurityProtocolType.Tls11|SecurityProtocolType.Tls12;
                HttpClient.DefaultRequestHeaders.ConnectionClose = true;
                MopsBot.Data.Entities.UserEvent.UserVoted += UserVoted;
                Task.Run(() => new MopsBot.Data.Entities.UserEvent().CheckUsersVotedLoop());

                Task.Run(() =>
                {
                    WelcomeMessages = Database.GetCollection<Data.Entities.WelcomeMessage>("WelcomeMessages").FindSync(x => true).ToEnumerable().ToDictionary(x => x.GuildId);
                    ReactGiveaways = new ReactionGiveaway();
                    ReactRoleJoin = new ReactionRoleJoin();
                    Poll = new ReactionPoll();
                });

                Auth.SetUserCredentials(Program.Config["TwitterKey"], Program.Config["TwitterSecret"],
                                        Program.Config["TwitterToken"], Program.Config["TwitterAccessSecret"]);
                TweetinviConfig.CurrentThreadSettings.TweetMode = TweetMode.Extended;
                TweetinviConfig.ApplicationSettings.TweetMode = TweetMode.Extended;
                Tweetinvi.ExceptionHandler.SwallowWebExceptions = false;
                Tweetinvi.RateLimit.RateLimitTrackerMode = RateLimitTrackerMode.TrackOnly;
                TweetinviEvents.QueryBeforeExecute += Data.Tracker.TwitterTracker.QueryBeforeExecute;

                gfy = new Gfycat.GfycatClient(Program.Config["GfyID"], Program.Config["GfySecret"]);

                NewsClient = new NewsApiClient(Program.Config["NewsAPI"]);
                //WoWTracker.WoWClient = new SharprWowApi.WowClient(Region.EU, Locale.en_GB, Program.Config["WoWKey"]);

                Trackers = new Dictionary<BaseTracker.TrackerType, Data.TrackerWrapper>();
                Trackers[BaseTracker.TrackerType.Osu] = new TrackerHandler<OsuTracker>();
                Trackers[BaseTracker.TrackerType.Overwatch] = new TrackerHandler<OverwatchTracker>();
                Trackers[BaseTracker.TrackerType.Twitch] = new TrackerHandler<TwitchTracker>();
                Trackers[BaseTracker.TrackerType.TwitchClip] = new TrackerHandler<TwitchClipTracker>();
                Trackers[BaseTracker.TrackerType.Twitter] = new TrackerHandler<TwitterTracker>();
                Trackers[BaseTracker.TrackerType.Youtube] = new TrackerHandler<YoutubeTracker>();
                Trackers[BaseTracker.TrackerType.YoutubeLive] = new TrackerHandler<YoutubeLiveTracker>();
                Trackers[BaseTracker.TrackerType.Reddit] = new TrackerHandler<RedditTracker>();
                Trackers[BaseTracker.TrackerType.JSON] = new TrackerHandler<JSONTracker>();
                //Trackers[BaseTracker.TrackerType.WoW] = new TrackerHandler<WoWTracker>();
                //Trackers[ITracker.TrackerType.WoWGuild] = new TrackerHandler<WoWGuildTracker>();
                Trackers[BaseTracker.TrackerType.OSRS] = new TrackerHandler<OSRSTracker>();
                Trackers[BaseTracker.TrackerType.HTML] = new TrackerHandler<HTMLTracker>();
                Trackers[BaseTracker.TrackerType.RSS] = new TrackerHandler<RSSTracker>();
                //Trackers[BaseTracker.TrackerType.Tibia] = new TrackerHandler<JSONTracker>();
                Trackers[BaseTracker.TrackerType.TwitterRealtime] = new TrackerHandler<TwitterRealtimeTracker>();

                foreach (var tracker in Trackers)
                {
                    Task.Run(() => tracker.Value.PostInitialisation());
                }

                init = true;

            }
        }

        public static async Task InsertOrUpdatePrefixAsync(ulong guildId, string prefix)
        {
            bool hasEntry = (await Database.GetCollection<Data.Entities.MongoKVP<ulong, string>>("GuildPrefixes").FindAsync(x => x.Key == guildId)).ToList().Count == 1;

            if (!hasEntry)
                await Database.GetCollection<Data.Entities.MongoKVP<ulong, string>>("GuildPrefixes").InsertOneAsync(new Data.Entities.MongoKVP<ulong, string>(guildId, prefix));
            else
                await Database.GetCollection<Data.Entities.MongoKVP<ulong, string>>("GuildPrefixes").ReplaceOneAsync(x => x.Key == guildId, new Data.Entities.MongoKVP<ulong, string>(guildId, prefix));
        }

        public static async Task<string> GetGuildPrefixAsync(ulong guildId)
        {
            string prefix = (await Database.GetCollection<Data.Entities.MongoKVP<ulong, string>>("GuildPrefixes").FindAsync(x => x.Key == guildId)).FirstOrDefault()?.Value;
            return prefix ?? "!";
        }

        /// <summary>
        /// Updates the guild count, by displaying it as an activity.
        /// </summary>
        /// <returns>A Task that sets the activity</returns>
        public static async Task UpdateServerCount()
        {
            await Program.Client.SetActivityAsync(new Game($"{Program.Client.Guilds.Count} servers", ActivityType.Watching));
            try
            {
                if(Program.Client.CurrentUser.Id == 305398845389406209)
                    await DiscordBotList.UpdateStats(Program.Client.Guilds.Count);
            }
            catch (Exception e)
            {
                await Program.MopsLog(new LogMessage(LogSeverity.Error, "", "discord bot list api failed", e));
            }
        }

        public static async Task UserVoted(IDblEntity user){
            await Program.MopsLog(new LogMessage(LogSeverity.Info, "", $"User {user.ToString()}({user.Id}) voted. Adding 10 VP to balance!"));
            await MopsBot.Data.Entities.User.ModifyUserAsync(user.Id, x => x.Money += 10);
            try{
                if(Program.Client.CurrentUser.Id == 305398845389406209)
                    await (await Program.Client.GetUser(user.Id).GetOrCreateDMChannelAsync()).SendMessageAsync("Thanks for voting for me!\nI have added 10 Votepoints to your balance!");
            } catch(Exception e){
                await Program.MopsLog(new LogMessage(LogSeverity.Error, "", "messaging voter failed", e));
            }
        }

        /// <summary>
        /// Displays the tracker counts one after another.
        /// </summary>
        /// <returns>A Task that sets the activity</returns>
        public static async Task UpdateStatusAsync()
        {
            await Program.Client.SetActivityAsync(new Game("Currently Restarting!", ActivityType.Playing));
            await Task.Delay(60000);

            int status = Enum.GetNames(typeof(BaseTracker.TrackerType)).Length;
            while (true)
            {
                try
                {
                    BaseTracker.TrackerType type = (BaseTracker.TrackerType)status++;
                    var trackerCount = Trackers[type].GetTrackers().Count;
                    await Program.Client.SetActivityAsync(new Game($"{trackerCount} {type.ToString()} Trackers", ActivityType.Watching));
                }
                catch
                {
                    //Trackers were not initialised yet, or status exceeded trackertypes
                    //Show servers instead
                    status = 0;
                    await UpdateServerCount();
                }

                await Task.Delay(30000);
            }
        }
    }
}
