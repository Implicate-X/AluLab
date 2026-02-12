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

/// <summary>
/// Represents the main application class for the Avalonia application, providing services and managing the application
/// lifecycle.
/// </summary>
/// <remarks>The App class is responsible for initializing the application, configuring dependency injection
/// services, and setting up the main window or view depending on the application lifetime. It exposes extension points
/// for customizing service registration and for responding when the main window is ready. This class should be used as
/// the entry point for application startup and service configuration in Avalonia-based projects.</remarks>
public partial class App : Application
{
	/// <summary>
	/// Gets the service provider that supplies access to application services.
	/// </summary>
	/// <remarks>This property is typically used to resolve dependencies and access shared services within the
	/// application. The value is initialized with a default implementation and is intended for use with dependency
	/// injection patterns.</remarks>
	public IServiceProvider Services { get; private set; } = default!;

	/// <summary>
	/// Gets or sets the action used to configure the host's service collection for dependency injection.
	/// </summary>
	/// <remarks>This property allows customization of the services registered with the host. The specified action
	/// is invoked during host startup, enabling the registration of additional services or the modification of existing
	/// ones. Use this property to tailor the dependency injection container to the application's requirements.</remarks>
	public Action<IServiceCollection>? ConfigureHostServices { get; set; }
	
	/// <summary>
	/// Gets or sets the action to execute after the host services have been built, providing access to the application's
	/// service provider.
	/// </summary>
	/// <remarks>Use this property to perform additional initialization or configuration that requires access to the
	/// fully constructed service provider. This is useful for scenarios where certain services or resources must be set up
	/// after all dependencies are available.</remarks>
	public Action<IServiceProvider>? AfterHostServicesBuilt { get; set; }

	/// <summary>
	/// Occurs when the main window has been initialized and is ready for interaction.
	/// </summary>
	/// <remarks>Subscribers can use this event to perform actions that require the main window to be fully set up.
	/// This event is typically raised after the application's main window has completed its initialization and is about to
	/// be displayed to the user.</remarks>
	public event Action<Window>? MainWindowReady;

	/// <summary>
	/// Initializes the application by loading its XAML resources and preparing it for use.
	/// </summary>
	/// <remarks>This method should be called during the application's startup sequence to ensure that all
	/// XAML-defined resources and components are properly loaded. It is typically invoked by the framework and does not
	/// need to be called directly in most scenarios.</remarks>
	public override void Initialize()
	{
		AvaloniaXamlLoader.Load( this );
	}

	/// <summary>
	/// Initializes the application framework and configures the main view or window based on the application lifetime
	/// type.
	/// </summary>
	/// <remarks>This method is called when the framework initialization is completed. It sets up the main view for
	/// single view applications or the main window for classic desktop applications. The method also removes the first
	/// data validator from the binding plugins.</remarks>
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

	/// <summary>
	/// Configures and registers application services with the dependency injection container.
	/// </summary>
	/// <remarks>This method adds required services to the service collection, including singleton and transient
	/// dependencies. It also allows for additional service configuration through the optional ConfigureHostServices
	/// callback and notifies subscribers after the service provider is built via the AfterHostServicesBuilt
	/// callback.</remarks>
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
