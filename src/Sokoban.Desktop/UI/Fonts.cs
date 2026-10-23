using FontStashSharp;
using Jentl.Core.Content;

namespace Sokoban.Desktop.UI;

/// <summary>The game's font in the sizes the UI uses. Glyphs are rasterised on demand.</summary>
internal sealed class Fonts : IDisposable
{
    private readonly FontSystem _system;

    public Fonts()
    {
        // The sprite batch blends non-premultiplied (see JentlGame), so the glyph atlas must be too.
        _system = new FontSystem(new FontSystemSettings { GlyphRenderResult = GlyphRenderResult.NonPremultiplied });
        _system.AddFont(GameFiles.ReadAllBytes("Assets/fonts/LilitaOne-Regular.ttf"));
    }

    public SpriteFontBase Title => _system.GetFont(112);

    public SpriteFontBase Heading => _system.GetFont(52);

    public SpriteFontBase Body => _system.GetFont(34);

    public SpriteFontBase Small => _system.GetFont(22);

    public void Dispose() => _system.Dispose();
}
