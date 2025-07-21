using System;
using Microsoft.Maui.Controls;

namespace WraithLite
{
    public partial class LoginModalPage : ContentPage
    {
        public event EventHandler<LoginEventArgs> LoginCompleted;

        public LoginModalPage()
        {
            InitializeComponent();
        }

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            var username = UsernameEntry.Text;
            var password = PasswordEntry.Text;

            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                LoginCompleted?.Invoke(this, new LoginEventArgs(username, password));
                await Navigation.PopModalAsync();
            }
        }
    }

    public class LoginEventArgs : EventArgs
    {
        public string Username { get; }
        public string Password { get; }

        public LoginEventArgs(string username, string password)
        {
            Username = username;
            Password = password;
        }
    }
}
