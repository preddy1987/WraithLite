using WraithLite.ViewModels;

namespace WraithLite
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
            BindingContext = new MainViewModel();
        }

        void SendCommand(object sender, EventArgs e)
        {
            if (BindingContext is MainViewModel vm)
            {
                vm.SendCommand();
            }
        }
    }

}
