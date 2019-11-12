﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using EnhancedStreamChat.Chat;
using System.Text.RegularExpressions;
using EnhancedStreamChat.UI;
using Random = System.Random;
using EnhancedStreamChat.Images;
using StreamCore;
using EnhancedStreamChat.Config;
using EnhancedStreamChat.Textures;
using StreamCore.Chat;
using StreamCore.YouTube;
using StreamCore.Twitch;

namespace EnhancedStreamChat.Chat
{
    public class MessageParser : MonoBehaviour
    {
        private static readonly Regex _emoteRegex = new Regex(@"(?<EmoteIndex>[a-zA-Z0-9_-]+):(?<StartIndex>[0-9]+)-(?<EndIndex>[0-9]+)", RegexOptions.Compiled);
        private static readonly Regex _badgeRegex = new Regex(@"(?<BadgeName>[^,\/]+)\/(?<BadgeVersion>[^,;]+)", RegexOptions.Compiled);

        private static Dictionary<int, string> _userColors = new Dictionary<int, string>();
        public static async void Parse(ChatMessage newChatMessage)
        {
            // Setup local variables
            char swapChar = (char)0xE000;
            List<EmoteInfo> parsedEmotes = new List<EmoteInfo>();
            List<BadgeInfo> parsedBadges = new List<BadgeInfo>();

            bool isActionMessage = false;
            if (newChatMessage.origMessage is TwitchMessage)
            {
                // If this is a twitch message, check if they used /me and color it accordingly
                isActionMessage = newChatMessage.displayMsg.Substring(1).StartsWith("ACTION") && newChatMessage.displayMsg[0] == (char)0x1;
                if (isActionMessage)
                {
                    newChatMessage.displayMsg = newChatMessage.displayMsg.TrimEnd((char)0x1).Substring(8);
                }
            }

            StringBuilder emojilessMessageBuilder = new StringBuilder(newChatMessage.displayMsg);
            // Parse and download any emojis included in the message
            var matches = EmojiUtilities.GetEmojisInString(newChatMessage.displayMsg);
            if (matches.Count > 0)
            {
                List<string> foundEmojis = new List<string>();
                foreach (Match m in matches)
                {
                    string emojiIndex = EmojiUtilities.WebParseEmojiRegExMatchEvaluator(m);
                    string replaceString = m.Value;

                    // Build up a copy of the message with no emojis so we can parse out our twitch emotes properly
                    emojilessMessageBuilder = emojilessMessageBuilder.Replace(m.Value, " ");

                    if (emojiIndex != String.Empty)
                    {
                        emojiIndex += ".png";
                        if (!ImageDownloader.CachedTextures.ContainsKey(emojiIndex))
                            ImageDownloader.Instance.Queue(new TextureDownloadInfo(emojiIndex, ImageType.Emoji, newChatMessage.origMessage.id));

                        if (!foundEmojis.Contains(emojiIndex))
                        {
                            foundEmojis.Add(emojiIndex);
                            EmoteInfo swapInfo = new EmoteInfo();
                            swapInfo.imageType = ImageType.Emoji;
                            swapInfo.isEmoji = true;
                            swapInfo.swapChar = swapChar;
                            swapInfo.swapString = replaceString;
                            swapInfo.textureIndex = emojiIndex;
                            parsedEmotes.Add(swapInfo);
                            swapChar++;
                        }
                    }
                }
                parsedEmotes = parsedEmotes.OrderByDescending(o => o.swapString.Length).ToList();
                Thread.Sleep(5);
            }

            if (newChatMessage.origMessage is TwitchMessage)
            {
                // Parse and download any twitch emotes in the message
                var emotes = _emoteRegex.Matches(newChatMessage.origMessage.Twitch.emotes);
                if (emotes.Count > 0)
                {
                    string emojilessMessage = emojilessMessageBuilder.ToString();
                    foreach (Match e in emotes)
                    {
                        string emoteIndex = $"T{e.Groups["EmoteIndex"].Value}";
                        if (!ImageDownloader.CachedTextures.ContainsKey(emoteIndex))
                            ImageDownloader.Instance.Queue(new TextureDownloadInfo(emoteIndex, ImageType.Twitch, newChatMessage.origMessage.id));

                        int startReplace = Convert.ToInt32(e.Groups["StartIndex"].Value);
                        int endReplace = Convert.ToInt32(e.Groups["EndIndex"].Value);
                        string emoteName = emojilessMessage.Substring(startReplace, endReplace - startReplace + 1);

                        EmoteInfo swapInfo = new EmoteInfo();
                        swapInfo.swapChar = swapChar;
                        swapInfo.swapString = emoteName;
                        swapInfo.textureIndex = emoteIndex;
                        swapInfo.imageType = ImageType.Twitch;
                        parsedEmotes.Add(swapInfo);
                        swapChar++;
                    }
                    Thread.Sleep(5);
                }

                // Parse and download any twitch badges included in the message
                var badges = _badgeRegex.Matches(newChatMessage.origMessage.user.Twitch.badges);
                if (badges.Count > 0)
                {
                    foreach (Match b in badges)
                    {
                        string badgeName = $"{b.Groups["BadgeName"].Value}{b.Groups["BadgeVersion"].Value}";
                        string badgeIndex = string.Empty;
                        if (ImageDownloader.TwitchBadgeIDs.ContainsKey(badgeName))
                        {
                            badgeIndex = ImageDownloader.TwitchBadgeIDs[badgeName];
                            if (!ImageDownloader.CachedTextures.ContainsKey(badgeIndex))
                                ImageDownloader.Instance.Queue(new TextureDownloadInfo(badgeIndex, ImageType.Badge, newChatMessage.origMessage.id));

                            BadgeInfo swapInfo = new BadgeInfo();
                            swapInfo.swapChar = swapChar;
                            swapInfo.textureIndex = badgeIndex;
                            swapInfo.imageType = ImageType.Badge;
                            parsedBadges.Add(swapInfo);
                            swapChar++;
                        }
                    }
                    Thread.Sleep(5);
                }

                // Parse and download any BTTV/FFZ emotes and cheeremotes in the message
                string[] msgParts = newChatMessage.displayMsg.Split(' ').Distinct().ToArray();
                foreach (string w in msgParts)
                {
                    string word = w;
                    //Plugin.Log($"WORD: {word}");
                    string textureIndex = String.Empty;
                    ImageType imageType = ImageType.None;
                    if (ImageDownloader.BTTVEmoteIDs.ContainsKey(word))
                    {
                        if (ChatConfig.Instance.ShowBTTVEmotes)
                        {
                            textureIndex = $"B{ImageDownloader.BTTVEmoteIDs[word]}";
                            imageType = ImageType.BTTV;
                        }
                    }
                    else if (ImageDownloader.BTTVAnimatedEmoteIDs.ContainsKey(word))
                    {
                        if (ChatConfig.Instance.ShowBTTVEmotes)
                        {
                            textureIndex = $"AB{ImageDownloader.BTTVAnimatedEmoteIDs[word]}";
                            imageType = ImageType.BTTV_Animated;
                        }
                    }
                    else if (ImageDownloader.FFZEmoteIDs.ContainsKey(word))
                    {
                        if (ChatConfig.Instance.ShowFFZEmotes)
                        {
                            textureIndex = $"F{ImageDownloader.FFZEmoteIDs[word]}";
                            imageType = ImageType.FFZ;
                        }
                    }
                    else if (newChatMessage.origMessage.Twitch.bits > 0 && EmojiUtilities.cheermoteRegex.IsMatch(word.ToLower()))
                    {
                        Match match = EmojiUtilities.cheermoteRegex.Match(word.ToLower());
                        string prefix = match.Groups["Prefix"].Value;
                        if (ImageDownloader.TwitchCheermoteIDs.ContainsKey(prefix))
                        {
                            int bits = Convert.ToInt32(match.Groups["Value"].Value);
                            string tier = ImageDownloader.TwitchCheermoteIDs[prefix].GetTier(bits);
                            textureIndex = $"{prefix}{tier}";
                            imageType = ImageType.Cheermote;
                        }
                    }

                    if (imageType != ImageType.None)
                    {
                        if (!ImageDownloader.CachedTextures.ContainsKey(textureIndex))
                            ImageDownloader.Instance.Queue(new TextureDownloadInfo(textureIndex, imageType, newChatMessage.origMessage.id));

                        EmoteInfo swapInfo = new EmoteInfo();
                        swapInfo.imageType = imageType;
                        swapInfo.swapChar = swapChar;
                        swapInfo.swapString = word;
                        swapInfo.textureIndex = textureIndex;
                        parsedEmotes.Add(swapInfo);
                        swapChar++;
                    }
                }

                
                Thread.Sleep(5);
            }
            else if(newChatMessage.origMessage is YouTubeMessage)
            {
                if (!ImageDownloader.CachedTextures.ContainsKey(newChatMessage.origMessage.user.YouTube.profileImageUrl))
                    ImageDownloader.Instance.Queue(new TextureDownloadInfo(newChatMessage.origMessage.user.YouTube.profileImageUrl, ImageType.YouTube_Profile, newChatMessage.origMessage.id, true));

                BadgeInfo swapInfo = new BadgeInfo();
                swapInfo.swapChar = swapChar;
                swapInfo.textureIndex = newChatMessage.origMessage.user.YouTube.profileImageUrl;
                swapInfo.imageType = ImageType.YouTube_Profile;
                parsedBadges.Add(swapInfo);
                swapChar++;
            }

            string[] parts = newChatMessage.displayMsg.Split(' ');
            // Replace each emote with a unicode character from a private range; we'll draw the emote at the position of this character later on
            foreach (EmoteInfo e in parsedEmotes.Where(e => !e.isEmoji))
            {
                var cachedSprite = e.GetCachedSprite();
                string extraInfo = String.Empty;
                if (e.imageType == ImageType.Cheermote)
                {
                    try
                    {
                        // Insert an animated cheermote into the message
                        Match cheermote = EmojiUtilities.cheermoteRegex.Match(e.swapString);
                        if (cheermote.Success)
                        {
                            string numBits = cheermote.Groups["Value"].Value;
                            string prefix = cheermote.Groups["Prefix"].Value.ToLower();
                            extraInfo = $"\u200A<color={ImageDownloader.TwitchCheermoteIDs[prefix].GetColor(Convert.ToInt32(numBits))}><size=4><b>{numBits}</b></size></color>\u200A";
                        }
                        else
                        {
                            Plugin.Log($"Cheermote regex failed for message {newChatMessage.origMessage}");
                        }
                    }
                    catch(Exception ex)
                    {
                        Plugin.Log($"Failed to parse cheermote! {ex.ToString()}");
                    }
                }

                // Replace any instances of the swapString we find in the message
                string replaceString = $"\ufeff{Char.ConvertFromUtf32(e.swapChar)}{e.spacingString}{extraInfo}";
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i] == e.swapString)
                        parts[i] = replaceString;
                }
            }

            // Then replace our emojis after all the emotes are handled, since these aren't sensitive to spacing
            StringBuilder sb = new StringBuilder(string.Join(" ", parts));
            foreach (EmoteInfo e in parsedEmotes.Where(e => e.isEmoji))
            {
                var cachedSprite = e.GetCachedSprite();
                sb.Replace(e.swapString, $"\ufeff{Char.ConvertFromUtf32(e.swapChar)}{e.spacingString}");
            }
            newChatMessage.displayMsg = sb.ToString();

            Thread.Sleep(5);

            //// TODO: Re-add tagging, why doesn't unity have highlighting in its default rich text markup?
            //// Highlight messages that we've been tagged in
            //if (Plugin._twitchUsername != String.Empty && msg.Contains(Plugin._twitchUsername)) {
            //    msg = $"<mark=#ffff0050>{msg}</mark>";
            //}
            
            string displayColor = newChatMessage.origMessage.user.color;
            int nameHash = newChatMessage.origMessage.user.displayName.GetHashCode();
            // If the user doesn't already have a displayColor, generate one and store it for later user
            if (string.IsNullOrEmpty(displayColor) && !_userColors.TryGetValue(nameHash, out displayColor))
            {
                // Generate a random color
                Random rand = new Random(nameHash);
                int r = rand.Next(255);
                int g = rand.Next(255);
                int b = rand.Next(255);

                // Convert it to a pastel color
                System.Drawing.Color pastelColor = Drawing.GetPastelShade(System.Drawing.Color.FromArgb(255, r, g, b));
                int argb = ((int)pastelColor.R << 16) + ((int)pastelColor.G << 8) + (int)pastelColor.B;
                string colorString = String.Format("#{0:X6}", argb) + "FF";
                _userColors.Add(nameHash, colorString);
                displayColor = colorString;
            }
            // Set the final displayColor to our message
            newChatMessage.displayColor = displayColor;

            // Add the users name to the message with the correct color
            newChatMessage.displayMsg = $"<color={newChatMessage.displayColor}><b>{newChatMessage.origMessage.user.displayName}</b>{(!isActionMessage?"</color>:":"")} {newChatMessage.displayMsg}{(isActionMessage?"</color>":"")}";

            // Prepend the users badges to the front of the message
            StringBuilder badgeStr = new StringBuilder();
            if (parsedBadges.Count > 0)
            {
                parsedBadges.Reverse();
                foreach (BadgeInfo b in parsedBadges)
                {
                    var cachedSprite = b.GetCachedSprite();
                    badgeStr.Insert(0, $"{Char.ConvertFromUtf32(b.swapChar)}{b.spacingString}\u00A0");
                }
            }
            badgeStr.Insert(0, "\uFEFF");
            badgeStr.Append(newChatMessage.displayMsg);


            // Finally, store our final message, parsedEmotes and parsedBadges; then render the message
            newChatMessage.displayMsg = badgeStr.ToString();
            newChatMessage.parsedEmotes = parsedEmotes;
            newChatMessage.parsedBadges = parsedBadges;

            ChatHandler.RenderQueue.Enqueue(newChatMessage);
        }
    };
}
