using SankakuAPI;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SankakuDownloader
{
    public partial class MainWindow : Window
    {
        public static MainViewModel ActiveViewModel;
        public const string SaveFileLocation = "settings.xml";

        public MainWindow()
        {
            InitializeComponent();
            ActiveViewModel = FindResource("viewModel") as MainViewModel;
            ActiveViewModel.Logs.CollectionChanged += Logs_CollectionChanged;
            Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;

            try
            {
                ActiveViewModel.LoadData(SaveFileLocation);              
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Failed to load settings -> ");
                MessageBox.Show("Failed to load previous settings!\n" + ex.Message, "Failed to load settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Current_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.Message, "Unhandled Exception!", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        void TextBlock_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // right click should display the context menu
            if (e.RightButton == MouseButtonState.Pressed) { e.Handled = true; return; }

            // left click will display the folder browser dialog
            var f = new System.Windows.Forms.FolderBrowserDialog();         
            
            if (f.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                ActiveViewModel.CurrentJob.DownloadLocation = f.SelectedPath;            
        }

        new void GotFocus(object sender, RoutedEventArgs e) => ((TextBox)sender).SelectAll();

        void ToggleState(bool state)
        {
            _blacklist.IsEnabled = state;
            _checkSkipExisting.IsEnabled = state;
            _checkSkipVideo.IsEnabled = state;
            _limit.IsEnabled = state;
            _limitsize.IsEnabled = state;
            _limitdownload.IsEnabled = state;
            _location.IsEnabled = state;
            _minfavcount.IsEnabled = state;
            _minscore.IsEnabled = state;
            _password.IsEnabled = state;
            _username.IsEnabled = state;
            _tags.IsEnabled = state;
            _startingPage.IsEnabled = state;
            _minscore.IsEnabled = state;
            _checkboxConcurrent.IsEnabled = state;
            _checkResizedOnly.IsEnabled = state;
            _checkPreviousDownloaded.IsEnabled = state;
            _concurrencySlider.IsEnabled = state;
            _namingFormat.IsEnabled = state;
            btnEnqueue.IsEnabled = state;
            btnLogin.IsEnabled = state;
        }

        async void btnStart_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveViewModel.CurrentlyDownloading)
            {
                // stop the downloading
                ActiveViewModel.StopDownloading();
                btnStart.IsEnabled = false;
                return;
            }

            // check if queue has jobs
            if (ActiveViewModel.Jobs.Count == 0)
            {
                MessageBox.Show("No jobs in queue! Please add something to queue!", "Empty queue", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // start downloading
            try
            {
                btnStart.Content = "Stop";
                ToggleState(false);

                ActiveViewModel.SaveData(SaveFileLocation);

                await ActiveViewModel.StartDownloading();
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Download error -> ");
                MessageBox.Show(ex.Message, "Download Error!", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnStart.Content = "Start Downloading";
                btnStart.IsEnabled = true;
                ToggleState(true);
            }
        }

        void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) => ActiveViewModel.SaveData(SaveFileLocation);
        
        void Logs_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (ActiveViewModel.Logs.Count == 0) return;

            var item = ActiveViewModel.Logs.LastOrDefault();
            _list.ScrollIntoView(item);
        }

        #region MenuItem clicks
        void CopyMessage_Click(object sender, RoutedEventArgs e)
        {
            var l = ((MenuItem)sender).DataContext as LogItem;
            Clipboard.SetText(l.Message);
            Clipboard.Flush();
        }

        void CopyFilename_Click(object sender, RoutedEventArgs e)
        {
            var l = ((MenuItem)sender).DataContext as LogItem;
            Clipboard.SetText(l.FileName);
            Clipboard.Flush();
        }

        void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var l = ((MenuItem)sender).DataContext as LogItem;
            Process.Start(l.FullPath);
        }

        void OpenLocation_Click(object sender, RoutedEventArgs e)
        {
            var l = ((MenuItem)sender).DataContext as LogItem;
            Process.Start(System.IO.Path.GetDirectoryName(l.FullPath));
        }
        #endregion

        async void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            ToggleState(false);
            try
            {
                var result = await ActiveViewModel.Login(_password.Password);
                if (result)
                {
                    ActiveViewModel.SaveData(SaveFileLocation);
                    MessageBox.Show("Logged in successfully!", "Login success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Failed to log in!", "Login failure", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (LoginException lex)
            {
                Logger.Log(lex, "btnLogin_Click error -> ");
                MessageBox.Show("Failed to log in!", "Login failure", MessageBoxButton.OK, MessageBoxImage.Warning);              
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "btnLogin_Click unkown error -> ");
                MessageBox.Show("Unknown error occured!\n\n" + ex.Message, "Login failure", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ToggleState(true);
            }
        }

        void OpenFolder(object sender, RoutedEventArgs e)
        {
            if (ActiveViewModel.IsPathSet() == false) return;
            Process.Start(ActiveViewModel.CurrentJob.DownloadLocation);
        }

        private void BtnEnqueue_Click(object sender, RoutedEventArgs e)
        {
            // check download location
            if (ActiveViewModel.IsPathSet() == false)
            {
                MessageBox.Show("Please set the download location!", "Missing download location!",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // check query
            if (ActiveViewModel.CurrentJob.Query == null || ActiveViewModel.CurrentJob.Query?.Length == 0)
            {
                MessageBox.Show("Please specify some tags!", "Missing tags!",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // enqueue current search
            ActiveViewModel.EnqueueCurrentJob();
        }

        private void BtnViewQueue_Click(object sender, RoutedEventArgs e)
        {
            // view the queue
            var queue = new JobQueueWindow(this);
            queue.Show();
        }
    }
}
