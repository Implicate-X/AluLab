# 📟 AluLab — Platform-Independent 74181 ALU Demonstration System

AluLab is a cross-platform .NET-based system designed to demonstrate the operation of a real 1970s-era 74181 Arithmetic Logic Unit (ALU) across modern software and hardware hosts.

It provides a unified, deterministic view of ALU computation using Avalonia UI, structured hardware abstraction, and synchronized real-time I/O.

> **Status:** 🚧 Active development — APIs, host configurations, and hardware bindings may evolve.

## 🎯 Purpose

AluLab serves as a didactic and architectural showcase:

- Demonstrates the fundamental logic operations of a physical ALU
- Provides a single shared codebase across desktop, mobile, browser (WASM), and embedded hosts
- Integrates modern .NET architecture with legacy hardware
- Offers synchronized, real-time interaction with the ALU via a dedicated hardware host

## ✨ Key Features

### 🧩 Platform-Independent Architecture

Shared domain logic runs on:

- 🖥️ Windows Desktop
- 🐧 Linux (incl. Raspberry Pi)
- 🌐 Browser (WebAssembly)
- 📱 Android
- 🔌 TinyCLR / SitCore
- 🧱 Endpoint Domino (dedicated hardware host)

Only the entry points differ — the architecture remains identical.

### 🔧 Hardware-First Domain Model

Hardware is treated as a first-class domain:

- I²C / SPI / GPIO orchestration
- MCP23017 expander integration
- ALU controller wiring
- Reset sequencing
- Touch/display drivers
- Deterministic pin-toggle propagation

### 🔄 Real-Time Synchronization Backbone

Gateway + Server provide:

- 🔌 SignalR synchronization
- 📡 State endpoints
- 📊 Live HTML monitoring
- 🧮 ALU evaluation API
- 🔁 Distributed pin-toggle propagation
- 📥 Hardware feedback (`AluOutputsChanged`)

### 🧱 Endpoint Domino Hardware Host

Runs .NET 10 and acts as autonomous operator:

- Direct I/O to the 74181
- Continuous sync with the server
- 24/7 unattended operation
- Multi-client access from desktop, browser, mobile

## 📁 Repository Structure

```text
AluLab.Android/           Android host (Avalonia)
AluLab.Browser/           Browser/WASM host
AluLab.Common/            Shared domain logic
AluLab.Board/             Hardware abstraction layer
AluLab.Gateway.*/         Gateway hosts (Desktop, Raspberry, Domino)
AluLab.IoT.Controller/    IoT hardware controller
AluLab.Server/            Web API + Swagger
AluLab.Documentation/     Project documentation
```

Supporting files:

- `Directory.Build.props` / `Directory.Packages.props`
- `CodeMaid.config`
- `Settings.XamlStyler`
- `LICENSE.txt`
- `readme.md` (initial placeholder)

## 🚀 Application Startup

Desktop hosts start in `HousingWindow`, mobile/browser hosts in `HousingView`.

Avalonia initialization pattern:

```csharp
public static AppBuilder BuildAvaloniaApp() =>
	AppBuilder.Configure<App>()
		.UsePlatformDetect()
		.WithInterFont()
		.LogToTrace()
		.AfterSetup(builder =>
		{
			if (builder.Instance is App app)
			{
				app.ConfigureHostServices = services =>
				{
					services.AddLogging(lb =>
					{
						lb.ClearProviders();
						lb.AddSerilog(Log.Logger, dispose: false);
					});

					services.AddSingleton<IBoardHardwareContext, WorkbenchHardwareContext>();
					services.AddSingleton<IBoardProvider, BoardProvider>();
				};

				app.AfterHostServicesBuilt = sp =>
				{
					var logger = sp.GetRequiredService<ILogger<Program>>();

					try
					{
						var provider = sp.GetService<IBoardProvider>();
						if (provider == null)
						{
							logger.LogError("Board: IBoardProvider not registered.");
							return;
						}

						if (!provider.TryGetBoard(out var board, out var error) || board == null)
						{
							logger.LogWarning("Board: not available: {Error}", error);
							return;
						}

						if (board.Initialize())
						{
							logger.LogInformation("Board: initialized.");

							try
							{
								board.AluController.ConfigureSync(
									"https://iot.homelabs.one/sync",
									logger);

								logger.LogInformation("Board: ALU sync configured.");
							}
							catch (Exception ex)
							{
								logger.LogWarning(ex, "Board: failed to configure ALU sync.");
							}

							return;
						}

						logger.LogError("Board: initialization failed: {Reason}",
							board.LastInitializationException?.ToString() ?? "Unknown error");
					}
					catch (Exception ex)
					{
						logger.LogError(ex, "Board: early check exception");
					}
				};
			}
		});
```

## 🛠️ Building

### 📌 Requirements

- .NET 8 or later
- Avalonia UI 12
- Platform SDKs (Android, WASM, TinyCLR)
- Optional hardware host: Endpoint Domino or SitCore

### 📦 Steps

Clone the repository:

```bash
git clone https://github.com/Implicate-X/AluLab
```

Restore packages:

```bash
dotnet restore
```

Build:

```bash
dotnet build AluLab.slnx
```

Run a host:

```bash
dotnet run --project AluLab.Gateway.Desktop
```

## 🔌 Hardware Integration

Hardware access is provided through:

- `IBoardProvider`
- `IBoardHardwareContext`
- `AluController`

The hardware host handles:

- Pin toggling
- MCP23017 output mapping
- ALU output reporting
- Sync with the server

## 🌐 Server Component

The server provides:

- REST endpoints
- SignalR synchronization
- Swagger documentation
- Real-time ALU state distribution

Designed for deterministic multi-client operation.

## 📜 License

This project is licensed under the Unlicense (public domain). See `LICENSE.txt`.

## 🤝 Contributing

Contributions are welcome. Please open issues or submit pull requests.

## 📬 Contact

Use the GitHub issue tracker for questions or collaboration.
