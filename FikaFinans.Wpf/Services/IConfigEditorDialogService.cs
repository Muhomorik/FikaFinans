namespace FikaFinans.Wpf.Services;

/// <summary>Opens the config-file text editor modal. Returns true if the file was saved.</summary>
public interface IConfigEditorDialogService
{
    bool Edit(string configFilePath, System.Windows.Window? owner = null);
}
