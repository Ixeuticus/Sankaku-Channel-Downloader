using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using SankakuAPI;

namespace SankakuDownloader
{
    public class MainViewModel : INotifyPropertyChanged
    {
        #region Private Fields
        string username, loginstatus = "User is not logged in!";
        bool? cd = false;

        SynchronizationContext UIContext;
        CancellationTokenSource csrc;
        JobConfiguration cjob;

        Queue<JobConfiguration> queue;
        #endregion

        #region Public Properties    
        public string Username { get => username; set { username = value; Changed(); } }
        public string PasswordHash { get; set; }
        public string LoginStatus { get => loginstatus; set { loginstatus = value; Changed(); } }

        public JobConfiguration CurrentJob { get => cjob; set { cjob = value; Changed(); } }
        public Queue<JobConfiguration> Jobs { get => queue; set { queue = value; Changed(); Changed("JobsRemaining"); } }
        public event EventHandler<bool> JobsCollectionChanged;
        public int JobsRemaining => Jobs.Count;

        public bool? ConcurrentDownloads { get => cd; set { cd = value; Changed(); } }
        public ObservableCollection<LogItem> Logs { get; set; } = new ObservableCollection<LogItem>();
        public bool CurrentlyDownloading { get; private set; } = false;


        public SankakuChannelClient Client { get; private set; } = new SankakuChannelClient();
        #endregion

