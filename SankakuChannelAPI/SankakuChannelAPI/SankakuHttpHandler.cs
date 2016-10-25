using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SankakuChannelAPI
{
    static class SankakuHttpHandler

    {
        public static bool LoginUser(string username, string password, out string cfduid, out string sankakuID, out string passwordHash, out bool tooManyRequests, out string actualUsername)
        {
            passwordHash = null; cfduid = null; sankakuID = null; tooManyRequests = false; actualUsername = null;
            try
            {
                // Create cookie ID request
                var cookieRequest = (HttpWebRequest)WebRequest.Create("https://chan.sankakucomplex.com/");
                cookieRequest.Method = "GET";
                cookieRequest.Headers.Add("Upgrade-Insecure-Requests: 1");
                cookieRequest.Headers.Add("Accept-Encoding: gzip, deflate, sdch, br");
                cookieRequest.Headers.Add("Accept-Language: en-US,en;q=0.8,sl;q=0.6");
                cookieRequest.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/53.0.2785.143 Safari/537.36";
                cookieRequest.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8";
                cookieRequest.Timeout = 10 * 1000;
                // Get cookie ID response
                var cookieResponse = (HttpWebResponse)cookieRequest.GetResponse();
                var cookieValue = cookieResponse.GetResponseHeader("Set-Cookie");
                cookieResponse.Close();
                cfduid = new Regex(@"__cfduid=(.*?);").Match(cookieValue).Groups[1].Value;

                // Create Login request
                var request = (HttpWebRequest)WebRequest.Create("https://chan.sankakucomplex.com/user/authenticate");
                request.Method = "POST";
                request.AllowAutoRedirect = false;
                request.Headers.Add("Origin", "https://chan.sankakucomplex.com");
                request.Headers.Add("Cache-Control", "max-age=0");
                request.Headers.Add("Upgrade-Insecure-Requests", "1");
                request.Headers.Add("Accept-Encoding", "gzip, deflate, br");
                request.Headers.Add("Accept-Language", "en-US,en;q=0.8,sl;q=0.6");
                request.Host = "chan.sankakucomplex.com";
                request.Referer = "https://chan.sankakucomplex.com/user/login";
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/53.0.2785.143 Safari/537.36";
                request.ContentType = "application/x-www-form-urlencoded";
                request.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8";
                request.Headers.Add("Cookie", $"__cfduid={cfduid}; auto_page=1; blacklisted_tags=; locale=en");
                string content = $"url=&user%5Bname%5D={username}&user%5Bpassword%5D={password}&commit=Login";
                
                var contentBytes = Encoding.ASCII.GetBytes(content);
                request.ContentLength = contentBytes.Length;
                var reqStream = request.GetRequestStream();
                reqStream.Write(contentBytes, 0, contentBytes.Length);
                reqStream.Close();
                request.Timeout = 10 * 1000;

                // get login data
                var response = (HttpWebResponse)request.GetResponse();
                var val = response.GetResponseHeader("Set-Cookie");
                response.Close();

                passwordHash = new Regex(@"pass_hash=(.*?);").Match(val).Groups[1].Value;
                actualUsername = new Regex(@"login=(.*?);").Match(val).Groups[1].Value;
                sankakuID = new Regex(@"_sankakucomplex_session=(.*?);").Match(val).Groups[1].Value;

                if (passwordHash.Length < 2) return false;
                return true;
            }
            catch(WebException ex)
            {
                if (ex.Message.ToLower().Contains("too many requests"))
                {
                    tooManyRequests = true;
                    return false;
                }
                else throw ex;
            }
            catch { return false; }
        }
        public static bool SendQuery(SankakuChannelUser user, string query, out List<SankakuPost> results, int page = 1, int limit = 15)
        {
            results = new List<SankakuPost>();
            try
            {
                string convertedQuery = query.Replace(" ", "+");
                var request = (HttpWebRequest)WebRequest.Create($"https://chan.sankakucomplex.com/?tags={convertedQuery}&limit={limit}&page={page}");
                request.Method = "GET";
                request.Headers.Add("Cache-Control", "max-age=0");
                request.Headers.Add("Upgrade-Insecure-Requests", "1");
                request.Host = "chan.sankakucomplex.com";
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/53.0.2785.143 Safari/537.36";
                request.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8";
                request.Headers.Add("Accept-Encoding", "gzip, deflate, sdch, br");
                request.Headers.Add("Accept-Language", "en-US,en;q=0.8,sl;q=0.6");
                request.Headers.Add("Cookie", $"__cfduid={user.cfduid}; login={user.Username}; pass_hash={user.PasswordHash}; " +
                    $"__atuvc=24%7C43; __atuvs=580cc97684a60c23003; mode=view; auto_page=1; " +
                    $"blacklisted_tags=full-package_futanari&futanari; locale=en; _sankakucomplex_session={user.SankakuComplexSessionID}");

                var response = (HttpWebResponse)request.GetResponse();
                var content = Encoding.UTF8.GetString(GZipDecompress(response.GetResponseStream()));
                response.Close();

                Regex rgx = new Regex(@"<span class="".*?"" id=.*?><a href=""\/post\/show\/(.*?)"" onclick="".*?"">" +
                    @"<img class=.*? src=""(.*?)"" title=""(.*?)"".*?><\/a><\/span>", RegexOptions.Singleline);

                foreach (Match m in rgx.Matches(content))
                {
                    try
                    {
                        results.Add(new SankakuPost(
                            int.Parse(m.Groups[1].Value),
                            "https://chan.sankakucomplex.com/post/show/" + m.Groups[1].Value,
                            "http:" + m.Groups[2].Value,
                            m.Groups[3].Value.Split(' '), 
                            user));
                    }
                    catch { }
                }

                return true;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        public static string GetImageLink(SankakuChannelUser user, string postReference)
        {
            var request = (HttpWebRequest)WebRequest.Create(postReference);
            request.Method = "GET";

            request.Headers.Add("Cache-Control", "max-age=0");
            request.Headers.Add("Upgrade-Insecure-Requests", "1");
            request.Host = "chan.sankakucomplex.com";
            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/53.0.2785.143 Safari/537.36";
            request.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8";
            request.Headers.Add("Accept-Encoding", "gzip, deflate, sdch, br");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.8,sl;q=0.6");
            request.Headers.Add("Cookie", $"__cfduid={user.cfduid}; login={user.Username}; pass_hash={user.PasswordHash}; " +
               $"__atuvc=24%7C43; __atuvs=580cc97684a60c23003; mode=view; auto_page=1; " +
               $"blacklisted_tags=full-package_futanari&futanari; locale=en; _sankakucomplex_session={user.SankakuComplexSessionID}");

            var response = (HttpWebResponse)request.GetResponse();
            var content = Encoding.UTF8.GetString(GZipDecompress(response.GetResponseStream()));
            response.Close();

            Regex rgx = new Regex(@"<li>Original: <a href=""(.*?)"" id=highres.*?<\/a><\/li>", RegexOptions.Singleline);
            return "http:" + rgx.Match(content).Groups[1].Value;            
        }
        public static byte[] DownloadImage(SankakuChannelUser user, string imageLink, out bool wasRedirected, bool containsVideo, double sizeLimit)
        {
            wasRedirected = false;

            var request = (HttpWebRequest)WebRequest.Create(imageLink);

            request.Host = "cs.sankakucomplex.com";
            request.Headers.Add("Upgrade-Insecure-Requests", "1");
            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/53.0.2785.143 Safari/537.36";
            request.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8";
            request.Headers.Add("Accept-Encoding", "gzip, deflate, sdch");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.8,sl;q=0.6");
            request.Headers.Add("Cookie", $"__cfduid={user.cfduid}");

            request.Timeout = 1000 * 20;
            var response = (HttpWebResponse)request.GetResponse();

            // check if redirected
            if (response.ResponseUri.OriginalString.ToLower().Contains("redirect.png"))
            {             
                wasRedirected = true;
            }

            if (containsVideo == false && (response.ContentType.ToLower().Contains("gif") ||
                response.ContentType.ToLower().Contains("webm") ||
                response.ContentType.ToLower().Contains("mp4") ||
                response.ContentType.ToLower().Contains("mpeg")))
            {
                return null;
            }
            if (sizeLimit > 0 &&
                (((double)response.ContentLength / 1024.0) / 1024.0) > sizeLimit)
            {
                return null;  // image is too big
            }
            

            var rStream = response.GetResponseStream();
            List<byte> readBytes = new List<byte>();
            while (true)
            {
                int rbyte = rStream.ReadByte();
                if (rbyte == -1) break;
                else readBytes.Add((byte)rbyte);
            }

            //rStream.Read(buffer, 0, buffer.Length);  -- THIS DOES NOT READ ALL THE BYTES IN STREAM
            response.Close();

            return readBytes.ToArray();
        }

        public static byte[] GZipCompress(byte[] arrayToCompress)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (GZipStream comprStream =
                    new GZipStream(stream, CompressionMode.Compress))
                {
                    new MemoryStream(arrayToCompress).CopyTo(comprStream);
                }

                return stream.ToArray();
            }
        }
        public static byte[] GZipDecompress(Stream streamToDecompress)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (GZipStream comprStream =
                    new GZipStream(streamToDecompress, CompressionMode.Decompress))
                {
                    comprStream.CopyTo(stream);
                }

                return stream.ToArray();
            }
        }
    }
}
