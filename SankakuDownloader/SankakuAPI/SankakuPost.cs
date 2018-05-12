using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace SankakuAPI
{
    public class SankakuPost
    {
        [JsonProperty("width")]
        public int Width { get; set; }
        [JsonProperty("height")]
        public int Height { get; set; }
        [JsonProperty("sample_url")]
        public string SampleUrl { get; set; }
        [JsonProperty("file_url")]
        public string FileUrl { get; set; }
        [JsonProperty("fav_count")]
        public int FavCount { get; set; }
        [JsonProperty("is_favorited")]
        public bool IsFavorited { get; set; }
        [JsonProperty("source")]
        public string Source { get; set; }
        [JsonProperty("rating")]
        public string Rating { get; set; }
        [JsonProperty("parent_id")]
        public int? ParentId { get; set; }
        [JsonProperty("has_children")]
        public bool HasChildren { get; set; }
        [JsonProperty("file_size")]
        public long FileSize { get; set; }
        [JsonProperty("total_score")]
        public int Score { get; set; }
        [JsonProperty("tags")]
        public List<SankakuTag> Tags { get; set; }
        public string FileName
        {
            get
            {
                if (FileUrl == null) return null;
                else if (FileUrl.Contains("?")) return Path.GetFileName(FileUrl.Substring(0, FileUrl.IndexOf('?')));
                else return Path.GetFileName(FileUrl);
            }
        }
        public double FileSizeMB => (FileSize / 1024.0) / 1024.0;
    }

    public class SankakuTag
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        [JsonProperty("type")]
        public int Type { get; set; }
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("name_ja")]
        public string NameJA { get; set; }
        public override string ToString() => Name;
    }
}
