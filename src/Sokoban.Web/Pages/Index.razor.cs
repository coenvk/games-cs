using Jentl.Core.Content;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Sokoban.Desktop;

namespace Sokoban.Web.Pages;

/// <summary>
/// Hosts the game on a canvas. Downloads every game file first, then starts the render loop;
/// the page's script calls <see cref="TickDotNet"/> once per animation frame.
/// </summary>
public sealed partial class Index : IDisposable
{
    private SokobanGame? _game;
    private DotNetObjectReference<Index>? _self;

    [Inject]
    private IJSRuntime JsRuntime { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [JSInvokable]
    public void TickDotNet()
    {
        if (_game is null)
        {
            var store = new LocalStorageStore((IJSInProcessRuntime)JsRuntime);
            _game = new SokobanGame(new GameOptions(), store, PlatformFeatures.Browser);
            _game.Run();
        }

        _game.Tick();
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
        using var list = typeof(Index).Assembly.GetManifestResourceStream("gamefiles.txt")
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
