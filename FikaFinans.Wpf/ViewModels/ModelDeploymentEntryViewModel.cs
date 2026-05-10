using DevExpress.Mvvm;

namespace FikaFinans.Wpf.ViewModels;

/// <summary>
/// One row in the Settings → Models list. Both fields are editable: the model id is a
/// free-form name the user picks (e.g. <c>gpt-5.4</c>); the deployment name is the
/// Azure-side string (e.g. <c>gpt-5.4-1</c>) used at runtime.
/// </summary>
public sealed class ModelDeploymentEntryViewModel : ViewModelBase
{
    private string _modelId;
    private string _deploymentName;

    public ModelDeploymentEntryViewModel(string modelId, string deploymentName)
    {
        _modelId = modelId;
        _deploymentName = deploymentName;
    }

    public string ModelId
    {
        get => _modelId;
        set => SetProperty(ref _modelId, value, nameof(ModelId));
    }

    public string DeploymentName
    {
        get => _deploymentName;
        set => SetProperty(ref _deploymentName, value, nameof(DeploymentName));
    }
}
