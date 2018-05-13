using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Xml;
using System.Xml.Serialization;
using SankakuAPI;

namespace SankakuDownloader
{
    public class MainViewModel : INotifyPropertyChanged
    {
        #region Private Fields
        string username, query, blacklist, location, loginstatus = "User is not logged in!";
        int spage = 1, limit = 50, maxdcount = 0, maxfs = 0, minsc = 0, minfc = 0;
        bool? skipvid, skipef = true;
        SynchronizationContext UIContext;
        #endregion

        #region Public Properties
        public string Username { get => username; set { username = value; Changed(); } }
        public string PasswordHash { get; set; }
        public string LoginStatus { get => loginstatus; set { loginstatus = value; Changed(); } }
        public int StartingPage { get => spage; set { spage = value; Changed(); } }
        public int Limit { get => limit; set { limit = value; Changed(); } }
        public string Query { get => query; set { query = value; Changed(); } }
        public string Blacklist { get => blacklist; set { blacklist = value; Changed(); } }
        public int MaxDownloadCount { get => maxdcount; set { maxdcount = value; Changed(); } }
        public int MaxFileSizeMB { get => maxfs; set { maxfs = value; Changed(); } }
        public bool? SkipExistingFiles { get => skipef != null ? skipef : false; set { skipef = value; Changed(); } }
        public bool? SkipVideoFiles { get => skipvid != null ? skipvid : false; set { skipvid = value; Changed(); } }
        public string DownloadLocation { get => location ?? "Click here to set it!"; set { location = value; Changed(); } }
        public int MinScore { get => minsc; set { minsc = value; Changed(); } }
        public int MinFavCount { get => minfc; set { minfc = value; Changed(); } }
        public ObservableCollection<LogItem> Logs { get; set; } = new ObservableCollection<LogItem>();
        public bool CurrentlyDownloading { get; private set; } = false;

        public SankakuChannelClient Client { get; private set; } = new SankakuChannelClient();
        #endregion

