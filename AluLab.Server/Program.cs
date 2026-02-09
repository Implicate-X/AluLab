using System.Text;
using System.Text.Json;
using AluLab.Common.Relay;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder( args );

builder.Services
	.AddSignalR( options =>
	{
		options.AddFilter<HubInvocationLogFilter>();
	} )
	.AddJsonProtocol( options =>
	{
		options.PayloadSerializerOptions.PropertyNamingPolicy = null;
		options.PayloadSerializerOptions.DictionaryKeyPolicy = null;
	} );

builder.Services.AddSingleton<HubInvocationLogFilter>();

builder.Services.AddCors( options =>
{
	options.AddDefaultPolicy( policy =>
	{
		policy
			.AllowAnyHeader()
			.AllowAnyMethod()
			.AllowCredentials()
			.SetIsOriginAllowed( _ => true );
	} );
} );

var app = builder.Build();

_ = SyncHub.GetSnapshot();

app.UseHttpsRedirection();
app.UseCors();

app.MapHub<SyncHub>( "/sync" ).RequireCors();

app.MapGet( "/sync/state", () =>
{
	var copy = SyncHub.GetSnapshot();
	return Results.Json( new SyncState( copy ) );
} ).RequireCors();

app.MapGet( "/sync/info", () => "Sync hub is available at /sync for SignalR clients (new++)." );
app.MapGet( "/", () => "AluLab IoT " + DateTime.Now.ToString() );

app.MapGet( "/debug/routes", ( EndpointDataSource ds ) =>
{
	var sb = new StringBuilder();
	foreach( var ep in ds.Endpoints )
	{
		sb.AppendLine( ( ep.DisplayName ?? "<no-name>" ) + " -> " + ep.ToString() );
	}
	return Results.Text( sb.ToString(), "text/plain; charset=utf-8" );
} );

// Monitor: liefert initiale Liste und subscribes via SignalR JS client für Live-Updates
app.MapGet( "/sync/monitor", ( HttpContext ctx ) =>
{
	var recent = SyncHub.GetRecent( 200 );
	var snapshot = SyncHub.GetSnapshot();

	// pre-allocate grob, damit weniger Reallocs beim Zusammenbauen passieren
	var sb = new StringBuilder( 16 * 1024 );

	sb.Append( @"<!doctype html>
<html>
<head>
  <meta charset=""utf-8"">
  <title>SyncHub Monitor (Live++)</title>
  <style>
	body{font-family:Segoe UI,Arial;margin:16px;}
	#log{white-space:pre-wrap;font-family:Consolas,monospace;background:#f6f6f6;padding:8px;border-radius:4px}
	#snapshot{white-space:pre-wrap;font-family:Consolas,monospace;background:#eef6ff;padding:8px;border-radius:4px;margin-bottom:12px}
  </style>
</head>
<body>
  <h2>SyncHub - Live Monitor 0.104</h2>
  <p>Server time (UTC): " + DateTime.UtcNow.ToString( "O" ) + @"</p>

  <h3>Snapshot (current pin status)</h3>
  <div id=""snapshot"">" );

	foreach( var kvp in snapshot.OrderBy( x => x.Key ) )
	{
		var key = System.Net.WebUtility.HtmlEncode( kvp.Key );
		sb.Append( $@"<div id=""pin_{key}"">{key} = {kvp.Value}</div>" );
	}

	sb.Append( @"</div>

  <h3>Events (latest first)</h3>
  <div id=""log"">" );

	foreach( var e in recent )
		sb.Append( System.Net.WebUtility.HtmlEncode( e ) + "\n" );

	// IMPORTANT: Close #log , sonst landet das <script> im Log-Container und wird als Text sichtbar
	sb.Append( @"</div>

  <script src=""https://cdn.jsdelivr.net/npm/@microsoft/signalr@7.0.7/dist/browser/signalr.min.js""></script>
  <script>
    (function(){
      const log = document.getElementById('log');
      const snapshot = document.getElementById('snapshot');

      function appendLine(s){ log.textContent = s + '\n' + log.textContent; }

      function setSnapshot(pin, state) {
        const id = 'pin_' + pin;
        let row = document.getElementById(id);
        if (!row) {
          row = document.createElement('div');
          row.id = id;
          snapshot.appendChild(row);
        }
        row.textContent = pin + ' = ' + state;
      }

      try {
        const conn = new signalR.HubConnectionBuilder()
          .withUrl('/sync')
          .withAutomaticReconnect()
          .build();

        conn.on('PinToggled', (pin, state) => {
          const now = new Date().toISOString();
          appendLine(now + ' | ' + pin + ' => ' + state);
          setSnapshot(pin, state);
        });

        conn.start().catch(err => {
          appendLine('Connection error: ' + (err && err.toString ? err.toString() : err));
        });
      } catch (ex) {
        appendLine('Monitor script error: ' + ex);
      }
    })();
  </script>
</body>
</html>" );

	return Results.Content( sb.ToString(), "text/html; charset=utf-8" );
} );

app.Run();

internal record PushDto( string Pin, bool State );

internal sealed class HubInvocationLogFilter : IHubFilter
{
	public async ValueTask<object?> InvokeMethodAsync(
		HubInvocationContext invocationContext,
		Func<HubInvocationContext, ValueTask<object?>> next )
	{
		try
		{
			var id = invocationContext.Context.ConnectionId ?? "unknown";
			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			SyncHub.EnqueueEvent( new SyncHub.SyncEvent( now, id, $"Invoke:{invocationContext.HubMethodName}", true ) );

			return await next( invocationContext );
		}
		catch( Exception ex )
		{
			var id = invocationContext.Context.ConnectionId ?? "unknown";
			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			SyncHub.EnqueueEvent( new SyncHub.SyncEvent( now, id, $"InvokeFail:{invocationContext.HubMethodName}:{ex.GetType().Name}", false ) );
			throw;
		}
	}
}


