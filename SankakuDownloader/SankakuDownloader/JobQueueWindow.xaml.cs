using System;
using System.Text;
using System.Linq;
using System.Windows;
using System.Collections.Generic;


namespace SankakuDownloader
{
    public partial class JobQueueWindow : Window
    {
        public static JobQueueWindow ActiveWindow = null;

        public JobQueueWindow(Window ownerWindow)
        {
            this.Owner = ownerWindow;
            InitializeComponent();

            if (ActiveWindow != null) ActiveWindow.Close();
            ActiveWindow = this;

            MainWindow.ActiveViewModel.JobsCollectionChanged += ActiveViewModel_JobsCollectionChanged;
            Closing += (a,b) => MainWindow.ActiveViewModel.JobsCollectionChanged -= ActiveViewModel_JobsCollectionChanged;
        }

        private void ActiveViewModel_JobsCollectionChanged(object sender, bool e) => list.Items.Refresh();

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            var items = list.SelectedItems.Cast<JobConfiguration>().ToList();
            MainWindow.ActiveViewModel.Jobs = new Queue<JobConfiguration>(MainWindow.ActiveViewModel.Jobs.Where(x => !items.Contains(x)));
        }


        private void CopyJobConfiguration(object sender, RoutedEventArgs e)
        {
            if (MainWindow.ActiveViewModel.CurrentlyDownloading)
            {
                MessageBox.Show("Can't copy configuration while downloading!", "Can't copy", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var item = list.SelectedItem as JobConfiguration;
            if (item == null)
            {
                MessageBox.Show("No job selected to copy from!", "Nothing selected", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (MessageBox.Show("This will copy the job configuration. Are you sure?", "Copy job",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                MainWindow.ActiveViewModel.CurrentJob = new JobConfiguration(item);
            }
        }
    }
}
