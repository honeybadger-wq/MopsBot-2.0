﻿using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json;
using System.Net.NetworkInformation;
using System.Net.Http;
using System.Xml.Serialization;
using MopsBot.Module.Preconditions;

namespace MopsBot.Module
{
    public class Information : ModuleBase
    {
        public static int FailedRequests = 0, SucceededRequests = 0;

        [Command("HowLong")]
        [Summary("Returns the date you joined the Guild")]
        [RequireBotPermission(ChannelPermission.SendMessages)]
        public async Task howLong([Remainder]SocketGuildUser user = null)
        {
            if (user == null)
                user = (SocketGuildUser)Context.User;
            await ReplyAsync(user.JoinedAt.Value.Date.ToString("d"));
        }

        [Command("Invite")]
        [Summary("Provides link to make me join your Server")]
        [RequireBotPermission(ChannelPermission.SendMessages)]
        public async Task joinServer()
        {
            await ReplyAsync($"https://discordapp.com/oauth2/authorize?client_id={Context.Client.CurrentUser.Id}&permissions=271969344&scope=bot");
        }

        [Command("Vote")]
        [Summary("Provides link to vote for me!")]
        [RequireBotPermission(ChannelPermission.SendMessages)]
        public async Task Vote()
        {
            await ReplyAsync($"https://discordbots.org/bot/{Program.Client.CurrentUser.Id}/vote");
        }

        [Command("Define", RunMode = RunMode.Async)]
        [Summary("Searches dictionaries for a definition of the specified word or expression")]
        [Ratelimit(1, 10, Measure.Seconds)]
        public async Task define([Remainder] string text)
        {
            using (Context.Channel.EnterTypingState())
            {

                string query = Task.Run(() => GetURLAsync($"http://api.wordnik.com:80/v4/word.json/{text}/definitions?limit=1&includeRelated=false&sourceDictionaries=all&useCanonical=true&includeTags=false&api_key={Program.Config["Wordnik"]}")).Result;

                dynamic tempDict = JsonConvert.DeserializeObject<dynamic>(query);

                tempDict = tempDict[0];
                await ReplyAsync($"__**{tempDict["word"]}**__\n\n``{tempDict["text"]}``");
            }
        }

        [Command("Translate", RunMode = RunMode.Async)]
        [Summary("Translates your text from srcLanguage to tgtLanguage.")]
        [RequireBotPermission(ChannelPermission.SendMessages)]
        [Ratelimit(1, 10, Measure.Seconds)]
        public async Task translate(string srcLanguage, string tgtLanguage, [Remainder] string text)
        {
            using (Context.Channel.EnterTypingState())
            {
                string query = Task.Run(() => GetURLAsync($"https://translate.googleapis.com/translate_a/single?client=gtx&sl={srcLanguage}&tl={tgtLanguage}&dt=t&q={text}")).Result;
                dynamic tempDict = JsonConvert.DeserializeObject<dynamic>(query);
                await ReplyAsync(tempDict[0][0][0].ToString());
            }
        }

        [Command("Wolfram", RunMode = RunMode.Async)]
        [Summary("Sends a query to wolfram alpha.")]
        [Ratelimit(1, 10, Measure.Seconds)]
        public async Task wolf([Remainder]string query)
        {
            using (Context.Channel.EnterTypingState())
            {
                var result = await GetURLAsync($"https://api.wolframalpha.com/v2/query?input={System.Web.HttpUtility.UrlEncode(query)}&format=image,plaintext&podstate=Step-by-step%20solution&output=JSON&appid={Program.Config["WolframAlpha"]}");
                var jsonResult = JsonConvert.DeserializeObject<Data.Tracker.APIResults.Wolfram.WolframResult>(result);
                for (int i = 0; i < 2 && i < jsonResult.queryresult.pods.Count; i++)
                {
                    var image = jsonResult.queryresult.pods[i].subpods.FirstOrDefault(x => x.title == "Possible intermediate steps")?.img.src ?? jsonResult.queryresult.pods[i].subpods.First()?.img.src;
                    var embed = new EmbedBuilder().WithTitle(jsonResult.queryresult.pods[i].title).WithDescription(query).WithImageUrl(image);
                    await ReplyAsync("", embed: embed.Build());
                }
            }
        }

