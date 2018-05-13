using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace SankakuAPI
{
    public class SankakuAuthResponse
    {
        [JsonProperty("current_user")]
        public SankakuUser CurrentUser { get; set; }
        [JsonProperty("success")]
        public bool Success { get; set; }
        [JsonProperty("password_hash")]
        public string PasswordHash { get; set; }
    }
    public class SankakuUser
    {
        [JsonProperty("name")]
        public string Name { get; set; }   
    }
}
