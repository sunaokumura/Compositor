using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Compositor;

public partial class MessageWindow : Window
{
    public MessageWindow() { InitializeComponent(); }

    public MessageWindow(string message) : this()
    {
        Message.Text = message;
    }

    void OnOk(object s, RoutedEventArgs e) => Close();
}
