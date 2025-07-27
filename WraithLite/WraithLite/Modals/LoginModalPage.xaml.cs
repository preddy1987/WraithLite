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
            // Trim whitespace from inputs to avoid accidental spaces
            var username = UsernameEntry.Text?.Trim();
            var password = PasswordEntry.Text?.Trim();

            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                // Fire the event to send credentials to the main page
                LoginCompleted?.Invoke(this, new LoginEventArgs(username, password));
                // Close the modal after login attempt
                await Navigation.PopModalAsync();
            }
            // If either field is blank, do nothing (stay on modal to let user fill both)
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            // Simply close the modal without doing anything
            await Navigation.PopModalAsync();
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
