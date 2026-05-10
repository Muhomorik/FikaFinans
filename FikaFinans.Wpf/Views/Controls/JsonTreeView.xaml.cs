using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FikaFinans.Wpf.Views.Controls;

public partial class JsonTreeView : UserControl
{
    public static readonly DependencyProperty JsonTextProperty =
        DependencyProperty.Register(nameof(JsonText), typeof(string), typeof(JsonTreeView),
            new PropertyMetadata(string.Empty, OnJsonTextChanged));

    public string JsonText
    {
        get => (string)GetValue(JsonTextProperty);
        set => SetValue(JsonTextProperty, value);
    }

    public JsonTreeView()
    {
        InitializeComponent();
    }

    private static void OnJsonTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is JsonTreeView view)
            view.RebuildTree((string?)e.NewValue ?? string.Empty);
    }

    private void RebuildTree(string json)
    {
        JsonTree.Items.Clear();
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = BuildNode("root", doc.RootElement);
            JsonTree.Items.Add(root);
        }
        catch
        {
            JsonTree.Items.Add(new JsonNodeViewModel("(parse error)", json, null));
        }
    }

    private static JsonNodeViewModel BuildNode(string key, JsonElement element)
    {
        var node = new JsonNodeViewModel(key, GetDisplayValue(element),
            element.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? new ObservableCollection<JsonNodeViewModel>() : null);

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
                node.Children!.Add(BuildNode(prop.Name, prop.Value));
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in element.EnumerateArray())
                node.Children!.Add(BuildNode($"[{i++}]", item));
        }

        return node;
    }

    private static string GetDisplayValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Object => $"{{ {el.EnumerateObject().Count()} }}",
        JsonValueKind.Array => $"[ {el.GetArrayLength()} ]",
        JsonValueKind.String => el.GetString() ?? string.Empty,
        JsonValueKind.Number => el.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => el.GetRawText()
    };
}

public sealed class JsonNodeViewModel(string key, string displayValue, ObservableCollection<JsonNodeViewModel>? children)
{
    public string Key { get; } = key;
    public string DisplayValue { get; } = displayValue;
    public ObservableCollection<JsonNodeViewModel>? Children { get; } = children;
}
