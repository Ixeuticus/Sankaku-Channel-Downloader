using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;

namespace SankakuAPI
{
    public class SankakuChannelClient
    {
        public string Username { get; set; }

        #region Private Properties
        const string BaseURL = "https://capi-beta.sankakucomplex.com";
        string PasswordHash { get; set; }
        string AppKey { get; set; }
        string Credentials => $"login={HttpUtility.UrlEncode(Username)}&password_hash={PasswordHash}&appkey={AppKey}";
        HttpClient client; 
        #endregion


        public SankakuChannelClient(string username, string password)
        {
            InitializeClient();

            Username = username;
            PasswordHash = sha1($"choujin-steiner--{password}--");
            AppKey = sha1($"sankakuapp_{Username.ToLower()}_Z5NE9YASej");
        }

        public async Task<List<SankakuPost>> Search(string query, int page = 1, int limit = 30)
        {
            if (page < 1) throw new NotSupportedException("Page count starts at 1");
            if (limit < 1) throw new NotSupportedException("Limit size must be at least 1");

            var tgs = HttpUtility.UrlEncode(query);
            var response = await client.GetAsync($"/post/index.json?limit={limit}&page={page}&tags={tgs}&{Credentials}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var posts = JsonConvert.DeserializeObject<List<SankakuPost>>(content);

            return posts; 
        }

        public async Task<byte[]> DownloadImage(string url) => await client.GetByteArrayAsync(url);
        
        string sha1(string text)
        {
            var hash = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(text));

            string result = "";
            foreach(var b in hash) result += int.Parse((
                (b & 255) + 256).ToString(), NumberStyles.HexNumber)
                .ToString().Substring(1);
            
            return result;
        }
        void InitializeClient()
        {
            client = new HttpClient()
            {
                BaseAddress = new Uri(BaseURL)
            };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/66.0.3359.139 Safari/537.36");
            client.DefaultRequestHeaders.Add("Referer", "https://beta.sankakucomplex.com");
            client.DefaultRequestHeaders.Add("Origin", "https://beta.sankakucomplex.com");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        }
    }
}
