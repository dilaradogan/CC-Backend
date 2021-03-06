﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using CommunicaptionBackend.Core;
using CommunicaptionBackend.Entities;
using CommunicaptionBackend.Messages;
using CommunicaptionBackend.Wrappers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CommunicaptionBackend.Api {

    public class MainService {
        private readonly MainContext mainContext;
        private readonly MessageQueue messageQueue;
        private readonly MessageProcessor messageProcessor;
        private readonly LuceneProcessor luceneProcessor;
        private static readonly HttpClient client = new HttpClient();

        private const string RECOMMENDER_HOST = "http://37.148.210.36:8082";


        public MainService(MainContext mainContext, MessageProcessor messageProcessor, MessageQueue messageQueue, LuceneProcessor luceneProcessor) {
            this.mainContext = mainContext;
            this.messageProcessor = messageProcessor;
            this.messageQueue = messageQueue;
            this.luceneProcessor = luceneProcessor;
        }

        public void ClearDb() {
            mainContext.Arts.RemoveRange(from c in mainContext.Arts select c);
            mainContext.Users.RemoveRange(from c in mainContext.Users select c);
            mainContext.Medias.RemoveRange(from c in mainContext.Medias select c);
            mainContext.Settings.RemoveRange(from c in mainContext.Settings select c);
            mainContext.Texts.RemoveRange(from c in mainContext.Texts select c);
            mainContext.SaveChanges();
        }

        public void SetDump(DumpWrapper dumpWrapper) {
            ClearDb();

            mainContext.Arts.AddRange(dumpWrapper.Arts);
            //mainContext.Users.AddRange(dumpWrapper.Users);
            mainContext.Medias.AddRange(dumpWrapper.Medias);
            mainContext.Settings.AddRange(dumpWrapper.Settings);
            mainContext.Texts.AddRange(dumpWrapper.Texts);

            if (!Directory.Exists("medias"))
                Directory.CreateDirectory("medias");
            if (!Directory.Exists("thumbnails"))
                Directory.CreateDirectory("thumbnails");
            int id = 1;
            foreach (var item in dumpWrapper.MediaBase64) {
                string filename = id + "";
                string saveImagePath = ("medias/") + filename;
                File.WriteAllBytes(saveImagePath, Convert.FromBase64String(item));

                string saveThumbnPath = ("thumbnails/") + filename + ".jpg";
                File.WriteAllBytes(saveThumbnPath, Convert.FromBase64String(item));

                id++;
            }
            mainContext.SaveChanges();
        }

        public void DisconnectDevice(int userId) {
            var user = mainContext.Users.SingleOrDefault(x => userId == x.Id);
            if (user == null)
                return;
            user.Connected = false;
            mainContext.SaveChanges();
        }

        public string GeneratePin() {
            var user = new UserEntity {
                Pin = Guid.NewGuid().ToString().Replace("-", "").Substring(0, 8),
                Connected = false
            };

            mainContext.Users.Add(user);
            mainContext.SaveChanges();
            return user.Pin;
        }

        public byte[] GetMediaData(string mediaId) {
            if (mediaId == "0")
                return null;
            return File.ReadAllBytes("medias/" + mediaId);
        }

        public object getDetails(int artId) {
            var artInfo = mainContext.Arts.FirstOrDefault(x => x.Id == artId);

            var mediaItems = GetMediaItemsOfArt(artInfo.Id);
            var textsRelatedToArt = mainContext.Texts.Where(x => x.ArtId == artId).ToList();
            string ocrText = "";

            var wiki = artInfo.link;

            foreach (var textObject in textsRelatedToArt) {
                ocrText += @" " + textObject.Text;
            }

            var recommendationsList = new List<object>();

            int[] recommedArr = Recommend(artInfo.UserId, artId);
            Console.Error.WriteLine(JsonConvert.SerializeObject(recommedArr));
            for (int i = 0; i < recommedArr.Length; i++) {
                int j = recommedArr[i];
                var artInf = mainContext.Arts.FirstOrDefault(x => x.Id == j);
                int artId_ = artInf.Id;
                var mediaInfo = mainContext.Medias.FirstOrDefault(x => x.ArtId == artId_);
                object obj = new {
                    picture = mediaInfo == null ? "" : Convert.ToBase64String(GetMediaData(mediaInfo.Id + "")),
                    url = artInf.link
                };

                recommendationsList.Add(obj);
            }
            Console.Error.WriteLine("---1---");

            int[] recommedArrL = LocationBasedRecommendation(artInfo.Latitude, artInfo.Longitude);
            Console.Error.WriteLine(JsonConvert.SerializeObject(recommedArrL));

            for (int i = 0; i < recommedArrL.Length; i++) {
                int j = recommedArrL[i];
                var artInf = mainContext.Arts.FirstOrDefault(x => x.Id == j);
                int artId_ = artInf.Id;
                var mediaInfo = mainContext.Medias.FirstOrDefault(x => x.ArtId == artId_);
                Console.Error.WriteLine(JsonConvert.SerializeObject(mediaInfo));
                object obj = new {
                    picture = mediaInfo == null ? "" : Convert.ToBase64String(GetMediaData(mediaInfo.Id + "")),
                    url = artInf.link
                };
                recommendationsList.Add(obj);
            }
            Console.Error.WriteLine("---2---");

            recommendationsList = recommendationsList.Distinct().Take(3).ToList();

            object[] objlist = new object[] {
                new {items = mediaItems, text = ocrText, wikipedia = wiki, recommendations = recommendationsList}
            };

            return objlist;
        }

        public object Dump() {
            return new {
                Users = mainContext.Users.ToList(),
                Medias = mainContext.Medias.ToList(),
                Settings = mainContext.Settings.ToList(),
                Texts = mainContext.Texts.ToList(),
                Arts = mainContext.Arts.ToList(),
            };
        }

        public object getSearchResult(string searchInputJson) {
            var artList = luceneProcessor.getArtList(mainContext.Texts.ToList());
            luceneProcessor.AddToTheIndex(artList);
            return luceneProcessor.FetchResults(searchInputJson);
        }

        public List<object> GetMediaItems(int userId, bool onlyHoloLens) {
            var itemInformations = new List<object>();
            var medias = mainContext.Medias.Where(x => x.UserId == userId || (onlyHoloLens));
            foreach (var media in medias) {
                if (onlyHoloLens && media.ArtId != 0)
                    continue;

                object obj = new {
                    mediaId = media.Id,
                    fileName = JsonConvert.SerializeObject(media.DateTime),
                    thumbnail = File.ReadAllBytes("thumbnails/" + media.Id.ToString() + ".jpg")
                };
                itemInformations.Add(obj);
            }
            return itemInformations;
        }

        public List<object> GetMediaItemsOfArt(int artId) {
            var itemInformations = new List<object>();
            var medias = mainContext.Medias.Where(x => x.ArtId == artId);
            foreach (var media in medias) {
                object obj = new {
                    mediaId = media.Id,
                    fileName = JsonConvert.SerializeObject(media.DateTime),
                    thumbnail = File.ReadAllBytes("thumbnails/" + media.Id.ToString() + ".jpg")
                };
                itemInformations.Add(obj);
            }
            return itemInformations;
        }

        public void PushMessage(Message message) {
            if (message is SaveMediaMessage) {
                messageProcessor.ProcessMessage(message);
            }
            else if (message is SaveTextMessage) {
                messageProcessor.ProcessMessage(message);
            }
            else if (message is SettingsChangedMessage) {
                messageProcessor.ProcessMessage(message);
            }
            else {
                messageQueue.PushMessage(message);
            }
        }

        public List<Message> GetMessages(int userId) {
            return messageQueue.SwapQueue(userId);
        }

        public int CheckForPairing(string pin) {
            var user = mainContext.Users.SingleOrDefault(x => x.Pin == pin);
            if (user == null)
                return 0;
            return user.Id;
        }

        public bool CheckUserExists(int userId) {
            var user = mainContext.Users.SingleOrDefault(x => x.Id == userId);
            if (user == null)
                return false;
            return true;
        }

        public int ConnectWithoutHoloLens() {
            UserEntity user = new UserEntity {
                Pin = Guid.NewGuid().ToString().Replace("-", "").Substring(0, 8),
                Connected = false
            };

            mainContext.Users.Add(user);
            mainContext.SaveChanges();

            return user.Id;
        }

        public int ConnectWithHoloLens(string pin) {
            int userId = CheckForPairing(pin);

            if (userId == 0)
                return 0;
            else {
                var user = mainContext.Users.SingleOrDefault(x => userId == x.Id);
                user.Connected = true;

                mainContext.Users.Update(user);
                mainContext.SaveChanges();

                return userId;
            }
        }

        public string GetUserSettings(int userId) {
            var settings = mainContext.Settings.FirstOrDefault(x => x.UserId == userId);
            if (settings == null)
                return JsonConvert.SerializeObject(new SettingsChangedMessage());
            return settings.Json;
        }

        public List<GalleryResponseItem> GetGallery(int userId) {
            var arts = mainContext.Arts.Where(x => x.UserId == userId || x.UserId == 0).ToList();

            var result = new List<GalleryResponseItem>();

            foreach (var art in arts) {
                var medias = mainContext.Medias.Where(x => x.ArtId == art.Id).ToArray();
                var texts = mainContext.Texts.Where(x => x.ArtId == art.Id).ToArray();

                byte[] picture = GetMediaData((medias.FirstOrDefault()?.Id ?? 0) + "");
                string text = string.Join(" ", texts.Select(x => x.Text));

                result.Add(new GalleryResponseItem {
                    artId = art.Id,
                    title = art.Title,
                    picture = picture,
                    text = text
                });
            }

            return result;
        }

        public int CreateArt(int userId, string artTitle) {

            var art = new ArtEntity();
            art.Title = artTitle;
            art.UserId = userId;
            Random rand = new Random();
            art.Latitude = (float)(rand.NextDouble());
            art.Longitude = (float)(rand.NextDouble());
            art.link = GetWikipediaLink(artTitle).Result;

            mainContext.Arts.Add(art);
            mainContext.SaveChanges();
            return art.Id;
        }

        private int[] ArtsSimilar(string title) {
            title = title.ToLowerInvariant();
            var res = new List<int>();
            foreach (var item in mainContext.Arts.Select(x => new { x.Id, x.Title }).ToList()) {
                string t = item.Title.ToLowerInvariant();
                if (t.Contains(title)) {
                    res.Add(item.Id);
                }
            }
            return res.ToArray();
        }

        private Tuple<int, string> GetDocumentFromArt(int artId) {
            var all = mainContext.Texts.Where(x => x.ArtId == artId)
                .Select(x => x.Text)
                .Select(x => x.Replace("\r", "").Replace("\n", " ")).ToList();
            var s = string.Join(" ", all).Trim();
            return new Tuple<int, string>(artId, s);
        }

        public void TriggerTrain() {
            var web = new WebClient();
            web.Proxy = null;
            web.Headers[HttpRequestHeader.ContentType] = "application/json";

            var arts = mainContext.Arts.Select(x => new { x.Id, x.UserId, x.Title });

            var ratings = new List<int[]>();
            foreach (var item in arts) {
                ratings.Add(new int[2] { item.UserId, item.Id });
            }
            web.UploadString($"{RECOMMENDER_HOST}/ch1/train/", "POST", JsonConvert.SerializeObject(ratings));

            var docs = arts.Select(x => GetDocumentFromArt(x.Id));
            web.Headers[HttpRequestHeader.ContentType] = "application/json";
            web.UploadString($"{RECOMMENDER_HOST}/ch2/train/", "POST", JsonConvert.SerializeObject(new {
                Item1 = docs.Select(x => x.Item1).ToArray(),
                Item2 = docs.Select(x => x.Item2).ToArray(),
            }));
        }

        public int[] Recommend(int userId, int baseArtId) {
            /*var random = new Random();
            int length = random.Next(9, 14);
            var arts = mainContext.Arts.Select(x => x.Id).ToList();

            var list = new List<int>();
            for (int i = 0; i < length; i++) {
                var j = arts[random.Next(0, arts.Count)];
                if (j != baseArtId) {
                    list.Add(j);
                }
            }
            list = list.Distinct().ToList();
            return list.ToArray();*/

            try {
                var artTitle = mainContext.Arts.FirstOrDefault(x => x.Id == baseArtId)?.Title;
                if (artTitle == null) artTitle = ".";

                var web = new WebClient();
                web.Proxy = null;
                web.Headers[HttpRequestHeader.ContentType] = "application/json";

                var channel1 = JsonConvert.DeserializeObject<int[]>(web.DownloadString($"{RECOMMENDER_HOST}/ch1/recommend/{userId}/{baseArtId}"));
                web = new WebClient();
                web.Headers[HttpRequestHeader.ContentType] = "application/json";
                var channel2 = JsonConvert.DeserializeObject<int[]>(web.DownloadString($"{RECOMMENDER_HOST}/ch2/similarity/{userId}"));
                web = new WebClient();
                web.Headers[HttpRequestHeader.ContentType] = "application/json";

                var alsoSearch = JsonConvert.DeserializeObject<string[]>(web.UploadString($"{RECOMMENDER_HOST}/ch3/predict", "POST", JsonConvert.SerializeObject(artTitle)));
                var channel3 = alsoSearch.Select(x => ArtsSimilar(x));

                var all = new List<int>();
                all.AddRange(channel1);
                all.AddRange(channel2);
                foreach (var item in channel3) {
                    all.AddRange(item);
                }

                return all.Distinct().ToArray();

            }
            catch (WebException exception) {
                string responseText = "<null>";

                var responseStream = exception.Response?.GetResponseStream();

                if (responseStream != null) {
                    using (var reader = new StreamReader(responseStream)) {
                        responseText = reader.ReadToEnd();
                    }
                }

                Console.Error.WriteLine(responseText);
                throw;
            }
        }

        public string TrainDebug() {
            var web = new WebClient();
            web.Proxy = null;
            web.Headers[HttpRequestHeader.ContentType] = "application/json";
            return web.DownloadString($"{RECOMMENDER_HOST}/ch1/info");
        }

        public int[] LocationBasedRecommendation(float latitude, float longitude) {
            var info = mainContext.Arts.Select(x => new { x.Id, x.Latitude, x.Longitude }).ToList();
            List<double[]> sortedList = new List<double[]>();

            for (int i = 0; i < info.Count; i++) {
                double distance = Math.Pow(latitude - info[i].Latitude, 2) + Math.Pow(longitude - info[i].Longitude, 2);
                sortedList.Add(new double[] { info[i].Id, distance });
            }

            sortedList.OrderBy(x => x[1]);
            return sortedList.Take(5).Select(x => Convert.ToInt32(x[0])).ToArray();
        }

        public async Task<string> GetWikipediaLink(string title) {
            return "OF ne keymiş ya";
            var response = await client.GetStringAsync($"https://app.zenserp.com/api/v2/search?apikey=72e89f30-8369-11ea-b8c1-27fe76fdaa39&q={title}&lr=lang_tr&hl=tr&location=Turkey&gl=tr");
            var x = JObject.Parse(response);
            string[] result = x["organic"].Where(x => x["title"] != null && x["title"].ToString().Contains("Vikipedi")).Select(x => x["url"].ToString()).ToArray();

            if (result.Length != 0)
                return result[0];
            else
                return x["query"]["url"].ToString();
        }
    }
}
