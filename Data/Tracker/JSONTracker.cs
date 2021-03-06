using System;
using System.Collections.Generic;
using System.Linq;
using Discord;
using Discord.WebSocket;
using Discord.Commands;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using MopsBot.Data.Entities;

namespace MopsBot.Data.Tracker
{
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreExtraElements]
    public class JSONTracker : BaseTracker
    {
        [MongoDB.Bson.Serialization.Attributes.BsonDictionaryOptions(MongoDB.Bson.Serialization.Options.DictionaryRepresentation.ArrayOfDocuments)]
        public Dictionary<string, string> PastInformation;
        public List<string> ToTrack;
        public DatePlot DataGraph;
        public static readonly string INTERVAL = "IntervalInMs";

        public JSONTracker() : base()
        {
        }

        public JSONTracker(string name) : base()
        {
            //name = name.Replace(" ", string.Empty);
            Name = name;
            ToTrack = name.Split("|||")[1].Split("\n").ToList();

            //Check if name yields proper results.
            try
            {
                PastInformation = getResults().Result;
                var graphMembers = PastInformation.Where(x => x.Key.Contains("graph:"));
                foreach(var graphTest in graphMembers){
                    if(!graphTest.Equals(default(KeyValuePair<string,string>))){
                        bool succeeded = double.TryParse(graphTest.Value, out double test);

                        if(succeeded){
                            var chosenName = graphTest.Key.Contains("as:") ? graphTest.Key.Split(":").Last() : graphTest.Key;
                            if(DataGraph == null) DataGraph = new DatePlot("JSON" + Name.GetHashCode(), "Date", "Value", format: "dd-MMM", relativeTime: false, multipleLines: true);
                            DataGraph.AddValueSeperate(chosenName, test, relative:false);
                        }

                        else throw new Exception("Graph value is not a number!");
                    }
                }
                SetTimer();
            }
            catch (Exception e)
            {
                Dispose();
                throw new Exception($"{Name} could not be resolved, using the given paths.", e);
            }
        }

        public override async void Conversion(object obj = null)
        {
            bool save = false;
            foreach (var channel in ChannelConfig.Keys.ToList())
            {
                if (!ChannelConfig[channel].ContainsKey(INTERVAL))
                {
                    ChannelConfig[channel][INTERVAL] = 60000;
                    save = true;
                }
            }
            if (save)
                await UpdateTracker();
        }

        public async override void PostChannelAdded(ulong channelId)
        {
            base.PostChannelAdded(channelId);

            var config = ChannelConfig[channelId];
            config[INTERVAL] = 600000;

            await UpdateTracker();
        }

        public override bool IsConfigValid(Dictionary<string, object> config, out string reason){
            reason = "";
            if((int)config[INTERVAL] < 60000){
                reason = "Interval can't be lower than 1 minute";
                return false;
            }
            return true;
        }

        public async override void PostInitialisation(object info = null)
        {
            if (DataGraph != null)
                DataGraph.InitPlot("Date", "Value", format: "dd-MMM", relative: false);

            SetTimer((int)ChannelConfig.FirstOrDefault().Value[INTERVAL]);
        }

        protected async override void CheckForChange_Elapsed(object stateinfo)
        {
            try
            {
                var newInformation = await getResults();

                if(PastInformation == null) PastInformation = newInformation;

                var embed = createEmbed(newInformation, PastInformation, out bool changed);
                if(changed){
                    var graphMembers = newInformation.Where(x => x.Key.Contains("graph:"));

                    foreach(var graphValue in graphMembers){
                        var name = graphValue.Key.Contains("as:") ? graphValue.Key.Split(":").Last() : graphValue.Key;
                        if(!graphValue.Equals(default(KeyValuePair<string,string>))){
                            DataGraph.AddValueSeperate(name, double.Parse(PastInformation[graphValue.Key]), relative: false);
                            DataGraph.AddValueSeperate(name, double.Parse(graphValue.Value), relative: false);
                        }
                    }

                    foreach (var channel in ChannelConfig.Keys.ToList()){
                        await OnMajorChangeTracked(channel, DataGraph == null ? embed : createEmbed(newInformation, PastInformation, out changed), (string)ChannelConfig[channel]["Notification"]);
                    }

                    PastInformation = newInformation;
                    await UpdateTracker();
                }
            }
            catch (Exception e)
            {
                await Program.MopsLog(new LogMessage(LogSeverity.Error, "", $" error by {Name}", e));
            }
        }

        private async Task<Dictionary<string, string>> getResults()
        {
            return await GetResults(TrackerUrl(), ToTrack.ToArray());
        }

        public static async Task<Dictionary<string, string>> GetResults(string JSONUrl, string[] ToTrack){
            var json = await FetchJSONDataAsync<dynamic>(JSONUrl);
            var result = new Dictionary<string, string>();

            foreach(string cur in ToTrack){
                string curMod = cur;
                if(curMod.Contains("graph:")) curMod = curMod.Replace("graph:", string.Empty);
                if(curMod.Contains("always:")) curMod = curMod.Replace("always:", string.Empty);
                string[] keywords = curMod.Split("->");
                var tmpJson = json;
                bool summarize = false;
                bool find = false;
                foreach(string keyword in keywords){
                    if(keyword.StartsWith("as:")) break;
                    int.TryParse(keyword, out int index);
                    if(summarize){
                        result[cur] = "";
                        foreach(var element in tmpJson){
                            if(index > 0) result[cur] += element[index - 1].ToString() + ", ";
                            else result[cur] += element[keyword].ToString() + ", ";
                        }
                        if(result[cur].Equals(string.Empty)) result[cur] = "No values";
                        break;
                    }
                    else if(find){
                        find = false;
                        var tmpKeywords = keyword.Split("=");
                        int.TryParse(tmpKeywords[0], out int i);
                        foreach(var element in tmpJson){
                            if(i > 0){
                                if(element[i].ToString().Equals(tmpKeywords[1])){
                                    tmpJson = element;
                                    break;
                                }
                            }
                            else{
                                if(element[tmpKeywords[0]].ToString().Equals(tmpKeywords[1])){
                                    tmpJson = element;
                                    break;
                                }
                            }
                        }
                    }
                    else{
                        if(index > 0) tmpJson = tmpJson[index - 1];
                        else if(!keyword.StartsWith("all") && !keyword.StartsWith("find")) tmpJson = tmpJson[keyword];
                        else if(keyword.StartsWith("all")) summarize = true;
                        else find = true;
                    }
                }
                
                if(summarize == false)
                    result[cur] = tmpJson.ToString();
                else if(keywords.Last().StartsWith("all") || keywords.Last().StartsWith("as:")){
                    try{
                        result[cur] = string.Join(", ", tmpJson);
                    } catch {
                        result[cur] = "No Value Found";
                    }
                }
            }

            return result;
        }

        private Embed createEmbed(Dictionary<string, string> newInformation, Dictionary<string, string> oldInformation, out bool changed)
        {
            var embed = new EmbedBuilder();
            embed.WithColor(255, 227, 21);
            embed.WithTitle("Change tracked").WithUrl(TrackerUrl()).WithCurrentTimestamp();
            embed.WithFooter(x => {
                                   x.Text = "JsonTracker"; 
                                   x.IconUrl="https://upload.wikimedia.org/wikipedia/commons/thumb/c/c9/JSON_vector_logo.svg/160px-JSON_vector_logo.svg.png";
                            });

            changed = false;
            foreach(var kvp in newInformation){
                string oldS = oldInformation.ContainsKey(kvp.Key) ? oldInformation[kvp.Key] : "No Value Found";
                string newS = kvp.Value;
                var keyName = kvp.Key.Contains("as:") ? kvp.Key.Split(":").Last() : kvp.Key.Split("->").Last();

                if(!newS.Equals(oldS)){
                    changed = true;
                    embed.AddField(keyName, $"{oldS} -> {newS}", true);
                }
                else if(kvp.Key.Contains("always:")){
                    embed.AddField(keyName, newS, true);
                }
            }

            if(DataGraph != null && changed) embed.ImageUrl = DataGraph.DrawPlot();

            return embed.Build();
        }

        public override string TrackerUrl()
        {
            return Name.Split("|||")[0];
        }

        public override async Task UpdateTracker(){
            await StaticBase.Trackers[TrackerType.JSON].UpdateDBAsync(this);
        }
    }
}
