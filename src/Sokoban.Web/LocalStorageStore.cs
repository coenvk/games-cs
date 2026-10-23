using Jentl.Core.Storage;
using Microsoft.JSInterop;

namespace Sokoban.Web;

/// <summary>Save files kept in the browser's <c>localStorage</c>, one key per file.</summary>
internal sealed class LocalStorageStore(IJSInProcessRuntime js) : ISaveStore
{
    private const string Prefix = "sokoban/";

    public string? ReadText(string name) => js.Invoke<string?>("localStorage.getItem", Prefix + name);

    public void WriteText(string name, string contents) => js.InvokeVoid("localStorage.setItem", Prefix + name, contents);

    public void Delete(string name) => js.InvokeVoid("localStorage.removeItem", Prefix + name);

    public string? Quarantine(string name, DateTimeOffset now)
    {
        if (ReadText(name) is not { } contents)
        {
            return null;
        }

        var target = $"{name}.corrupt-{now.ToUnixTimeSeconds()}";
        WriteText(target, contents);
        js.InvokeVoid("localStorage.removeItem", Prefix + name);
        return $"localStorage[{Prefix}{target}]";
    }
}
