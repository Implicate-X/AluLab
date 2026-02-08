using System;
using Microsoft.Extensions.DependencyInjection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Data.Core.Plugins;
using AluLab.Common.Views;
using AluLab.Common.Services;
using AluLab.Common.ViewModels;

namespace AluLab.Common;

public partial class App : Application
{
	public IServiceProvider Services { get; private set; } = default!;

	/// <summary>
	/// Optionaler Hook für Host-Projekte, um zusätzliche DI-Registrierungen vorzunehmen
	/// (z.B. Board, Gateway-spezifische Services), ohne dass <c>AluLab.Common</c> diese kennt.
	/// </summary>
	public Action<IServiceCollection>? ConfigureHostServices { get; set; }

	/// <summary>
	/// Optionaler Hook für Host-Projekte, um nach dem Erstellen des DI-Containers
	/// Initialisierungen/Checks durchzuführen (z.B. Board-Erkennung).
	/// </summary>
	public Action<IServiceProvider>? AfterHostServicesBuilt { get; set; }

	public override void Initialize()
	{
		AvaloniaXamlLoader.Load( this );
	}

	public override void OnFrameworkInitializationCompleted()
	{
		BindingPlugins.DataValidators.RemoveAt( 0 );

		ConfigureServices();

		if( ApplicationLifetime is ISingleViewApplicationLifetime single )
		{
			single.MainView = new HousingView();
			return;
		}

		if( ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop )
		{
			desktop.MainWindow = new HousingWindow();
		}

		base.OnFrameworkInitializationCompleted();
	}

	private void ConfigureServices()
	{
		var services = new ServiceCollection();

		services.AddSingleton<SyncService>();
		services.AddTransient<HousingViewModel>();

		ConfigureHostServices?.Invoke( services );

		Services = services.BuildServiceProvider();

		AfterHostServicesBuilt?.Invoke( Services );
	}
}
