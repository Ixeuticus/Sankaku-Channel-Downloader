using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using SankakuAPI;

namespace SankakuDownloader
{
    public class MainViewModel : INotifyPropertyChanged
    {
        #region Private Fields
        string username, query, blacklist, location, loginstatus = "User is not logged in!";
        int spage = 1, limit = 50, maxdcount = 0, maxfs = 0, minsc = 0, minfc = 0;
        bool? skipvid, cd = false, skipef = true;
        SynchronizationContext UIContext;
        CancellationTokenSource csrc;
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
        public bool? ConcurrentDownloads { get => cd; set { cd = value; Changed(); } }
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
            LoginStatus = "Logging in...";
            try
            {
                var success = await this.Client.Login(Username, password);

                Username = Client.Username;
                PasswordHash = Client.PasswordHash;
                LoginStatus = $"Logged in as {Username}";

                return success;
            }
            catch
            {
                LoginStatus = "Failed to log in.";
                throw;
            }
        }
        public void LoadPasswordHash(string username, string phash) => Client = new SankakuChannelClient(username, phash);
        public async Task StartDownloading()
        {
            CurrentlyDownloading = true;
            csrc = new CancellationTokenSource();

            try
            {
                await Task.Run(async () =>
                {
                    // start downloading
                    int downloadCount = 0;
                    int currentPage = StartingPage;

                    // variables for retrying when connection fails
                    int waitingTime = 0;
                    int waitingTimeIncrement = 2000;
                    int waitingTimeLimit = 60 * 60 * 1000;

                    while (true)
                    {
                        try
                        {
                            csrc.Token.ThrowIfCancellationRequested();

                            // get pages
                            Log($"Searching on page {currentPage} in chunks of {Limit} posts per page.");
                            var posts = await Client.Search(Query.ToLower(), currentPage, Limit);
                            csrc.Token.ThrowIfCancellationRequested();

                            Log($"Found {posts.Count} posts on page {currentPage}");
                            if (posts.Count == 0) break; // end reached

                            object padlockp = new object();
                            int downloaded = 0;
                            int dprogress = 0;

                            // local function for getting progress text
                            string getProgress(int p) => $"[{((p / (double)posts.Count) * 100.0).ToString("0.00")}%]";

                            // local function for getting first non-aggregate exception
                            Exception ignoreAggregateExceptions(Exception exc)
                            {
                                Exception excc = exc;
                                while (true)
                                {
                                    if (excc.InnerException == null || excc is AggregateException == false) return excc;
                                    if (excc is AggregateException) excc = excc.InnerException;
                                }
                            }

                            // local function for downloading a post
                            async Task downloadPost(SankakuPost p)
                            {
                                int pr = 0;
                                CancellationTokenSource oldcsrc = csrc;

                                csrc.Token.ThrowIfCancellationRequested();
                                var targetDestination = Path.Combine(DownloadLocation, p.FileName);
                                if (Directory.Exists(DownloadLocation) == false) Directory.CreateDirectory(DownloadLocation);

                                #region Check if File is to be downloaded
                                if (MaxDownloadCount != 0 && downloadCount + downloaded + 1 > MaxDownloadCount) throw new LimitReachedException();
                                if (MaxFileSizeMB != 0 && p.FileSizeMB > MaxFileSizeMB)
                                {
                                    lock (padlockp)
                                    {
                                        pr = ++dprogress;
                                        // limit reached
                                        Log($"{getProgress(pr)} Skipped '{p.FileName}' ({p.FileSizeMB.ToString("0.00")} MB) - File size limit exceeded",
                                            false, targetDestination, true);
                                    }
                                    return;
                                }
                                if (File.Exists(targetDestination))
                                {
                                    // file with same filename exists - check size
                                    if (Math.Abs(new FileInfo(targetDestination).Length - p.FileSize) < 400 && SkipExistingFiles == true)
                                    {
                                        lock (padlockp)
                                        {
                                            pr = ++dprogress;
                                            // if size difference is less than 400 bytes - consider images to be the same
                                            Log($"{getProgress(pr)} Skipped '{p.FileName}' ({p.FileSizeMB.ToString("0.00")} MB) - File exists",
                                                false, targetDestination, true);
                                        }
                                        return;
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
                                    bool isBlacklisted = false;

                                    foreach (var b in blacklistedTags) if (p.Tags.Count(x => x.Name.ToLower() == b) > 0)
                                        {
                                            lock (padlockp)
                                            {
                                                pr = ++dprogress;
                                                // post contains blacklisted tag
                                                Log($"{getProgress(pr)} Skipped '{p.FileName}' ({p.FileSizeMB.ToString("0.00")} MB) - Contains blacklisted tag '{b}'",
                                                    false, targetDestination, true);
                                                isBlacklisted = true;
                                            }
                                            break;
                                        }

                                    if (isBlacklisted) return;
                                }
                                if (SkipVideoFiles == true)
                                {
                                    var ext = Path.GetExtension(targetDestination).Replace(".", "").ToLower();
                                    var videoExtension = new string[] { "gif", "webm", "mp4", "avi", "flv", "swf" };
                                    if (videoExtension.Contains(ext))
                                    {
                                        lock (padlockp)
                                        {
                                            pr = ++dprogress;
                                            // is a video
                                            Log($"{getProgress(pr)} Skipped '{p.FileName}' ({p.FileSizeMB.ToString("0.00")} MB) - Is video",
                                            false, targetDestination, true);
                                        }
                                        return;
                                    }
                                }
                                if (p.Score < MinScore || p.FavCount < MinFavCount)
                                {
                                    lock (padlockp)
                                    {
                                        pr = ++dprogress;
                                        // post is below score/favcount limit
                                        Log($"{getProgress(pr)} Skipped '{p.FileName}' ({p.FileSizeMB.ToString("0.00")} MB) - Score or Fav. Count is below limit",
                                            false, targetDestination, true);
                                    }

                                    return;
                                }
                                #endregion

                                // download data
                                var task = Client.DownloadImage(p.FileUrl);
                                task.Wait(csrc.Token);
                                var data = task.Result;

                                csrc.Token.ThrowIfCancellationRequested();
                                if (oldcsrc != csrc) throw new OperationCanceledException("Token has changed!");

                                File.WriteAllBytes(targetDestination, data);

                                lock (padlockp)
                                {
                                    pr = ++dprogress;
                                    downloaded++;
                                    Log($"{getProgress(pr)} Downloaded '{p.FileName}' ({p.FileSizeMB.ToString("0.00")} MB)", false, targetDestination);
                                }
                            }

                            // start downloading
                            Log($"Downloading posts...");

                            Exception e = null;
                            if (ConcurrentDownloads == true)
                            {
                                // concurrent downloading
                                CancellationTokenSource parallelsrc = new CancellationTokenSource();
                                Parallel.ForEach(posts, new ParallelOptions() { MaxDegreeOfParallelism = 5 }, (p, state) =>
                                    {
                                        try
                                        {
                                            // only start downloading if parallelsrc is not cancelled
                                            if (parallelsrc.IsCancellationRequested == false)
                                                downloadPost(p).Wait(parallelsrc.Token);
                                        }
                                        catch (AggregateException aex)
                                        {
                                            lock (padlockp)
                                            {
                                                e = ignoreAggregateExceptions(aex);

                                                // task sometimes gets cancelled without ever requesting cancellation
                                                if (e is TaskCanceledException) e = new HttpRequestException("Lost connection [0].");
                                            }
                                            parallelsrc.Cancel();
                                        }
                                        catch (Exception ex)
                                        {
                                            lock (padlockp)
                                            {
                                                // cancel entire task if csrc is cancelled
                                                if (csrc.IsCancellationRequested) e = ex;
                                                // cancel only this loop if parallelsrc is cancelled
                                                else if (parallelsrc.IsCancellationRequested) e = new HttpRequestException("Lost connection [1].");
                                                // unknown error
                                                else e = ex;
                                            }

                                            if (parallelsrc.IsCancellationRequested == false) parallelsrc.Cancel();
                                        }
                                    });

                                if (e != null) throw e;
                            }
                            else foreach (var p in posts)
                                {
                                    // sequential downloading
                                    try { await downloadPost(p); }
                                    catch(AggregateException aex)
                                    {
                                        e = ignoreAggregateExceptions(aex);

                                        // task sometimes gets cancelled without ever requesting cancellation
                                        if (e is TaskCanceledException && csrc.IsCancellationRequested == false)
                                            throw new HttpRequestException("Lost connection [0].");

                                        throw e;
                                    }
                                    catch (Exception ex)
                                    {
                                        // task sometimes gets cancelled without ever requesting cancellation
                                        if (ex is TaskCanceledException && csrc.IsCancellationRequested == false)
                                            throw new HttpRequestException("Lost connection [0].");

                                        throw;
                                    }
                                }


                            currentPage++;
                            waitingTime = 0;
                            downloadCount += posts.Count;
                        }
                        catch (HttpRequestException ex)
                        {
                            string getTime()
                            {
                                if (waitingTime < 60 * 1000) return $"{Math.Round(waitingTime / 1000.0, 2)} second/s";
                                else if (waitingTime < 60 * 60 * 1000) return $"{Math.Round(waitingTime / (1000.0 * 60), 2)} minute/s";
                                else return $"{Math.Round(waitingTime / (1000.0 * 60 * 60), 2)} hour/s";
                            }

                            // increment waiting time unless limit reached
                            if (waitingTime <= waitingTimeLimit) waitingTime += waitingTimeIncrement;

                            // log
                            Log("Error! " + ex.Message + $" Trying again in {getTime()}", true);
                            Logger.Log(ex, $"HttpError - Trying again in {getTime()} -> ");

                            // wait
                            await Task.Delay(waitingTime, csrc.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (LimitReachedException)
                        {
                            throw;
                        }
                        catch (Exception e)
                        {
                            Logger.Log(e, "StartDownloading() exception -> ");
                            throw;
                        }
                    }

                    CurrentlyDownloading = false;
                    Log("Task finished.");
                }, csrc.Token);
            }
            catch (LimitReachedException)
            {
                // ignore
                CurrentlyDownloading = false;
                Log($"Limit reached. Downloaded {MaxDownloadCount} posts.");
            }
            catch (OperationCanceledException)
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

            csrc?.Cancel();
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
                    Username = Client.Username,
                    ConcurrentDownloads = ConcurrentDownloads == true
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
                this.ConcurrentDownloads = save.ConcurrentDownloads;

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
            var timestamp = $"[{DateTime.Now.ToString("HH:mm:ss")}]";
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
        public bool ConcurrentDownloads { get; set; }
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
    #endregion
}
