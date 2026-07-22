using System.Windows;

namespace VideoSerialVisualizer.Views;

public partial class RenameCategoryDialog : Window
{
    public string? ResultName { get; private set; }

    public RenameCategoryDialog(string currentName, string? title = null)
    {
        InitializeComponent();
        if (title is not null)
            Title = title;

        NameTextBox.Text = currentName;
        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var text = NameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        ResultName = text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    public static string? PromptForName(string currentName, Window? owner, string? title = null)
    {
        var dialog = new RenameCategoryDialog(currentName, title) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.ResultName : null;
    }
}
