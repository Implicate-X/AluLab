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





//private static IReadOnlyDictionary<string, PinStyle> CreateStylesByPin()
//{
//	Dictionary<string, PinStyle> pinStyleMap = new( StringComparer.Ordinal );

//	void Add( PinStyle style, params string[] pins )
//	{
//		foreach( string pin in pins )
//			pinStyleMap[ pin ] = style;
//	}

//	PinStyle pinStyleBl = new( new SolidColorBrush( Colors.DeepSkyBlue ), new SolidColorBrush( Colors.DodgerBlue ) );
//	PinStyle pinStyleRd = new( new SolidColorBrush( Colors.Red ), new SolidColorBrush( Colors.DarkRed ) );
//	PinStyle pinStyleGn = new( new SolidColorBrush( Colors.LimeGreen ), new SolidColorBrush( Colors.Green ) );
//	PinStyle pinStyleYe = new( new SolidColorBrush( Colors.Yellow ), new SolidColorBrush( Colors.Goldenrod ) );
//	PinStyle pinStyleOr = new( new SolidColorBrush( Colors.Orange ), new SolidColorBrush( Colors.DarkOrange ) );

//	Add( pinStyleBl, "A0", "A1", "A2", "A3", "B0", "B1", "B2", "B3" );
//	Add( pinStyleRd, "S0", "S1", "S2", "S3" );
//	Add( pinStyleGn, "F0", "F1", "F2", "F3" );
//	Add( pinStyleYe, "P", "G", "M" );
//	Add( pinStyleOr, "CN", "CN4" );

//	return pinStyleMap;
//}