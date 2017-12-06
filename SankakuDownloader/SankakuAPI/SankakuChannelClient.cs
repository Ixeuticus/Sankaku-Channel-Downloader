using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SankakuAPI
{
    public class SankakuChannelClient
    {
        public string Username { get; set; }
        string PasswordHash { get; set; }
        HttpClientHandler handler;
        HttpClient client;

        public SankakuChannelClient()
        {
            InitializeClient();
        }

        void InitializeClient()
        {
            handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = true,
                CookieContainer = new CookieContainer()
            };
            client = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://chan.sankakucomplex.com/")
            };

            client.DefaultRequestHeaders.Host = "chan.sankakucomplex.com";
            client.DefaultRequestHeaders.Add("User-Agent", "SankakuChannelDownloader");
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/xml"));
        }

        public async Task<bool> Login(string username, string password)
        {
            var cnt = new FormUrlEncodedContent(new []
            {
                new KeyValuePair<string, string>("url", ""),
                new KeyValuePair<string, string>("user[name]", username),
                new KeyValuePair<string, string>("user[password]", password),
                new KeyValuePair<string, string>("commit", $"Login")
            });

            var msg = await client.PostAsync("user/authenticate", cnt);
            TransferCookies(msg);

            return msg.IsSuccessStatusCode;
        }

        public async Task Search(string tags)
        {
            int limit = 20;
            int page = 1;

            // for this use "text/html accept"
            var msg = await client.GetAsync($"post/index.json?login={Username}&password_hash={PasswordHash}&tags={tags}&page={page}");
            TransferCookies(msg);
            msg.EnsureSuccessStatusCode();

            var cnt2 = await msg.Content.ReadAsStringAsync();
        }

        void TransferCookies(HttpResponseMessage response)
        {
            response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string> cookies);
            if (cookies == null) return;
            foreach (var c in cookies)
            {
                if (c.StartsWith("login")) Username = c.Substring(6, c.IndexOf(';') - 6);
                else if (c.StartsWith("pass_hash")) PasswordHash = c.Substring(10, c.IndexOf(';') - 10);

                handler.CookieContainer.SetCookies(client.BaseAddress, c);
            }
        }
    }
}
