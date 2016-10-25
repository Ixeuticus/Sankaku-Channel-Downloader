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
using System.Windows.Shapes;

namespace SankakuChannelDownloader
{
    public partial class PostInfo : Window
    {
        public PostInfo(string[] foundPosts)
        {
            InitializeComponent();

            listBox.ItemsSource = foundPosts;
            listBox.Items.Refresh();
        }

        private void MenuItem_Click(Object sender, RoutedEventArgs e)
        {
            if (listBox.SelectedIndex == -1) return;

            string selected = (string)listBox.SelectedItem;
            Clipboard.SetText(selected);
        }
    }
}
