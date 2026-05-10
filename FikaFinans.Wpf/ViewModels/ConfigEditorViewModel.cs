using System.IO;
using System.Text.Json;
using System.Windows.Input;
using DevExpress.Mvvm;
using NLog;

namespace FikaFinans.Wpf.ViewModels;

public sealed class ConfigEditorViewModel : ViewModelBase
{
    private readonly ILogger? _logger;

    private string _configFilePath = string.Empty;
    private string _configText = string.Empty;
    private string _validationMessage = string.Empty;
    private bool _hasValidationError;
    private bool _isDirty;
    private bool _isClosing;

    public string ConfigFilePath
    {
        get => _configFilePath;
        set
        {
            SetProperty(ref _configFilePath, value, nameof(ConfigFilePath));
            RaisePropertyChanged(nameof(WindowTitle));
        }
    }

    public string ConfigText
    {
        get => _configText;
        set
        {
            SetProperty(ref _configText, value, nameof(ConfigText));
            _isDirty = true;
            ValidateJson(value);
        }
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value, nameof(ValidationMessage));
    }

    public bool HasValidationError
    {
        get => _hasValidationError;
        private set => SetProperty(ref _hasValidationError, value, nameof(HasValidationError));
    }

    public string WindowTitle => $"Edit {Path.GetFileName(ConfigFilePath)}";

    public bool SaveRequested { get; private set; }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public ConfigEditorViewModel(ILogger logger) : this()
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ConfigEditorViewModel()
    {
        SaveCommand = new DelegateCommand(OnSave, CanSave);
        CancelCommand = new DelegateCommand(OnCancel);
    }

    public void LoadFile(string path)
    {
        ConfigFilePath = path;
        ConfigText = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        _isDirty = false;
        SaveRequested = false;
    }

    private bool CanSave() => !HasValidationError && _isDirty;

    private void OnSave()
    {
        if (HasValidationError) return;
        try
        {
            var dir = Path.GetDirectoryName(ConfigFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tmp = ConfigFilePath + ".tmp";
            File.WriteAllText(tmp, ConfigText);
            File.Move(tmp, ConfigFilePath, overwrite: true);

            SaveRequested = true;
            _isDirty = false;
            _logger?.Info("Config saved: {Path}", ConfigFilePath);

            CloseWindow();
        }
        catch (Exception ex)
        {
            ValidationMessage = $"Save failed: {ex.Message}";
            HasValidationError = true;
            _logger?.Error(ex, "Failed to save config {Path}", ConfigFilePath);
        }
    }

    private void OnCancel() => CloseWindow();

    private void CloseWindow()
    {
        if (_isClosing) return;
        _isClosing = true;
        GetService<ICurrentWindowService>()?.Close();
    }

    private void ValidateJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            HasValidationError = false;
            ValidationMessage = string.Empty;
            return;
        }

        try
        {
            JsonDocument.Parse(text);
            HasValidationError = false;
            ValidationMessage = string.Empty;
        }
        catch (JsonException ex)
        {
            HasValidationError = true;
            ValidationMessage = $"Invalid JSON at line {ex.LineNumber}, col {ex.BytePositionInLine}: {ex.Message}";
        }
    }
}
