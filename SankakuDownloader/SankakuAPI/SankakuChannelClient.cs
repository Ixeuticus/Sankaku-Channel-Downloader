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
            int limit = 100;
            int page = 1;

            // for this use "text/html accept"
            var msg = await client.GetAsync($"post/index.xml?tags={tags}"); // limit={limit}&page={page}
            TransferCookies(msg);
            msg.EnsureSuccessStatusCode();

            var cnt2 = await msg.Content.ReadAsStringAsync();
        }

        void TransferCookies(HttpResponseMessage response)
        {
            response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string> cookies);
            if (cookies == null) return;
            foreach (var c in cookies) handler.CookieContainer.SetCookies(client.BaseAddress, c);
        }
    }
}
