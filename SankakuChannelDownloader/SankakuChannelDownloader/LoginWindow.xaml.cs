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
using SankakuChannelAPI;
using System.Windows.Threading;

namespace SankakuChannelDownloader
{
    public partial class LoginWindow : Window
    {       
        public bool Success { get; set; } = false;
        public LoginWindow()
        {
            InitializeComponent();

            // Focus on "txtUsername" textbox
            txtUsername.Focus();
        }

        private async void Window_KeyDown(Object sender, KeyEventArgs e)
        {
            // This gets called when user presses a key on their keyboard...   WOOOAH
            if (e.Key == Key.Enter && txtUsername.IsEnabled)
            {
                if (txtUsername.Text.Length < 2)
                {
                    txtUsername.SelectAll();
                    return;
                }
                if (txtPassword.Password.Length < 2)
                {
                    txtPassword.Clear();
                    txtPassword.Focus();
                    return;
                }
                
                // Basically... if they pressed ENTER, it will attempt to login using my API  :D
                txtPassword.IsEnabled = false;
                txtUsername.IsEnabled = false;
                SankakuChannelUser user = new SankakuChannelUser(txtUsername.Text, txtPassword.Password);
                
                // Attempt to login - start Task, so the UI thread won't get blocked by unnecessary work
                var success = await Task.Run(() => LoginUser(user, associatedWindow: this, associatedWindowDispatcher: Dispatcher)); 
                if (success)
                {
                    // if successful login, set the user and close the window
                    MainWindow.User = user;
                    Success = true;
                    this.Close();
                }
                else
                {
                    txtPassword.IsEnabled = true;
                    txtUsername.IsEnabled = true;
                    txtUsername.SelectAll();
                }          
            }
        }

        public static bool LoginUser(SankakuChannelUser user, 
            bool supressMessagebox = false, 
            Window associatedWindow = null, 
            Dispatcher associatedWindowDispatcher = null)
        {
            bool tooMany = false;
            if (user.IsAuthenicated) user.LogOut();

            try
            {
                var success = user.Authenticate(out tooMany);
                if (success) return true;
                else
                {
                    if (supressMessagebox == false)
                        associatedWindowDispatcher?.Invoke(() => MessageBox.Show(associatedWindow, $"Failed to login!" +
                            $"{(tooMany ? "\n\nToo many requests! Please wait a few minutes before trying again!" : " Try again!")}", "Login fail", MessageBoxButton.OK, MessageBoxImage.Error));

                    return false;
                }
            }
            catch (Exception ex)
            {
                if (supressMessagebox == false)
                    associatedWindowDispatcher?.Invoke(() => MessageBox.Show(associatedWindow, "Failed to login. Unexpected error occurred:\n\n" + ex.Message, "Failed to login!", MessageBoxButton.OK, MessageBoxImage.Error));
                return false;
            } 
        }
    }
}
