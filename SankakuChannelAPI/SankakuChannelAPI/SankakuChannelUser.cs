using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SankakuChannelAPI
{
    [Serializable]
    public class SankakuChannelUser
    {
        public string Username { get; private set; }
        public string Password { get; }
        public string PasswordHash { get; private set; }

        /// <summary>
        /// CloudFlare cookie ID
        /// </summary>
        public string cfduid { get; private set; }

        /// <summary>
        /// Session ID
        /// </summary>
        public string SankakuComplexSessionID { get; private set; }

        /// <summary>
        /// Returns true if user is logged in.
        /// </summary>
        public bool IsAuthenicated { get; private set; }

        /// <summary>
        /// Create a SankakuChannel user. Use this to interact with Sankaku Channel.
        /// </summary>
        /// <param name="username">Registered username</param>
        /// <param name="password">Registered password</param>
        public SankakuChannelUser(string username, string password)
        {
            Username = username;
            Password = password;
            IsAuthenicated = false;
        }

        /// <summary>
        /// Attempt to login with specified username and password.
        /// </summary>
        /// <param name="tooManyRequests">Is true if response was denied because of too many requests made</param>
        /// <returns>Returns true if login successful</returns>
        public bool Authenticate(out bool tooManyRequests)
        {   
            if (IsAuthenicated) throw new InvalidOperationException("User is already authenticated!");

            tooManyRequests = false;
            if (SankakuHttpHandler.LoginUser(Username, Password, out string cookieID, out string sankakuID, out string passHash, out tooManyRequests, out string actualUsername) == false || 
                passHash == null || sankakuID == null || sankakuID.Length < 2 || actualUsername == null)
            {
                return false;
            };

            this.Username = actualUsername;
            this.SankakuComplexSessionID = sankakuID;
            this.cfduid = cookieID;
            this.PasswordHash = passHash;

            IsAuthenicated = true;
            return true;
        }
        /// <summary>
        /// Attempt to login with specified username and password.
        /// </summary>
        /// <returns>Returns true if login successful</returns>
        public bool Authenticate() => Authenticate(out bool tooRequests);
        public bool LogOut() => IsAuthenicated = false;

        /// <summary>
        /// Search for posts with specified tags
        /// </summary>
        /// <param name="query">Specified tags. Each tag seperated by ' '</param>
        /// <param name="page">Page to download</param>
        /// <param name="limit">Limit the number of posts per page</param>
        /// <returns></returns>
        public List<SankakuPost> Search(string query, int page = 1, int limit = 15)
        {
            if (IsAuthenicated == false) throw new InvalidOperationException("You need to be authenticated to use the Search functionality!");
            SankakuHttpHandler.SendQuery(this, query, out List<SankakuPost> results, page, limit);
            return results;
        }
        public bool Favorite(int postID, out bool wasUnfavorited)
        {
            if (IsAuthenicated == false) throw new InvalidOperationException("You need to be authenticated to use the Search functionality!");

            return SankakuHttpHandler.FavoritePost(this, postID, out wasUnfavorited);            
        }
    }
}
