using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using Newtonsoft.Json;

namespace MopsBot.Data
{
    public class Giveaway
    {
        public Dictionary<string, HashSet<ulong>> Giveaways = new Dictionary<string, HashSet<ulong>>();

        public Giveaway()
        {
            using (StreamReader read = new StreamReader(new FileStream($"mopsdata//Giveaways.json", FileMode.OpenOrCreate)))
            {
                try
                {
                    Giveaways = JsonConvert.DeserializeObject<Dictionary<string, HashSet<ulong>>>(read.ReadToEnd());
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message + e.StackTrace);
                }
            }
            Giveaways = Giveaways ?? new Dictionary<string, HashSet<ulong>>();
        }

        public void SaveJson()
        {
            using (StreamWriter write = new StreamWriter(new FileStream($"mopsdata//Giveaways.json", FileMode.Create)))
                write.Write(JsonConvert.SerializeObject(Giveaways, Formatting.Indented));
        }

        public void AddGiveaway(string name)
        {
            name = name.ToLower();

            if (!Giveaways.ContainsKey(name))
            {
                Giveaways.Add(name, new HashSet<ulong>());
                SaveJson();
            }

            else
                throw new Exception("A Giveaway with the same name already exists.\nPlease try another name.");
        }

        public void JoinGiveaway(string name, ulong id)
        {
            if (Giveaways.ContainsKey(name))
            {
                Giveaways[name].Add(id);
                SaveJson();
            }

            else
                throw new Exception("The Giveaway does not seem to exist.");
        }