        public MainViewModel()
        {          
            UIContext = SynchronizationContext.Current;
        }
        public async Task<bool> Login(string password)
        {
            var success = await this.Client.Login(Username, password);
            if (success)
            {
                Username = Client.Username;
                PasswordHash = Client.PasswordHash;
                LoginStatus = $"Logged in as {Username}";                
            }
            return success;
        }
        public void LoadPasswordHash(string username, string phash) => Client = new SankakuChannelClient(username, phash);
        public async Task StartDownloading()
        {
            CurrentlyDownloading = true;

            try
            {
                await Task.Run(async () =>
                {
                    // start downloading
                    int downloadCount = 0;
                    int currentPage = StartingPage;

                    int waitingTime = 2000;
                    int waitingTimeIncrement = 2000;

                    while (true)
                    {
                        try
                        {
                            if (CurrentlyDownloading == false) throw new CancelledException();

                            // get pages
                            Log($"Searching on page {currentPage} in chunks of {Limit} posts per page.");
                            var posts = await Client.Search(Query.ToLower(), currentPage, Limit);
                            if (CurrentlyDownloading == false) throw new CancelledException();

                            Log($"Found {posts.Count} posts on page {currentPage}");

                            if (posts.Count == 0)
                            {
                                // end reached
                                break;
                            }

                            int downloaded = 0;
                            int dprogress = 0;
                            string getProgress() => $"[{((dprogress / (double)posts.Count) * 100.0).ToString("0.00")}%]";

                            Log($"Downloading posts...");
                            foreach (var p in posts)
                            {
                                dprogress++;

                                if (CurrentlyDownloading == false) throw new CancelledException();
                                var targetDestination = Path.Combine(DownloadLocation, p.FileName);

                                if (MaxDownloadCount != 0 && downloadCount + downloaded + 1 > MaxDownloadCount)
                                {
                                    // limit reached
                                    Log($"Limit reached. Downloaded {MaxDownloadCount} posts.");
                                    throw new LimitReachedException();
                                }
                                if (MaxFileSizeMB != 0 && p.FileSizeMB > MaxFileSizeMB)
                                {
                                    // limit reached
                                    Log($"{getProgress()} Skipped '{p.FileName}' ({p.FileSizeMB.ToString("0.00")} MB) - File size limit exceeded", 
                                        false, targetDestination, true);
                                    continue;
                                }
                                if (File.Exists(targetDestination))
                                {
                                    // file with same filename exists - check size
                                    if (Math.Abs(new FileInfo(targetDestination).Length - p.FileSize) < 400 && SkipExistingFiles == true)
                                    {
                                        // if size difference is less than 400 bytes - consider images to be the same
                                        Log($"{getProgress()} Skipped '{p.FileName}' ({p.FileSizeMB.ToString("0.00")} MB) - File exists", 
                                            false, targetDestination, true);
                                        continue;
                                    }
                                    else
                                    {
                                        // if files have same names but are NOT the same, get new filename that does not exist
                                        int count = 0;
                                        do
                                        {
                                            if (count > 1000) break;

                                            targetDestination = Path.Combine(DownloadLocation, p.FileName.Insert(0, $"({count})"));
                                            count++;
                                        } while (File.Exists(targetDestination));
                                    }
                                }
                                if (blacklist != null && blacklist?.Length > 0)
                                {
                                    // check blacklisted tags
                                    string[] blacklistedTags = blacklist.Split(' ').Select(x => x.ToLower()).ToArray();
                                    foreach (var b in blacklistedTags) if (p.Tags.Count(x => x.Name.ToLower() == b) > 0)
                                        {
                                            // post contains blacklisted tag
                                            Log($"{getProgress()} Skipped '{p.FileName}' ({p.FileSizeMB.ToString("0.00")} MB) - Contains blacklisted tag '{b}'", 
                                                false, targetDestination, true);
                                            continue;
                                        }
                                }
                                if (SkipVideoFiles == true)
                                {
                                    var ext = Path.GetExtension(targetDestination).Replace(".", "").ToLower();
                                    var videoExtension = new string[] { "gif", "webm", "mp4", "avi", "flv", "swf" };
                                    if (videoExtension.Contains(ext))
                                    {
                                        // is a video
                                        Log($"{getProgress()} Skipped '{p.FileName}' ({p.FileSizeMB.ToString("0.00")} MB) - Is video", 
                                            false, targetDestination, true);
                                        continue;
                                    }
                                }
                                if (p.Score < MinScore || p.FavCount < MinFavCount)
                                {
                                    // post is below score/favcount limit
                                    Log($"{getProgress()} Skipped '{p.FileName}' ({p.FileSizeMB.ToString("0.00")} MB) - Score or Fav. Count is below limit", 
                                        false, targetDestination, true);
                                    continue;
                                }

                                var data = await Client.DownloadImage(p.FileUrl);
                                if (CurrentlyDownloading == false) throw new CancelledException();

                                File.WriteAllBytes(targetDestination, data);

                                Log($"{getProgress()} Downloaded '{p.FileName}' ({p.FileSizeMB.ToString("0.00")} MB)", false, targetDestination);
                                downloaded++;
                            }

                            currentPage++;
                            waitingTime = 0;
                            downloadCount += posts.Count;
                        }
                        catch (HttpRequestException ex)
                        {
                            string getTime() {
                                if (waitingTime < 60 * 1000) return $"{Math.Round(waitingTime/1000.0, 2)} second/s";
                                else if (waitingTime < 60 * 60 * 1000) return $"{Math.Round(waitingTime / (1000.0 * 60), 2)} minute/s";
                                else return $"{Math.Round(waitingTime / (1000.0 * 60 * 60), 2)} hour/s";
                            }

                            // try again
                            Log("Error! " + ex.Message + $" Trying again in {getTime()}", true);

                            if (waitingTime <= 60 * 60 * 1000) waitingTime += waitingTimeIncrement;
                            await Task.Delay(waitingTime);
                        }
                        catch 
                        {
                            throw;
                        }
                    }

                    Log("Task finished.");
                });
            }
            catch (LimitReachedException)
            {
                // ignore
                CurrentlyDownloading = false;
            }
            catch (CancelledException)
            {
                // ignore it
                CurrentlyDownloading = false;
                Log("Task stopped.");
            }
            catch (Exception ex)
            {
                CurrentlyDownloading = false;
                Log("Error! " + ex.Message, true);
                throw;
            }
        }
        public void StopDownloading()
        {
            Log("Stopping task...");
            CurrentlyDownloading = false;
        }
        public void SaveData(string path)
        {
            var serializer = new XmlSerializer(typeof(SaveData));
            using (var writer = new StreamWriter(path))
            {
                serializer.Serialize(writer, new SaveData()
                {
                    Blacklist = blacklist ?? "",
                    Query = query ?? "",
                    DownloadLocation = location,
                    MaxDownloadCount = maxdcount,
                    MaxFileSizeMB = maxfs,
                    Limit = limit,
                    MinFavCount = minfc,
                    MinScore = minsc,
                    PasswordHash = Client.PasswordHash,
                    SkipExistingFiles = skipef == true,
                    SkipVideoFiles = skipvid == true,
                    StartingPage = spage,
                    Username = Client.Username
                });
            }
        }
        public void LoadData(string path)
        {
            if (File.Exists(path) == false) return;

            var serializer = new XmlSerializer(typeof(SaveData));
            using (var r = new StreamReader(path))
            {
                var save = (SaveData)serializer.Deserialize(r);

                this.Query = save.Query;
                this.Limit = save.Limit;
                this.Username = save.Username;
                this.MinScore = save.MinScore;
                this.Blacklist = save.Blacklist;
                this.MinFavCount = save.MinFavCount;
                this.StartingPage = save.StartingPage;
                this.PasswordHash = save.PasswordHash;
                this.MaxFileSizeMB = save.MaxFileSizeMB;
                this.SkipVideoFiles = save.SkipVideoFiles;
                this.DownloadLocation = save.DownloadLocation;
                this.MaxDownloadCount = save.MaxDownloadCount;
                this.SkipExistingFiles = save.SkipExistingFiles;

                if (string.IsNullOrEmpty(save.PasswordHash) == false && string.IsNullOrEmpty(Username) == false)
                {
                    LoadPasswordHash(Username, PasswordHash);
                    LoginStatus = $"Logged in as {Username} (Loaded from settings)";
                }
                else LoginStatus = "User is not logged in!";               
            }
        }

