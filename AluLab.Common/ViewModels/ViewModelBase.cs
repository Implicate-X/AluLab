using CommunityToolkit.Mvvm.ComponentModel;

namespace AluLab.Common.ViewModels;

/// <summary>
/// Serves as a base class for view models, providing property change notification and a standard title property.
/// </summary>
/// <remarks>Inherit from this class to implement view models that support data binding and property change
/// notifications. The Title property can be used to represent the display name or caption of the view model in user
/// interfaces.</remarks>
public abstract class ViewModelBase : ObservableObject
{
  private string _title = string.Empty;

  public string Title { get => _title; set => SetProperty(ref _title, value); }
}
