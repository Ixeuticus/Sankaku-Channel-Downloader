using SankakuAPI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

            try
            {
                ActiveViewModel.LoadData(SaveFileLocation);
            }
            catch
            {
                MessageBox.Show("Failed to load previous settings!", "Failed to load settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        void TextBlock_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var f = new System.Windows.Forms.FolderBrowserDialog();
            
            if (f.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                ActiveViewModel.DownloadLocation = f.SelectedPath;            
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


            // check download location
            if (ActiveViewModel.IsPathSet() == false)
            {
                MessageBox.Show("Please set the download location!", "Missing download location!",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // check query
            if (ActiveViewModel.Query == null || ActiveViewModel.Query?.Length == 0)
            {
                MessageBox.Show("Please specify some tags!", "Missing tags!",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // start downloading
            try
            {
                btnStart.Content = "Stop";
                ToggleState(false);

                await ActiveViewModel.StartDownloading(_password.Password);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnStart.Content = "Start Downloading";
                btnStart.IsEnabled = true;
                ToggleState(true);
            }
        }


        void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ActiveViewModel.SaveData(SaveFileLocation);
        }

        void Logs_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (ActiveViewModel.Logs.Count == 0) return;

            _list.SelectedIndex = ActiveViewModel.Logs.Count - 1;
            _list.ScrollIntoView(_list.SelectedItem);
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
    }
}
