using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using SankakuChannelAPI;
using System.Net;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading;
using System.Runtime.Serialization.Formatters.Binary;
using System.Windows.Threading;

namespace SankakuChannelDownloader
{
    public partial class MainWindow : Window
    {
        public const string SavePath = "save.data";
        public const string CachePath = "cache.data";
        public static SankakuChannelUser User;
        public static bool CancelRequested = false;

        public List<DateTime> RegisteredTimestamps;
        public List<Log> Logs = new List<Log>();

        public MainWindow()
        {
            InitializeComponent();

            // Opens up the LoginWindow (check "LoginWindow.xaml.cs" for more info)
            LoginWindow form = new LoginWindow();
            form.ShowDialog();

            // If login was not successful - exit the program
            if (form.Success == false)
            {
                Environment.Exit(1);
                return;
            }

            // Subscribe to the event "FinishedWork"
            FinishedWork += MainWindow_FinishedWork;

            // Load saved data if it exists
            LoadData();

            // Display other necessary information...
            txtLoggedIn.Text = "Logged in as ";
            txtLoggedIn.Inlines.Add(new Run(User.Username) { FontWeight = FontWeights.Bold });

            txtTags.Focus(); // <-- focus on textbox "txtTags" so the user can start typing right away :D
        }

        private void LoadData()
        {
            try
            {
                // This just loads the save data if it exists, otherwise it does nothing
                if (File.Exists(SavePath) == false) return;
                Save sv = Save.GetSave(File.ReadAllBytes(SavePath));

                Left = sv.Left;
                Top = sv.Top;
                Height = sv.Height;
                Width = sv.Width;
                txtTags.Text = sv.Tags;
                txtBlacklist.Text = sv.BlacklistedTags;
                txtSizeLimit.Text = sv.SizeLimit;
                txtImageCount.Text = sv.ImageLimit;
                txtPath.Text = sv.FilePath;
                txtPageLimit.Text = (sv.PageLimit.Length == 0 || sv.PageLimit == "0") ? "20" : sv.PageLimit;
                checkBoxSkip.IsChecked = sv.SkipExisting;
                this.WindowState = sv.IsFullscreen ? WindowState.Maximized : WindowState.Normal;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to read the save file!\n\n" + ex.Message, "Save file error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MainWindow_FinishedWork(Object sender, DownloadStats e)
        {
            // This is what happens when that event "FinishedWork" (a little below) is invoked
            Dispatcher.Invoke(() =>
            {
                if (e.WasCancelled == false) WriteToLog("Finished task.");
                else WriteToLog("Task was cancelled.");

                MessageBox.Show(this, $"Download {(e.WasCancelled ? "was cancelled." : "finished.")}\n\n" +
                    $"A total of {e.PostsFound} posts was found and {e.PostsDownloaded} posts were downloaded.",
                    "Download info", MessageBoxButton.OK, MessageBoxImage.Information);

                ToggleControls(true);
                btnStartDownload.Content = "Start Download";

                CancelRequested = false;
                btnStartDownload.IsEnabled = true;
            });
        }

        private void txtPath_MouseDown(Object sender, MouseButtonEventArgs e)
        {
            // If you click on the Path text, a dialog opens to browse folders...
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.ShowDialog();

            if (dialog.SelectedPath.Length > 2)  // if a folder is selected, display it
            {
                txtPath.Text = dialog.SelectedPath;
            }
        }

        private void btnStartDownload_Click(Object sender, RoutedEventArgs e)
        {
            if (btnStartDownload.Content.ToString() == "Stop Download")
            {
                CancelRequested = true;
                btnStartDownload.IsEnabled = false;
                WriteToLog("User requested to abort the task... please wait.");
            }
            else
            {
                // A hell lot of validation going on here...
                if (txtTags.Text.Length < 3)
                {
                    MessageBox.Show("Please enter some actual tags.", "Invalid tags", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }

                // Getting text from TextBoxes -- removing excessive spaces etc...
                string tags = Regex.Replace(txtTags.Text, @"\s+", " ");
                string blacklisted = Regex.Replace(txtBlacklist.Text, @"\s+", " ");
                if (blacklisted == " ") blacklisted = "";
                if (tags == " ") tags = "";
                if (tags.StartsWith(" ")) tags = tags.Substring(1, tags.Length - 1);

                int count;
                double sizeLimit;
                int pageLimit = 20;
                int startingPage = 1;
                bool skipExisting = checkBoxSkip.IsChecked == true;
                bool containVideos = checkboxFilterVideos.IsChecked == false;

                // MORE VALIDATION....
                bool isUserIdiot = false;
                foreach (var tg in tags.Split(' '))
                {
                    foreach (var b in txtBlacklist.Text.Split(' '))
                        if (tg.ToLower() == b.ToLower())
                        {
                            isUserIdiot = true;
                            break;
                        }

                    if (isUserIdiot) break;
                }
                if (isUserIdiot)
                {
                    MessageBox.Show("Are you a fcken idiot?\nDon't use the same search tags for the blacklist!", "User is an idiot", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }
                if (int.TryParse(txtStartingPage.Text, out startingPage) == false || startingPage <= 0)
                {
                    MessageBox.Show("You can't start searching at page 0 or less!", "Invalid input", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }
                if (int.TryParse(txtPageLimit.Text, out pageLimit) == false || pageLimit < 1)
                {
                    MessageBox.Show("Please explain to me how the hell are you going to find any post AT ALL\n if you're searching for less than 0 posts per page.\n\nHa, genius?", "Invalid input", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }
                if (txtBlacklist.Text.Contains(':'))
                {
                    MessageBox.Show("The blacklist contains an invalid tag.", "Invalid tag", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }

                if (int.TryParse(txtImageCount.Text, out count) == false || count < 0)
                {
                    MessageBox.Show("Invalid number of images entered!", "Invalid number", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }
                if (double.TryParse(txtSizeLimit.Text, out sizeLimit) == false || sizeLimit < 0)
                {
                    MessageBox.Show("Invalid size limit entered!", "Invalid number", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }

                if (Directory.Exists(txtPath.Text) == false)
                {
                    MessageBox.Show("Invalid directory specified!", "Invalid path!", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }
                string path = txtPath.Text;

                // Prompt if user is sure to continue...
                /*if (MessageBox.Show("Are you sure you wish to start the download process?\n", "Are you sure?",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No) return;*/

                ToggleControls(false);
                btnStartDownload.Content = "Stop Download";

                // Start the task - give it ALL the information it needs... now that's a lot of parameters... damn.
                Task.Run(() => StartDownloading(tags, count, path, sizeLimit, blacklisted, containVideos, startingPage, pageLimit, skipExisting));
            }
        }

        private void ToggleControls(bool state)
        {
            txtTags.IsEnabled = state;
            txtPath.IsEnabled = state;
            txtImageCount.IsEnabled = state;
            txtSizeLimit.IsEnabled = state;
            txtBlacklist.IsEnabled = state;
            checkboxFilterVideos.IsEnabled = state;
            checkBoxSkip.IsEnabled = state;
            txtPageLimit.IsEnabled = state;
            txtStartingPage.IsEnabled = state;
        }
        public void WriteToLog(
            string msg, bool registerTime = false, string filename = "", 
            string exMessage = "", SankakuPost postInfo = null, string[] fndPosts = null, bool wasSkipped = false)
        {
            // Dispatcher needs to be called when interacting with the GUI - otherwise an error can be thrown
            Dispatcher.Invoke(() =>
            {
                var date = DateTime.Now;
                if (registerTime) RegisteredTimestamps.Add(date);
                Logs.Add(new Log($"[{date.ToString("HH:mm:ss")}] " + msg, date, filename, exMessage, postInfo, fndPosts, wasSkipped));  // <--- adding the log to my log collection

                listBox.ItemsSource = Logs;  // <-- displaying my log collection
                listBox.Items.Refresh();     // <-- refreshing the view
                listBox.ScrollIntoView(listBox.Items[listBox.Items.Count - 1]);  // <-- scrolling to the end, so the viewer sees the latest log
            });
        }

        public void UpdateETA(DownloadStats stats, bool onlyFound = false, bool finished = false)
        {
            Dispatcher.Invoke(() =>
            {
                txtETA.Inlines.Clear();
                txtETA.Inlines.Add(new Run("Remaining (ETA): "));
                if (RegisteredTimestamps.Count < 3) txtETA.Inlines.Add(new Run($"{(finished ? "-" : (onlyFound ? "Not yet downloading." : $"{"Calculating..."}"))}") { FontWeight = FontWeights.Bold });
                else
                {
                    int count = 0;
                    int toScan = (stats.PostsFound > 60) ? 60 : stats.PostsFound;
                    double totalMiliseconds = 0.0;
                    for (int i = RegisteredTimestamps.Count - 1; i >= RegisteredTimestamps.Count - 1 - toScan; i--)
                    {
                        if (i < 1) break;
                        totalMiliseconds += RegisteredTimestamps[i].Subtract(RegisteredTimestamps[i - 1]).TotalMilliseconds;
                        count++;
                    }
                    double averageTime = totalMiliseconds / count;
                    double ETA = averageTime * (stats.PostsFound - stats.PostsDownloaded);
                    TimeSpan span = TimeSpan.FromMilliseconds(ETA);

                    txtETA.Inlines.Add(
                        new Run($"" +
                        $"{(finished ? "-" : $"{((span.TotalMinutes < 1) ? $"{span:ss} seconds" : ((span.TotalHours < 1) ? $"{span:mm} minutes, {span:ss} seconds" : ((span.TotalDays < 1) ? $"{span:hh} hours, {span:mm} minutes, {span:ss} seconds" : $"{span:d} {((span.TotalDays < 2) ? "day" : "days")} {span:hh} hours")))}")}")
                        { FontWeight = FontWeights.Bold });
                }
            });
        }

        public event EventHandler<DownloadStats> FinishedWork;  // This event handler gets invoked when Task is either finished or cancelled
        public static int SecondsWaited = 0; // This is just a temporary variable that gets incremented when task needs to wait for something....

        private void StartDownloading(string tags, int imageLimit, string path, double sizeLimit, string blacklistedTags, bool containVideos, int pageCount,
            int limit, bool skipExisting = false)
        {
            DownloadStats stats = new DownloadStats();
            WriteToLog($"Task started");
            RegisteredTimestamps = new List<DateTime>(); UpdateETA(stats, true);

            List<SankakuPost> foundPosts = new List<SankakuPost>();
            string[] blTags = blacklistedTags.Split(' ');
            WriteToLog($"Searching for posts in chunks of {limit} posts per page...");
            while (true)
            {
                #region Searching posts
                search:
                if (CancelRequested)
                {
                    // Task gets cancelled if cancel is requested
                    stats.WasCancelled = true;
                    UpdateETA(stats, true, true);
                    FinishedWork?.Invoke(null, stats);
                    return;
                }

                try
                {
                    var list = User.Search(tags, pageCount, limit);
                    stats.PostsFound += list.Count;

                    // remove posts with blacklisted tags       
                    int removed = 0;
                    if (blTags.Length > 0)
                        foreach (string s in blTags)
                            removed += list.RemoveAll(x =>
                            {
                                foreach (var t in x.Tags)
                                {
                                    if (t.ToLower() == s) return true;
                                }

                                return false;
                            });

                    foundPosts.AddRange(list);
                    WriteToLog($"Found {list.Count} posts on page {pageCount}.{(removed > 0 ? $" (Removed {removed} posts because of blacklisted tags)" : "")}", fndPosts: list.Select(x => x.PostReference).ToArray());

                    if (foundPosts.Count >= imageLimit && imageLimit > 0) break;
                    if (list.Count < 2) break;
                    pageCount++;
                    SecondsWaited = 0;
                }
                catch (WebException ex)
                {
                    // Error handling... a lot of shit going on in here...
                    #region Error handling
                    if (ex.Message.ToLower().Contains("too many requests"))
                    {
                        if (SecondsWaited == 0) WriteToLog("Too many requests", exMessage: ex.Message);

                        if (SecondsWaited < 60) SecondsWaited += 15;
                        else if (SecondsWaited >= 60 && SecondsWaited < 60 * 15) SecondsWaited += 120;

                        WriteToLog($"Retrying in {SecondsWaited} seconds...");
                        Thread.Sleep(SecondsWaited * 1000);
                        goto search;
                    }
                    else if (ex.Message.ToLower().Contains("remote name could not be resolved"))
                    {
                        WriteToLog("Internet connection lost. Waiting for internet...", exMessage: ex.Message);

                        int secondsToWait = 2;
                        while (true)
                        {
                            if (CancelRequested)
                            {
                                stats.WasCancelled = true;
                                UpdateETA(stats, true, true);
                                FinishedWork?.Invoke(null, stats);
                                return;
                            }

                            Thread.Sleep(secondsToWait * 1000);
                            try
                            {
                                using (var client = new WebClient())
                                {
                                    using (var stream = client.OpenRead("http://www.google.com"))
                                    {
                                        break;
                                    }
                                }
                            }
                            catch
                            {
                                if (secondsToWait < 60 * 10)
                                    secondsToWait += 1;
                            }
                        }

                        WriteToLog("Internet connection restored. Continuing task...");
                        goto search;
                    }
                    else WriteToLog("ERROR: " + ex.Message, exMessage: ex.Message);
                    #endregion
                }
                #endregion
            }

            // remove posts to fit the limit
            if (foundPosts.Count > imageLimit && imageLimit > 0)
            {
                int removed = 0;
                do
                {
                    foundPosts.RemoveAt(foundPosts.Count - 1);
                    removed++;
                } while (foundPosts.Count > imageLimit);

                stats.PostsFound -= removed;
                WriteToLog($"Removed {removed} found posts to fit the given limit.");
            }

            WriteToLog($"Found all posts. ({foundPosts.Count} posts found in total)");
            WriteToLog("Downloading images...");

            var files = (skipExisting) ? Directory.GetFiles(path) : null;
            foreach (var a in foundPosts)
            {
                download:
                try
                {
                    #region Download posts
                    // Check if cancel requested
                    if (CancelRequested)
                    {
                        stats.WasCancelled = true;
                        UpdateETA(stats, true, true);
                        FinishedWork?.Invoke(null, stats);
                        return;
                    }

                    // Check if exists using the cache
                    if (skipExisting)
                    {
                        if (ImageExists(a.PostID, path, out string foundFilename))
                        {
                            double procentage = ((double)(foundPosts.IndexOf(a) + 1) / (double)foundPosts.Count) * 100;

                            WriteToLog($"[{procentage.ToString("0.000") + "%",-10}] Skipped existing file \"{foundFilename}\" (ID: {a.PostID})", false, foundFilename, postInfo: a, wasSkipped: true);
                            stats.PostsDownloaded++;
                            continue;
                        }
                    }

                    // Download actual image
                    var imageLink = a.GetFullImageLink();

                    var imageLinkShortened = imageLink.Substring(imageLink.LastIndexOf('/') + 1);
                    Match match = new Regex(@"(.*?)(\.[a-z,0-5]{0,5})", RegexOptions.Singleline).Match(imageLinkShortened);
                    var filen = match.Groups[1].Value + match.Groups[2].Value;
                    string filename = $"{path}\\{filen}";
                    if (WriteToCache(a.PostID, filen, out string ErrorMsg) == false)
                    {
                        WriteToLog($"Failed to write to cache.", exMessage: ErrorMsg);
                    }

                    var data = a.DownloadFullImage(imageLink, out bool wasRedirected, containVideos, sizeLimit);

                    // Check if response was redirected
                    if (wasRedirected == false)
                    {
                        // Check if post was too big/is a video file
                        if (data == null)
                        {
                            WriteToLog($"The post '{a.PostID}' was skipped because of given conditions.", wasSkipped: true);
                            continue;
                        }

                        File.WriteAllBytes(filename, data);

                        // Display progress
                        double procentage = ((double)(foundPosts.IndexOf(a) + 1) / (double)foundPosts.Count) * 100;
                        WriteToLog($"[{procentage.ToString("0.000") + "%",-10}] Downloaded \"{filename}\" ({Math.Round(((data.Length / 1024.0)/1024.9), 2)}MB) (ID: {a.PostID})", true, filename, postInfo: a);
                        UpdateETA(stats);

                        stats.PostsDownloaded++;
                        SecondsWaited = 0;
                    }
                    else
                    {
                        WriteToLog($"Server response was redirected!");
                    }
                    #endregion
                }
                catch (WebException ex)
                {
                    // Error handling... a lot of shit going on in here as well
                    #region Error handling
                    if (ex.Message.ToLower().Contains("too many requests"))
                    {
                        #region Too Many Requests
                        if (SecondsWaited == 0) WriteToLog("Too many requests", exMessage: ex.Message);

                        if (SecondsWaited < 60) SecondsWaited += 15;
                        else if (SecondsWaited >= 60 && SecondsWaited < 60 * 15) SecondsWaited += 120;

                        WriteToLog($"Retrying in {SecondsWaited} seconds...");
                        Thread.Sleep(SecondsWaited * 1000);
                        goto download;
                        #endregion
                    }
                    else if (ex.Message.ToLower().Contains("remote name could not be resolved"))
                    {
                        #region No Internet
                        WriteToLog("Internet connection lost. Waiting for internet...", exMessage: ex.Message);

                        int secondsToWait = 2;
                        while (true)
                        {
                            if (CancelRequested)
                            {
                                stats.WasCancelled = true;
                                UpdateETA(stats, true, true);
                                FinishedWork?.Invoke(null, stats);
                                return;
                            }

                            Thread.Sleep(secondsToWait * 1000);
                            try
                            {
                                using (var client = new WebClient())
                                {
                                    using (var stream = client.OpenRead("http://www.google.com"))
                                    {
                                        break;
                                    }
                                }
                            }
                            catch
                            {
                                if (secondsToWait < 60 * 10)
                                    secondsToWait += 1;
                            }
                        }

                        WriteToLog("Internet connection restored. Continuing task...");
                        goto download;
                        #endregion
                    }
                    else if (ex.Message.ToLower().Contains("time") && ex.Message.ToLower().Contains("out"))
                    {
                        #region Timeout
                        WriteToLog("ERROR: " + ex.Message, exMessage: ex.Message);
                        WriteToLog("Attempting to restore the connection...");

                        while (true)
                        {
                            if (CancelRequested)
                            {
                                stats.WasCancelled = true;
                                UpdateETA(stats, true, true);
                                FinishedWork?.Invoke(null, stats);
                                return;
                            }

                            bool success = false;
                            try
                            {
                                success = LoginWindow.LoginUser(User, true);
                            }
                            catch { }

                            if (success)
                            {
                                WriteToLog("Successfully restored connection. Continuing task...");
                                goto download;
                            }
                            else
                            {
                                if (SecondsWaited < 600) SecondsWaited += 15;

                                WriteToLog($"Failed to establish connection. Attempting again in {SecondsWaited} seconds...");
                                Thread.Sleep(SecondsWaited * 1000);
                            }
                        }
                        #endregion
                    }
                    else WriteToLog("ERROR: " + ex.Message, exMessage: ex.Message);
                    #endregion
                }
                catch (UriFormatException)
                {
                    // This exception gets thrown when a flash game is encountered on Sankaku and does not have a source link

                    // <param name=movie value="//cs.sankakucomplex.com/data/f6/23/f623ea7559ef39d96ebb0ca7530586b8.swf?3378073">
                    WriteToLog("Skipping invalid post.", wasSkipped: true);
                }
                catch (Exception ex)
                {
                    WriteToLog("ERROR: " + ex.Message + $"({ex.GetType().ToString()})",  exMessage: ex.Message);
                }
            }

            UpdateETA(stats, true, true);
            FinishedWork?.Invoke(null, stats);
        }

        private bool ImageExists(int postID, string path, out string filename)
        {
            filename = "";
            var fln = CheckCacheForFilename(postID);
            if (fln != null)
            {
                var fpath = path + "\\" + fln;
                if (File.Exists(fpath))
                {
                    filename = fpath;
                    return true;
                }
                else return false;
            }
            return false;
        }

        public bool WriteToCache(int postID, string filename, out string err)
        {
            err = "";
            if (CheckCacheForFilename(postID) != null) return true;

            try
            {
                File.AppendAllLines(CachePath, new string[] { $"{postID}:{filename}" });
                return true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return false;
            }
        }
        public string CheckCacheForFilename(int postID)
        {
            if (File.Exists(CachePath) == false) return null;

            using (FileStream fs = File.Open(CachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (StreamReader sr = new StreamReader(bs))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    try
                    {
                        if (line.Length < 5) continue;

                        var firstIndex = line.IndexOf(':');
                        var stringPostID = line.Substring(0, firstIndex);
                        var stringFilename = line.Substring(firstIndex + 1, line.Length - (firstIndex + 1));
                        int id;

                        if (int.TryParse(stringPostID, out id) == false) continue;
                        if (id == postID) return stringFilename;
                    }
                    catch { continue; }
                }

                return null;
            }
        }

        private void txtImageCount_GotFocus(Object sender, RoutedEventArgs e) => ((TextBox)sender).SelectAll();

        private void listBox_MouseDoubleClick(Object sender, MouseButtonEventArgs e)
        {
            // If nothing is selected, then return
            if (listBox.SelectedIndex == -1) return;

            // ... otherwise get the selected Log
            Log log = (Log)listBox.SelectedItem;

            // if it's a picture, open it using the default program
            if (log.IsPost)
            {
                try
                {
                    Process.Start(log.DownloadedFilepath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else if (log.IsError)
            {
                // if it's an error - show more information
                MessageBox.Show("Showing the logged exception message:\n\n" + log.ErrorMessage, "Exception message", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (log.FoundPosts != null)
            {
                // show
                PostInfo form = new PostInfo(log.FoundPosts);
                form.ShowDialog();
            }
        }

        private void Window_Closing(Object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // Save data when window closes
                File.WriteAllBytes(SavePath, new Save()
                {
                    Tags = txtTags.Text,
                    BlacklistedTags = txtBlacklist.Text,
                    ImageLimit = txtImageCount.Text,
                    SizeLimit = txtSizeLimit.Text,
                    FilePath = txtPath.Text,
                    Left = this.Left,
                    Top = this.Top,
                    Width = this.Width,
                    Height = this.Height,
                    IsFullscreen = this.WindowState == WindowState.Maximized,
                    PageLimit = txtPageLimit.Text,
                    SkipExisting = checkBoxSkip.IsChecked == true
                }.GetBytes());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to create a save file!\n\n" + ex.Message, "Save file error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void openFolder_Click(Object sender, RoutedEventArgs e)
        {
            if (listBox.SelectedIndex == -1) return;
            Log log = (Log)listBox.SelectedItem;

            if (log.DownloadedFilepath.Length > 1)
            {
                try
                {
                    Process.Start(Directory.GetParent(log.DownloadedFilepath).FullName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void openFile_Click(Object sender, RoutedEventArgs e)
        {
            if (listBox.SelectedIndex == -1) return;
            Log log = (Log)listBox.SelectedItem;

            if (log.IsPost)
            {
                try
                {
                    Process.Start(log.DownloadedFilepath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void fav_Click(Object sender, RoutedEventArgs e)
        {
            if (listBox.SelectedIndex == -1) return;
            Log log = (Log)listBox.SelectedItem;

            if (log.IsPost)
            {
                try
                {
                    Window win = this;
                    Dispatcher ds = Dispatcher;
                    await Task.Run(() =>
                    {
                        if (User.Favorite(log.PostInformation, out bool wasUnfavorited) == true)
                        {
                            ds.Invoke(() =>
                            MessageBox.Show(win, $"The selected post was {(wasUnfavorited ? "already" : "successfully")} favorited. (Post ID: {log.PostInformation.PostID})" +
                                $"{(wasUnfavorited ? "\nThe post was now unfavorited." : "")}",
                                $"Post {(wasUnfavorited ? "already" : "")} favorited!", MessageBoxButton.OK, MessageBoxImage.Information));
                        }
                        else
                        {
                            ds.Invoke(() => MessageBox.Show(win, "Failed to favorite/unfavorite the selected post!", 
                                "Failed to favor!", MessageBoxButton.OK, MessageBoxImage.Error));
                        }
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Because I have no better way to auto-hide opened context menus.... 
        private List<ContextMenu> openedContextMenus = new List<ContextMenu>();
        private void Window_PreviewMouseDown(Object sender, MouseButtonEventArgs e)
        {
            // close any opened context menus...
            foreach (var menu in openedContextMenus)
            {
                menu.Visibility = Visibility.Collapsed;
            }
            openedContextMenus.Clear();
        }

        private void TextBlock_MouseRightButtonUp(Object sender, MouseButtonEventArgs e)
        {
            var src = (TextBlock)e.OriginalSource;
            var log = (Log)src.DataContext;

            ContextMenu menu = src.ContextMenu;
            menu.Visibility = Visibility.Visible;
            menu.IsEnabled = log.IsPost;
            openedContextMenus.Add(menu);
        }

        [Serializable]
        public class Log
        {
            public DateTime Timestamp { get; set; }
            public string Message { get; set; }
            public Brush MessageColor { get; set; }

            // If it's an error...
            public bool IsError { get; set; } = false;
            public string ErrorMessage { get; set; }

            // If it's a downloaded post...
            public bool IsPost { get; set; } = false;
            public string DownloadedFilepath { get; set; }
            public SankakuPost PostInformation { get; set; }

            // If it's a "Found" log...
            public string[] FoundPosts { get; set; }

            public Log(string Message, DateTime timestamp, string filename = "", string errMsg = "", SankakuPost postInformation = null, string[] foundPosts = null, bool wasSkipped = false)
            {
                this.Timestamp = timestamp;
                this.Message = Message;
                
                this.DownloadedFilepath = filename;
                this.PostInformation = postInformation;
                this.IsPost = DownloadedFilepath.Length > 1;

                if (IsPost == false)
                {
                    this.ErrorMessage = errMsg;
                    this.IsError = ErrorMessage.Length > 1;
                }

                this.FoundPosts = foundPosts;

                if (IsError) MessageColor = Brushes.Red;
                else if (wasSkipped) MessageColor = Brushes.Gray;
                else MessageColor = Brushes.Black;
            }
        }

        [Serializable]
        public class Save
        {
            public string Tags { get; set; }
            public string BlacklistedTags { get; set; }
            public string ImageLimit { get; set; }
            public string SizeLimit { get; set; }
            public bool ContainVideo { get; set; }
            public string FilePath { get; set; }
            public string PageLimit { get; set; }
            public bool SkipExisting { get; set; }

            public double Left { get; set; }
            public double Top { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public bool IsFullscreen { get; set; }

            public byte[] GetBytes()
            {
                // This shit will convert THIS object to bytes
                using (MemoryStream stream = new MemoryStream())
                {
                    new BinaryFormatter().Serialize(stream, this);
                    return stream.ToArray();
                }
            }
            public static Save GetSave(byte[] source)
            {
                // This shit, however, will convert bytes BACK into this object
                using (MemoryStream stream = new MemoryStream(source))
                {
                    return (Save)new BinaryFormatter().Deserialize(stream);
                }
            }
        }
    }
}
