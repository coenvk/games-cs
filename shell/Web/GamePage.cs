using Jentl.Core.Content;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;
using Games;

namespace Games.Web;

/// <summary>
/// Hosts the game on a canvas. Downloads every game file first, then starts the render loop;
/// the page's script calls <see cref="TickDotNet"/> once per animation frame. The same page serves every game:
/// its <c>Entry.Create()</c> says which.
/// </summary>
public sealed class GamePage : ComponentBase, IDisposable
{
    private ShellGame? _game;
    private DotNetObjectReference<GamePage>? _self;

    [Inject]
    private IJSRuntime JsRuntime { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", "canvasHolder");
        builder.OpenElement(2, "canvas");
        builder.AddAttribute(3, "id", "theCanvas");
        builder.AddAttribute(4, "tabindex", "0");
        builder.CloseElement();
        builder.CloseElement();
    }

    [JSInvokable]
    public void TickDotNet()
    {
        if (_game is null)
        {
            var app = Entry.Create();
            var store = new LocalStorageStore((IJSInProcessRuntime)JsRuntime, app.SaveName.ToLowerInvariant() + "/");
            _game = new ShellGame(app, new GameOptions(Server: ServerFromAddress(Navigation.Uri)), store, PlatformFeatures.Browser);
            _game.Run();
        }

        _game.Tick();
    }

    /// <summary>The game server named in the page address (<c>?server=wss://example.org/play</c>), if any.</summary>
    private static Uri? ServerFromAddress(string address)
    {
        var query = new Uri(address).Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair is ["server", var value] && Uri.TryCreate(Uri.UnescapeDataString(value), UriKind.Absolute, out var server) && server.Scheme is "ws" or "wss")
            {
                return server;
            }
        }

        return null;
    }

    public void Dispose()
    {
        _game?.Dispose();
        _self?.Dispose();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        if (!firstRender)
        {
            return;
        }

        var files = await DownloadGameFilesAsync();
        GameFiles.Provider = path => files.TryGetValue(path, out var bytes)
            ? new MemoryStream(bytes, writable: false)
            : throw new FileNotFoundException($"'{path}' is not a shipped game file.", path);

        _self = DotNetObjectReference.Create(this);
        ((IJSInProcessRuntime)JsRuntime).InvokeVoid("initRenderJS", _self);
    }

    /// <summary>Fetches every file named in the embedded list, in parallel, keyed by its path relative to the site root.</summary>
    private async Task<Dictionary<string, byte[]>> DownloadGameFilesAsync()
    {
        using var list = typeof(GamePage).Assembly.GetManifestResourceStream("gamefiles.txt")
            ?? throw new InvalidOperationException("The game file list was not embedded.");
        using var reader = new StreamReader(list);
        var paths = (await reader.ReadToEndAsync())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Replace('\\', '/'))
            .Select(line => line.StartsWith("wwwroot/", StringComparison.Ordinal) ? line["wwwroot/".Length..] : line)
            .ToArray();

        using var http = new HttpClient { BaseAddress = new Uri(Navigation.BaseUri) };
        var contents = await Task.WhenAll(paths.Select(path => http.GetByteArrayAsync(new Uri(path, UriKind.Relative))));
        return paths.Zip(contents).ToDictionary(pair => pair.First, pair => pair.Second);
    }
}
