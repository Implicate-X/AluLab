using System;
using Microsoft.Extensions.DependencyInjection;
using Avalonia;
using Avalonia.Controls;
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

	public Action<IServiceCollection>? ConfigureHostServices { get; set; }
	public Action<IServiceProvider>? AfterHostServicesBuilt { get; set; }

	public event Action<Window>? MainWindowReady;

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
			var window = new HousingWindow();
			desktop.MainWindow = window;

			window.Opened += ( _, _ ) => MainWindowReady?.Invoke( window );
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