        public ulong DrawGiveaway(string name)
        {
            name = name.ToLower();

            if (Giveaways.ContainsKey(name))
            {
                if (Giveaways[name].Count > 1)
                {
                    ulong toReturn = Giveaways[name].ToList()[StaticBase.ran.Next(1, Giveaways[name].Count)];
                    Giveaways.Remove(name);
                    SaveJson();
                    return toReturn;
                }
                else
                {
                    Giveaways.Remove(name);
                    SaveJson();
                    throw new Exception("There was nobody to draw. Deleting Giveaway still.");
                }
            }

            throw new Exception("The Giveaway does not exist.");
        }
    }

    public class ReactionGiveaway
    {

        //Key: Channel ID, Value: (Key: Message ID, Value: User IDs)
        public Dictionary<ulong, Dictionary<ulong, List<ulong>>> Giveaways = new Dictionary<ulong, Dictionary<ulong, List<ulong>>>();

        public ReactionGiveaway()
        {
            using (StreamReader read = new StreamReader(new FileStream($"mopsdata//ReactionGiveaways.json", FileMode.OpenOrCreate)))
            {
                try
                {
                    Giveaways = JsonConvert.DeserializeObject<Dictionary<ulong, Dictionary<ulong, List<ulong>>>>(read.ReadToEnd());
                    foreach (var channel in Giveaways)
                    {
                        foreach (var message in channel.Value)
                        {
                            try
                            {
                                var textmessage = (IUserMessage)((ITextChannel)Program.Client.GetChannel(channel.Key)).GetMessageAsync(message.Key).Result;
                                Program.ReactionHandler.AddHandler(textmessage, new Emoji("✅"), JoinGiveaway).Wait();
                                Program.ReactionHandler.AddHandler(textmessage, new Emoji("❎"), LeaveGiveaway).Wait();
                                Program.ReactionHandler.AddHandler(textmessage, new Emoji("🎁"), DrawGiveaway).Wait();

                                //Task.Run(async () =>
                                //{
                                foreach (var user in textmessage.GetReactionUsersAsync(new Emoji("✅"), 100).First().Result.Where(x => !x.IsBot))
                                {
                                    JoinGiveaway(user.Id, textmessage);
                                    textmessage.RemoveReactionAsync(new Emoji("✅"), user);
                                }
                                foreach (var user in textmessage.GetReactionUsersAsync(new Emoji("❎"), 100).First().Result.Where(x => !x.IsBot))
                                {
                                    LeaveGiveaway(user.Id, textmessage);
                                    textmessage.RemoveReactionAsync(new Emoji("❎"), user);
                                }
                                foreach (var user in textmessage.GetReactionUsersAsync(new Emoji("🎁"), 100).First().Result.Where(x => !x.IsBot))
                                {
                                    DrawGiveaway(user.Id, textmessage);
                                    textmessage.RemoveReactionAsync(new Emoji("🎁"), user);
                                }
                                //});
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e.Message);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message + e.StackTrace);
                }
            }
            Giveaways = Giveaways ?? new Dictionary<ulong, Dictionary<ulong, List<ulong>>>();
        }

        public void SaveJson()
        {
            using (StreamWriter write = new StreamWriter(new FileStream($"mopsdata//ReactionGiveaways.json", FileMode.Create)))
                write.Write(JsonConvert.SerializeObject(Giveaways, Formatting.Indented));
        }

        public async Task AddGiveaway(IMessageChannel channel, string name, IUser creator)
        {
            EmbedBuilder e = new EmbedBuilder();
            e.Title = name + " Giveaway!";
            e.Description = "To join/leave the giveaway, press the ✅/❎ Icons below this message!\n" +
                            "The Creator may draw a winner at any time, by pressing the 🎁 Icon.";
            e.Color = new Color(100, 100, 0);

            var author = new EmbedAuthorBuilder();
            author.Name = creator.Username;
            author.IconUrl = creator.GetAvatarUrl();

            e.Author = author;
            e.AddField("Participants", 0, true);
            e.AddField("Chance to win", Double.NaN, true);

            var message = await channel.SendMessageAsync("", embed: e.Build());
            await Program.ReactionHandler.AddHandler(message, new Emoji("✅"), JoinGiveaway);
            await Program.ReactionHandler.AddHandler(message, new Emoji("❎"), LeaveGiveaway);
            await Program.ReactionHandler.AddHandler(message, new Emoji("🎁"), DrawGiveaway);

            Dictionary<ulong, List<ulong>> messages = new Dictionary<ulong, List<ulong>>();
            List<ulong> participants = new List<ulong>();
            participants.Add(creator.Id);

            messages.Add(message.Id, participants);
            if (Giveaways.ContainsKey(channel.Id)) Giveaways[channel.Id].Add(message.Id, participants);
            else Giveaways.Add(channel.Id, messages);

            SaveJson();
        }

        private async Task JoinGiveaway(ReactionHandlerContext context)
        {
            if (!Giveaways[context.Channel.Id][context.Message.Id].First().Equals(context.Reaction.UserId))
            {
                Giveaways[context.Channel.Id][context.Message.Id].Add(context.Reaction.UserId);
                SaveJson();
                await updateMessage(context.Message);
            }
        }

        private async Task JoinGiveaway(ulong userId, IUserMessage message)
        {
            if (!Giveaways[message.Id][message.Id].First().Equals(userId) && !Giveaways[message.Id][message.Id].Contains(userId))
            {
                Giveaways[message.Channel.Id][message.Id].Add(userId);
                SaveJson();
                await updateMessage(message);
            }
        }

        private async Task LeaveGiveaway(ReactionHandlerContext context)
        {
            if (!Giveaways[context.Channel.Id][context.Message.Id].First().Equals(context.Reaction.UserId))
            {
                Giveaways[context.Channel.Id][context.Message.Id].Remove(context.Reaction.UserId);
                SaveJson();
                await updateMessage(context.Message);
            }
        }

        private async Task LeaveGiveaway(ulong userId, IUserMessage message)
        {
            if (!Giveaways[message.Channel.Id][message.Id].First().Equals(userId))
            {
                Giveaways[message.Channel.Id][message.Id].Remove(userId);
                SaveJson();
                await updateMessage(message);
            }
        }

        private async Task DrawGiveaway(ReactionHandlerContext context)
        {
            if (context.Reaction.UserId.Equals(Giveaways[context.Channel.Id][context.Message.Id].First()))
            {
                await Program.ReactionHandler.ClearHandler(context.Message);

                ulong winner = Giveaways[context.Channel.Id][context.Message.Id].Count > 1 ? Giveaways[context.Channel.Id][context.Message.Id]
                               [StaticBase.ran.Next(1, Giveaways[context.Channel.Id][context.Message.Id].Count)]
                               : context.Reaction.UserId;

                await context.Channel.SendMessageAsync($"{context.Channel.GetUserAsync(winner).Result.Mention} won the "
                                                      + $"`{context.Message.Embeds.First().Title}`");

                if (Giveaways[context.Channel.Id].Count == 1) Giveaways.Remove(context.Channel.Id);
                else Giveaways[context.Channel.Id].Remove(context.Message.Id);
                SaveJson();
            }
        }

        private async Task DrawGiveaway(ulong userId, IUserMessage message)
        {
            if (userId.Equals(Giveaways[message.Channel.Id][message.Id].First()))
            {
                await Program.ReactionHandler.ClearHandler(message);

                ulong winner = Giveaways[message.Channel.Id][message.Id].Count > 1 ? Giveaways[message.Channel.Id][message.Id]
                               [StaticBase.ran.Next(1, Giveaways[message.Channel.Id][message.Id].Count)]
                               : userId;

                await message.Channel.SendMessageAsync($"{message.Channel.GetUserAsync(winner).Result.Mention} won the "
                                                      + $"`{message.Embeds.First().Title}`");

                if (Giveaways[message.Channel.Id].Count == 1) Giveaways.Remove(message.Channel.Id);
                else Giveaways[message.Channel.Id].Remove(message.Id);
                SaveJson();
            }
        }

        private async Task updateMessage(IUserMessage message)
        {
            var e = message.Embeds.First().ToEmbedBuilder();

            e.Color = new Color(100, 100, 0);

            foreach (EmbedFieldBuilder field in e.Fields)
            {
                if (field.Name.Equals("Participants"))
                    field.Value = Giveaways[message.Channel.Id][message.Id].Count - 1;
                else
                    field.Value = Giveaways[message.Channel.Id][message.Id].Count > 1 ?
                                  Math.Round((1.0 / (Giveaways[message.Channel.Id][message.Id].Count - 1)) * 100, 2) + "%"
                                  : Double.NaN.ToString();
            }

            await message.ModifyAsync(x =>
            {
                x.Embed = e.Build();
            });
        }
    }
}