        public MainViewModel()
        {
            UIContext = SynchronizationContext.Current;
            Jobs = new Queue<JobConfiguration>();
            CurrentJob = new JobConfiguration();
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
            bool isStopped = false;
            CurrentlyDownloading = true;
            csrc = new CancellationTokenSource();

            while (Jobs.Count > 0)
            {
                if (csrc.Token.IsCancellationRequested || isStopped) break;

                CurrentJob = Jobs.Peek();
                CurrentJob.IsActive = true;

                try
                {
                    Log($"Working on new job (Query: '{CurrentJob.Query}')");

                    await Task.Run(async () =>
                    {
                        // start downloading
                        int downloadCount = 0;
                        int currentPage = CurrentJob.StartingPage;

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
                                Log($"Searching on page {currentPage} in chunks of {CurrentJob.Limit} posts per page.");
                                var posts = await Client.Search(CurrentJob.Query.ToLower(), currentPage, CurrentJob.Limit).ConfigureAwait(false);
                                csrc.Token.ThrowIfCancellationRequested();

                                Log($"Found {posts.Count} posts on page {currentPage}");
                                if (posts.Count == 0) break; // end reached

                                object padlockp = new object();
                                int downloaded = 0;
                                int dprogress = 0;

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
                                async Task downloadPost(SankakuPost p, CancellationTokenSource taskTokenSource)
                                {
                                    int pr = 0;
                                    CancellationTokenSource oldcsrc = csrc;
                                    taskTokenSource.Token.ThrowIfCancellationRequested();

                                    // determine destination
                                    var fname = CurrentJob.GetFilename(p);
                                    var targetDestination = Path.Combine(CurrentJob.DownloadLocation, fname);
                                    if (Directory.Exists(CurrentJob.DownloadLocation) == false)
                                        Directory.CreateDirectory(CurrentJob.DownloadLocation);

                                    #region Check if File is to be downloaded
                                    if (CurrentJob.MaxDownloadCount != 0 && downloadCount + downloaded + 1 > CurrentJob.MaxDownloadCount) throw new LimitReachedException();
                                    if (CurrentJob.MaxFileSizeMB != 0 && p.FileSizeMB > CurrentJob.MaxFileSizeMB)
                                    {
                                        lock (padlockp)
                                        {
                                            pr = ++dprogress;
                                            // limit reached
                                            Log($"Skipped '{fname}' ({p.FileSizeMB.ToString("0.00")} MB) - File size limit exceeded",
                                                    false, targetDestination, true);
                                        }
                                        return;
                                    }
                                    if (File.Exists(targetDestination))
                                    {
                                        // file with same filename exists - check size
                                        if (Math.Abs(new FileInfo(targetDestination).Length - p.FileSize) < 400 && CurrentJob.SkipExistingFiles == true)
                                        {
                                            lock (padlockp)
                                            {
                                                pr = ++dprogress;
                                                // if size difference is less than 400 bytes - consider images to be the same
                                                Log($"Skipped '{fname}' ({p.FileSizeMB.ToString("0.00")} MB) - File exists",
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
                                                targetDestination = Path.Combine(CurrentJob.DownloadLocation, fname.Insert(0, $"({count})"));
                                                count++;
                                            } while (File.Exists(targetDestination));
                                        }
                                    }
                                    if (CurrentJob.Blacklist != null && CurrentJob.Blacklist?.Length > 0)
                                    {
                                        // check blacklisted tags
                                        string[] blacklistedTags = CurrentJob.Blacklist.Split(' ').Select(x => x.ToLower()).ToArray();
                                        bool isBlacklisted = false;

                                        foreach (var b in blacklistedTags) if (p.Tags.Count(x => x.Name.ToLower() == b) > 0)
                                            {
                                                lock (padlockp)
                                                {
                                                    pr = ++dprogress;
                                                    // post contains blacklisted tag
                                                    Log($"Skipped '{fname}' ({p.FileSizeMB.ToString("0.00")} MB) - Contains blacklisted tag '{b}'",
                                                            false, targetDestination, true);
                                                    isBlacklisted = true;
                                                }
                                                break;
                                            }

                                        if (isBlacklisted) return;
                                    }
                                    if (CurrentJob.SkipVideoFiles == true)
                                    {
                                        var ext = Path.GetExtension(targetDestination).Replace(".", "").ToLower();
                                        var videoExtension = new string[] { "gif", "webm", "mp4", "avi", "flv", "swf" };
                                        if (videoExtension.Contains(ext))
                                        {
                                            lock (padlockp)
                                            {
                                                pr = ++dprogress;
                                                // is a video
                                                Log($"Skipped '{fname}' ({p.FileSizeMB.ToString("0.00")} MB) - Is video",
                                                    false, targetDestination, true);
                                            }
                                            return;
                                        }
                                    }
                                    if (p.Score < CurrentJob.MinScore || p.FavCount < CurrentJob.MinFavCount)
                                    {
                                        lock (padlockp)
                                        {
                                            pr = ++dprogress;
                                            // post is below score/favcount limit
                                            Log($"Skipped '{fname}' ({p.FileSizeMB.ToString("0.00")} MB) - Score or Fav. Count is below limit",
                                                    false, targetDestination, true);
                                        }

                                        return;
                                    }
                                    #endregion

                                    #region Download File
                                    // download data
                                    bool useSample = CurrentJob.ResizedOnly == true && !string.IsNullOrEmpty(p.SampleUrl);
                                    var url = useSample ? p.SampleUrl : p.FileUrl;

                                    // add status log that will show progress
                                    LogItem log = new LogItem()
                                    {
                                        TimeStamp = $"[{DateTime.Now.ToString("HH:mm:ss")}]",
                                        FullPath = targetDestination,
                                        FileName = Path.GetFileName(targetDestination),
                                        IsError = false,
                                        Message = $"Downloading '{Path.GetFileName(targetDestination)}' " +
                                                  $"({p.ActualFileSizeMB.ToString("0.00")} MB) [0.00%]"
                                    };
                                    Action<long> prg = l =>
                                    {
                                        var progress = ((double)l / p.FileSize) * 100;
                                        log.Message = $"Downloading '{Path.GetFileName(targetDestination)}' " +
                                                      $"({p.ActualFileSizeMB.ToString("0.00")} MB) [{progress.ToString("0.00")}%]";

                                    };
                                    Log(log);

                                    try
                                    {
                                        // DOWNLOAD IMAGE
                                        await Client.DownloadImage(url, targetDestination, prg, taskTokenSource.Token).ConfigureAwait(false);
                                    }
                                    catch
                                    {
                                        // remove log
                                        UIContext.Send(d => Logs.Remove(log), null);
                                        throw;
                                    }

                                    if (oldcsrc != csrc) throw new OperationCanceledException("Token has changed!");
                                    #endregion

                                    // log progress
                                    lock (padlockp)
                                    {
                                        pr = ++dprogress;
                                        downloaded++;
                                        p.ActualFileSize = new FileInfo(targetDestination).Length;
                                        log.Message = $"Downloaded{(useSample ? " resized" : "")} '{fname}' ({p.ActualFileSizeMB.ToString("0.00")} MB)";
                                        /*
                                        Log($"{getProgress(pr)} Downloaded{(useSample ? " resized" : "")} '{fname}' ({p.ActualFileSizeMB.ToString("0.00")} MB)", 
                                            false, targetDestination);*/
                                    }
                                }

                                // start downloading
                                Log($"Downloading posts...");

                                Exception e = null;
                                if (ConcurrentDownloads == true)
                                {
                                    // concurrent downloading
                                    CancellationTokenSource parallelsrc = CancellationTokenSource.CreateLinkedTokenSource(csrc.Token);
                                    Parallel.ForEach(posts, new ParallelOptions() { MaxDegreeOfParallelism = 5 }, (p, state) =>
                                    {
                                        try
                                        {
                                            // only start downloading if parallelsrc is not cancelled
                                            if (parallelsrc.IsCancellationRequested == false)
                                                downloadPost(p, parallelsrc).Wait(parallelsrc.Token);
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
                                        try { await downloadPost(p, csrc); }
                                        catch (AggregateException aex)
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

                        Log("Job finished.");
                    }, csrc.Token);
                }
                catch (LimitReachedException)
                {
                    // ignore
                    CurrentlyDownloading = false;
                    Log($"Limit reached. Downloaded {CurrentJob.MaxDownloadCount} posts.");
                }
                catch (OperationCanceledException)
                {
                    // ignore it
                    isStopped = true;
                    CurrentlyDownloading = false;
                    Log("Task stopped.");
                }
                catch (Exception ex)
                {
                    CurrentlyDownloading = false;
                    Log("Error! " + ex.Message, true);
                    throw;
                }

                CurrentJob.IsActive = false;

                // if not cancelled, remove job from queue and continue to next one
                if (!isStopped)
                {
                    Jobs.Dequeue();
                    Changed("JobsRemaining");
                    JobsCollectionChanged?.Invoke(this, true);
                }
            }

            CurrentlyDownloading = false;
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
                var save = CurrentJob.GetSaveData();
                save.Username = Client.Username;
                save.PasswordHash = Client.PasswordHash;
                save.ConcurrentDownloads = ConcurrentDownloads == true;

                serializer.Serialize(writer, save);
            }
        }
        public void LoadData(string path)
        {
            if (File.Exists(path) == false) return;

            var serializer = new XmlSerializer(typeof(SaveData));
            using (var r = new StreamReader(path))
            {
                var save = (SaveData)serializer.Deserialize(r);

                if (CurrentJob == null) CurrentJob = new JobConfiguration();

                Username = save.Username;
                PasswordHash = save.PasswordHash;
                CurrentJob.Query = save.Query;
                CurrentJob.Limit = save.Limit;
                ConcurrentDownloads = save.ConcurrentDownloads;
                CurrentJob.MinScore = save.MinScore;
                CurrentJob.Blacklist = save.Blacklist;
                CurrentJob.ResizedOnly = save.ResizedOnly;
                CurrentJob.MinFavCount = save.MinFavCount;
                CurrentJob.NamingFormat = save.NamingFormat;
                CurrentJob.StartingPage = save.StartingPage;
                CurrentJob.MaxFileSizeMB = save.MaxFileSizeMB;
                CurrentJob.SkipVideoFiles = save.SkipVideoFiles;
                CurrentJob.DownloadLocation = save.DownloadLocation;
                CurrentJob.MaxDownloadCount = save.MaxDownloadCount;
                CurrentJob.SkipExistingFiles = save.SkipExistingFiles;
                CurrentJob.SkipPreviousFiles = save.SkipPreviousFiles;

                if (string.IsNullOrEmpty(save.PasswordHash) == false && string.IsNullOrEmpty(Username) == false)
                {
                    LoadPasswordHash(Username, PasswordHash);
                    LoginStatus = $"Logged in as {Username} (Loaded from settings)";
                }
                else LoginStatus = "User is not logged in!";
            }
        }
        public void EnqueueCurrentJob()
        {
            Jobs.Enqueue(CurrentJob);
            Changed("JobsRemaining");
            JobsCollectionChanged?.Invoke(this, true);

            Log("Job enqueued.");

            CurrentJob = new JobConfiguration(CurrentJob);
        }

        private void Log(LogItem item) => UIContext.Post(a => Logs.Add(item), null);

        private void Log(string message, bool iserror = false, string filepath = null, bool minor = false)
        {
            var timestamp = $"[{DateTime.Now.ToString("HH:mm:ss")}]";
            var filename = filepath == null ? null : Path.GetFileName(filepath);

            Log(new LogItem()
            {
                TimeStamp = timestamp,
                Message = message,
                IsError = iserror,
                FileName = filename ?? "",
                FullPath = filepath ?? "",
                IsMinor = minor
            });
        }

        public bool IsPathSet() => CurrentJob.IsPathSet();


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
        public bool ResizedOnly { get; set; }
        public bool ConcurrentDownloads { get; set; }
        public string DownloadLocation { get; set; }
        public int MinScore { get; set; }
        public int MinFavCount { get; set; }
        public string NamingFormat { get; set; }
        public bool SkipPreviousFiles { get; set; }
    }

    public class LogItem : INotifyPropertyChanged
    {
        string msg;

        public bool IsError { get; set; }
        public bool IsMinor { get; set; }
        public string Message { get => msg; set { msg = value; Changed(); } }
        public string TimeStamp { get; set; }
        public string FileSize { get; set; }
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public bool IsFile => !string.IsNullOrEmpty(FullPath) && File.Exists(FullPath);



        public event PropertyChangedEventHandler PropertyChanged;
        public void Changed([CallerMemberName]string name = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class JobConfiguration : INotifyPropertyChanged
    {
        string query, blacklist, location, namingFormat = null;
        int spage = 1, limit = 50, maxdcount = 0, maxfs = 0, minsc = 0, minfc = 0;
        bool? skipvid, skipef = true, resizeonly = false, skiprev = false;
        bool isactive = false;

        public bool IsActive { get => isactive; set { isactive = value; Changed(); Changed("ActiveText"); } }
        public string ActiveText => IsActive ? "[DOWNLOADING]" : "[WAITING]";

        public int StartingPage { get => spage; set { spage = value; Changed(); } }
        public int Limit { get => limit; set { limit = value; Changed(); } }
        public string Query { get => query; set { query = value; Changed(); } }
        public string Blacklist { get => blacklist; set { blacklist = value; Changed(); } }
        public int MaxDownloadCount { get => maxdcount; set { maxdcount = value; Changed(); } }
        public int MaxFileSizeMB { get => maxfs; set { maxfs = value; Changed(); } }
        public bool? SkipExistingFiles { get => skipef != null ? skipef : false; set { skipef = value; Changed(); } }
        public bool? SkipVideoFiles { get => skipvid != null ? skipvid : false; set { skipvid = value; Changed(); } }
        public bool? SkipPreviousFiles { get => skiprev != null ? skiprev : false; set { skiprev = value; Changed(); } }
        public bool? ResizedOnly { get => resizeonly != null ? resizeonly : false; set { resizeonly = value; Changed(); } }
        public string DownloadLocation { get => location ?? "Click here to set it!"; set { location = value; Changed(); } }
        public int MinScore { get => minsc; set { minsc = value; Changed(); } }
        public int MinFavCount { get => minfc; set { minfc = value; Changed(); } }
        public string NamingFormat
        {
            get => string.IsNullOrEmpty(namingFormat) ? "[md5].[extension]" : namingFormat;
            set { namingFormat = value; Changed(); Changed("NamingExample"); }
        }
        public string NamingExample => GetFilename(new SankakuPost()
        {
            Score = 233,
            FavCount = 13444,
            FileUrl = "//cs.sankakucomplex.com/data/c1/f1/c1f1bf167b1c24dad6475bfdf98fa3fd.png",
            Tags = new List<SankakuTag>()
            {
                new SankakuTag()
                {
                    Name = "artist-name",
                    Type = 1
                },
                new SankakuTag()
                {
                    Name = "genre",
                    Type = 5
                },
                new SankakuTag()
                {
                    Name = "general-tag1",
                    Type = 0
                },
                new SankakuTag()
                {
                    Name = "general-tag2",
                    Type = 0
                },
                new SankakuTag()
                {
                    Name = "character-tag",
                    Type = 4
                },
                new SankakuTag()
                {
                    Name = "copyright-tag",
                    Type = 3
                }
            }
        });

        public bool IsPathSet() => location != null && location?.Length > 0;

        public SaveData GetSaveData()
        {
            return new SaveData()
            {
                Blacklist = blacklist ?? "",
                Query = query ?? "",
                DownloadLocation = location,
                MaxDownloadCount = maxdcount,
                MaxFileSizeMB = maxfs,
                Limit = limit,
                MinFavCount = minfc,
                MinScore = minsc,
                SkipExistingFiles = skipef == true,
                SkipVideoFiles = skipvid == true,
                ResizedOnly = resizeonly == true,
                StartingPage = spage,
                NamingFormat = namingFormat,
                SkipPreviousFiles = skiprev == true
            };
        }
        public string GetFilename(SankakuPost p)
        {
            const int maxFilenameLength = 255;

            // get length of filename WITHOUT replaced values
            var emptyFormat = NamingFormat
                .Replace("[md5]", "")
                .Replace("[score]", "")
                .Replace("[favcount]", "")
                .Replace("[extension]", "")
                .Replace("[genre-tags]", "")
                .Replace("[artist-tags]", "")
                .Replace("[general-tags]", "")
                .Replace("[copyright-tags]", "")
                .Replace("[character-tags]", "");
            int len = emptyFormat.Length;

            // get basic information
            var f = p.FileName.Split('.');
            var md5 = f[0]; // 32 chars
            var ext = f[1]; // 3 chars
            var score = p.Score.ToString();         // 0-7 chars
            var favcount = p.FavCount.ToString();   // 0-7 chars

            // get all tags 
            var genreTags = tagListToString(p.Tags.Where(t => t.Type == 5).ToList());
            var artistTags = tagListToString(p.Tags.Where(t => t.Type == 1).ToList());
            var generalTags = tagListToString(p.Tags.Where(t => t.Type == 0).ToList());
            var copyrightTags = tagListToString(p.Tags.Where(t => t.Type == 3).ToList());
            var characterTags = tagListToString(p.Tags.Where(t => t.Type == 4).ToList());

            string tagListToString(List<SankakuTag> tags)
            {
                var str = "";
                foreach (var t in tags)
                {
                    if (str.Length == 0) str += t.Name;
                    else str += "-" + t.Name;
                }

                return str;
            }

            // shorten tags that are too long
            genreTags = trimString(genreTags, 60);
            artistTags = trimString(artistTags, 60);
            generalTags = trimString(generalTags, 60);
            copyrightTags = trimString(copyrightTags, 60);
            characterTags = trimString(characterTags, 60);
            string trimString(string t, int maxlen)
            {
                if (t.Length <= maxlen) return t;
                else return t.Substring(0, maxlen);
            }

            // construct filename and keep it's length within [maxFilenameLength]
            Dictionary<string, string> values = new Dictionary<string, string>
            {
                { "md5", md5 },
                { "extension", ext },
                { "score", score },
                { "favcount", favcount },
                { "genre-tags", genreTags },
                { "artist-tags", artistTags },
                { "copyright-tags", copyrightTags },
                { "character-tags", characterTags },
                { "general-tags", generalTags }
            };

            string fname = NamingFormat;
            foreach (var v in values)
            {
                var k = $"[{v.Key}]";
                if (NamingFormat.Contains(k))
                {
                    // only add if there is length for it
                    if (len + v.Value.Length <= maxFilenameLength)
                    {
                        fname = fname.Replace(k, v.Value);
                        len += v.Value.Length;
                    }
                    else
                    {
                        fname = fname.Replace(k, "");
                    }
                }
            }

            // check for illegal characters
            string regexSearch = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            Regex r = new Regex(string.Format("[{0}]", Regex.Escape(regexSearch)));
            fname = r.Replace(fname, "");

            return fname;
        }

        public JobConfiguration() { }
        public JobConfiguration(JobConfiguration j)
        {
            query = j.query;
            blacklist = j.blacklist;
            location = j.location;
            namingFormat = j.namingFormat;
            spage = j.spage;
            limit = j.limit;
            maxdcount = j.maxdcount;
            maxfs = j.maxfs;
            minsc = j.minsc;
            minfc = j.minfc;
            skipvid = j.skipvid;
            skipef = j.skipef;
            resizeonly = j.resizeonly;
            skiprev = j.skiprev;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void Changed([CallerMemberName]string name = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    #region Exceptions
    public class LimitReachedException : Exception { }
    public class SubtaskCanceledException : Exception { }
    #endregion
}
