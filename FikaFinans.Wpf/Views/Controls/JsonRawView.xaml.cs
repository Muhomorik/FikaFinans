using System.Windows;
using System.Windows.Controls;

namespace FikaFinans.Wpf.Views.Controls;

public partial class JsonRawView : UserControl
{
    public static readonly DependencyProperty JsonTextProperty =
        DependencyProperty.Register(nameof(JsonText), typeof(string), typeof(JsonRawView),
            new PropertyMetadata(string.Empty, OnJsonTextChanged));

    public string JsonText
    {
        get => (string)GetValue(JsonTextProperty);
        set => SetValue(JsonTextProperty, value);
    }

    public JsonRawView()
    {
        InitializeComponent();
    }

    private static void OnJsonTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is JsonRawView view)
            view.JsonTextBox.Text = (string?)e.NewValue ?? string.Empty;
    }
}