        private void Log(string message, bool iserror = false, string filepath = null, bool minor = false)
        {
            var timestamp = $"[{DateTime.Now.ToString("hh:mm:ss")}]";
            var filename = filepath == null ? null : Path.GetFileName(filepath);

            UIContext.Post(a => Logs.Add(new LogItem()
            {
                TimeStamp = timestamp,
                Message = message,
                IsError = iserror,
                FileName = filename ?? "",
                FullPath = filepath ?? "",
                IsMinor = minor
            }), null);
        }

        public bool IsPathSet() => location != null && location?.Length > 0;
        public event PropertyChangedEventHandler PropertyChanged;
        public void Changed([CallerMemberName]string name = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    [Serializable]
    public class SaveData
    {
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public int StartingPage { get; set; }
        public int Limit { get; set; }
        public string Query { get; set; }
        public string Blacklist { get; set; }
        public int MaxDownloadCount { get; set; }
        public int MaxFileSizeMB { get; set; }
        public bool SkipExistingFiles { get; set; }
        public bool SkipVideoFiles { get; set; }
        public string DownloadLocation { get; set; }
        public int MinScore { get; set; }
        public int MinFavCount { get; set; }
    }

    public class LogItem
    {
        public bool IsError { get; set; }
        public bool IsMinor { get; set; }
        public string Message { get; set; }
        public string TimeStamp { get; set; }
        public string FileSize { get; set; }
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public bool IsFile => !string.IsNullOrEmpty(FullPath) && File.Exists(FullPath);
    }
    #region Exceptions
    public class LimitReachedException : Exception { }
    public class CancelledException : Exception { }
    #endregion
}
