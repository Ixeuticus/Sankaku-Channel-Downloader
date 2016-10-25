namespace SankakuChannelDownloader
{
    public class DownloadStats
    {
        public int PostsFound { get; set; } = 0;
        public int PostsDownloaded { get; set; } = 0;
        public bool WasCancelled { get; set; } = false;
    }
}