        public async static Task<dynamic> GetRandomWordAsync()
        {
            try
            {
                string query = await GetURLAsync($"http://api.wordnik.com:80/v4/words.json/randomWord?hasDictionaryDef=true&excludePartOfSpeech=given-name&minCorpusCount=10000&maxCorpusCount=-1&minDictionaryCount=4&maxDictionaryCount=-1&minLength=3&maxLength=13&api_key={Program.Config["Wordnik"]}");
                dynamic tempDict = JsonConvert.DeserializeObject<dynamic>(query);
                return tempDict["word"];
            }
            catch (Exception e)
            {
                await Program.MopsLog(new LogMessage(LogSeverity.Error, "", $"Getting random word failed", e));
            }
            return null;
        }

        public static async Task<string> PostURLAsync(string URL, string body = "", params KeyValuePair<string, string>[] headers)
        {
            if(FailedRequests >= 10 && SucceededRequests / FailedRequests < 1){
                await Program.MopsLog(new LogMessage(LogSeverity.Warning, "HttpRequests", $"More Failed requests {FailedRequests} than succeeded ones {SucceededRequests}. Waiting"));
                return "";
            }

            HttpRequestMessage test = new HttpRequestMessage(HttpMethod.Post, URL);
            test.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            foreach(var header in headers)
                test.Headers.TryAddWithoutValidation(header.Key, header.Value);

            using (var response = await StaticBase.HttpClient.SendAsync(test))
            {
                try
                {
                    string value = await response.Content.ReadAsStringAsync();
                    SucceededRequests++;
                    return value;
                }
                catch (Exception e)
                {
                    FailedRequests++;
                    await Program.MopsLog(new LogMessage(LogSeverity.Error, "", $"error for sending post request to {URL}", e.GetBaseException()));
                    throw e;
                }
            }
        }

        public static async Task<string> GetURLAsync(string URL, params KeyValuePair<string, string>[] headers)
        {
            if(FailedRequests >= 10 && SucceededRequests / FailedRequests < 1){
                await Program.MopsLog(new LogMessage(LogSeverity.Warning, "HttpRequests", $"More Failed requests {FailedRequests} than succeeded ones {SucceededRequests}. Waiting"));
                return "";
            }

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, URL))
                {
                    foreach (var kvp in headers)
                        request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                    using (var response = await StaticBase.HttpClient.SendAsync(request))
                    {
                        using (var content = response.Content)
                        {
                            string value = "";
                            if (content?.Headers?.ContentType?.CharSet?.Contains("utf8") ?? false)
                                value = System.Text.Encoding.UTF8.GetString(await content.ReadAsByteArrayAsync());
                            else
                                value = await content.ReadAsStringAsync();
                            
                            SucceededRequests++;
                            return value;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                FailedRequests++;
                if (!e.GetBaseException().Message.Contains("the remote party has closed the transport stream") && !e.GetBaseException().Message.Contains("The server returned an invalid or unrecognized response"))
                    await Program.MopsLog(new LogMessage(LogSeverity.Error, "", $"error for sending request to {URL}", e.GetBaseException()));
                else if (e.GetBaseException().Message.Contains("the remote party has closed the transport stream"))
                    await Program.MopsLog(new LogMessage(LogSeverity.Warning, "", $"Remote party closed the transport stream: {URL}."));
                else
                    await Program.MopsLog(new LogMessage(LogSeverity.Debug, "", $"Osu API messed up again: {URL}"));
                throw e;
            }
        }
    }
}

