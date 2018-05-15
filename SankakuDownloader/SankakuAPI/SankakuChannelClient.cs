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
        public string PasswordHash { get; set; }

        #region Private Properties
        const string BaseURL = "https://capi-beta.sankakucomplex.com";
        string AppKey => sha1($"sankakuapp_{Username.ToLower()}_Z5NE9YASej");

        string Credentials => PasswordHash == null ? "" : $"&login={HttpUtility.UrlEncode(Username)}&password_hash={PasswordHash}&appkey={AppKey}";
        HttpClient client;
        #endregion


        public SankakuChannelClient(string username = null, string passwordhash = null)
        {
            InitializeClient();

            Username = username;
            PasswordHash = passwordhash;
        }

        public async Task<bool> Login(string username, string password)
        {
            var response = await client.PostAsync("/user/authenticate.json", new FormUrlEncodedContent(new Dictionary<string, string>()
            {
                { "user[name]", username },
                { "user[password]", password },
                { "appkey" , AppKey }

            }));
           
            var content = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode == false) throw new LoginException(content);

            var authresponse = JsonConvert.DeserializeObject<SankakuAuthResponse>(content);
            PasswordHash = authresponse?.PasswordHash;
            Username = authresponse?.CurrentUser?.Name;

            if (authresponse.Success == false) throw new LoginException(content);
            return authresponse.Success;
        }
        public async Task<List<SankakuPost>> Search(string query, int page = 1, int limit = 30)
        {
            if (page < 1) throw new NotSupportedException("Page count starts at 1");
            if (limit < 1) throw new NotSupportedException("Limit size must be at least 1");

            var tgs = HttpUtility.UrlEncode(query);
            var response = await client.GetAsync($"/post/index.json?limit={limit}&page={page}&tags={tgs}{Credentials}");
            // response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            if (content.ToLower().Contains("anonymous users can only view")) throw new UnauthorizedAccessException("Sign in to view more pages!");
            else if (response.IsSuccessStatusCode == false) throw new HttpRequestException(content);

            var posts = JsonConvert.DeserializeObject<List<SankakuPost>>(content);

            return posts;
        }

        public async Task<byte[]> DownloadImage(string url) => await client.GetByteArrayAsync(url);

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

        public static string sha1(string name)
        {
            var hash = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(name));
            string s = "";
            foreach (var b in hash)
            {
                int u = (b & 255) + 256;
                s += u.ToString("X").Substring(1);
            }

            return s.ToLower();
        }
    }

    public class LoginException : Exception {
        public LoginException(string msg) : base(msg) { }
    }
}
