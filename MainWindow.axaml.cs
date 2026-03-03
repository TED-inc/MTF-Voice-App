using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using ScottPlot.Avalonia;

namespace MTFvoiceTools;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        InitializeComponent();
        double[] dataX = { 1, 2, 3, 4, 5 };
        double[] dataY = { 1, 4, 9, 16, 25 };

        AvaPlot avaPlot1 = this.Find<AvaPlot>("AvaPlot1");
        avaPlot1.Plot.Add.Scatter(dataX, dataY);
        avaPlot1.Refresh();
    }
    
    public void ClickHandler(object sender, RoutedEventArgs args)
    {
        Message.Text = "Button clicked!";
    }
}