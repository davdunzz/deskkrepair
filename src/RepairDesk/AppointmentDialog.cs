using System.Windows;
using System.Windows.Controls;

namespace RepairDesk;

public sealed class AppointmentDialog : Window
{
    private readonly DatePicker _date = new();
    private readonly TextBox _time = new();
    public DateTime? Appointment { get; private set; }

    public AppointmentDialog(DateTime current)
    {
        Title = "Sposta appuntamento";
        Width = 520;
        Height = 350;
        MinWidth = 500;
        MinHeight = 330;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _date.SelectedDate = current.Date;
        _date.Margin = new Thickness(0, 7, 0, 18);
        _date.Height = 38;
        _time.Text = current.ToString("HH:mm");
        _time.Margin = new Thickness(0, 7, 0, 22);
        _time.Height = 42;

        var save = new Button
        {
            Content = "SALVA APPUNTAMENTO",
            MinWidth = 185,
            Height = 44,
            Margin = new Thickness(6, 0, 0, 0)
        };
        save.Click += Save_Click;

        var remove = new Button
        {
            Content = "RIMUOVI DATA",
            MinWidth = 145,
            Height = 44,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC4, 0x3E, 0x4D)),
            Margin = new Thickness(0, 0, 6, 0)
        };
        remove.Click += (_, _) => { Appointment = null; DialogResult = true; };

        var buttons = new Grid();
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(remove, 0);
        Grid.SetColumn(save, 1);
        buttons.Children.Add(remove);
        buttons.Children.Add(save);

        var panel = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
        panel.Children.Add(new TextBlock { Text = "Modifica data e ora", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 18) });
        panel.Children.Add(new TextBlock { Text = "Nuova data", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(_date);
        panel.Children.Add(new TextBlock { Text = "Nuova ora (esempio: 14:30)", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(_time);
        panel.Children.Add(buttons);
        Content = panel;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_date.SelectedDate is null || !TimeSpan.TryParse(_time.Text.Trim(), out var time) || time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
        { MessageBox.Show("Inserisci una data e un orario validi, per esempio 14:30."); return; }
        Appointment = _date.SelectedDate.Value.Date.Add(time); DialogResult = true;
    }
}
