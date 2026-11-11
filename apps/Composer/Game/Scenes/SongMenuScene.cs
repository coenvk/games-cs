using Composer;
using Jentl.Core.Audio;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Games.Input;
using Games.UI;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// The editor's menu: the song's name, saving it, taking it out of the Composer (as the code of a tune for a game, or
/// as a WAV file on the desktop), opening another, and leaving.
/// </summary>
internal sealed class SongMenuScene : Scene
{
    private readonly GameContext _context;
    private readonly ComposerScene _editor;
    private readonly Form _form;
    private string? _note;

    public SongMenuScene(GameContext context, ComposerScene editor)
    {
        _context = context;
        _editor = editor;
        var session = editor.Session;
        List<FormRow> rows =
        [
            new ButtonRow(T("Back to the song"), () => Scenes.Pop()),
            new TextRow(T("Name"), () => session.Song.Title, session.Rename, maxLength: 32),
            new ButtonRow(T("Save"), Save),
            new ButtonRow(T("Copy as code for a game"), CopyCode),
        ];
        if (context.Platform.CanQuit)
        {
            rows.Add(new ButtonRow(T("Save as a WAV file"), SaveWav));
        }

        rows.Add(new ButtonRow(T("Open another song"), () => Scenes.Replace(new SongListScene(_context))));
        rows.Add(new ButtonRow(T("Leave the Composer"), () => Navigation.ToMainMenu(Scenes, _context)));
        _form = new Form(context.Sounds, [.. rows]);
    }

    public override bool IsOverlay => true;

    protected override void OnEnter() => _editor.Stop();

    public override void Update(TimeSpan elapsed, InputState input)
    {
        if (!_form.IsEditing && _context.Actions.WasPressed(input, GameAction.Back))
        {
            Scenes.Pop();
            return;
        }

        _form.Update(input, Controls.Ui, elapsed);
    }

    public override void Draw(SpriteBatch batch, Rectangle viewport)
    {
        var session = _editor.Session;
        List<PanelLine> lines =
        [
            new(T("{0} bars, {1} channels", session.Song.Bars, session.Song.Channels.Count), Palette.Muted),
            new(_note ?? (session.IsChanged ? T("Not saved yet") : T("Saved")), _note is null ? Palette.Muted : Palette.Accent),
        ];
        OverlayPanel.Draw(batch, viewport, _context.Fonts, session.Song.Title, lines, _form);
    }

    private void Save()
    {
        _context.Get<SongShelf>().Save(_editor.Session.Song);
        _editor.Session.Saved();
        _note = T("Saved");
    }

    private void CopyCode()
    {
        var code = _editor.Session.Song.ToCode();
        _note = Clipboard.TrySetText(code) ? T("The code is on the clipboard") : T("There is no clipboard here");
    }

    /// <summary>Renders the song once through and writes it to the Music folder's Composer folder.</summary>
    private void SaveWav()
    {
        var song = _editor.Session.Song;
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        var folder = Path.Combine(music.Length > 0 ? music : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music"), "Composer");
        var file = Path.Combine(folder, SongShelf.FileOf(song.Title).Replace(".json", ".wav", StringComparison.Ordinal));
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(file, Synth.ToWav(Synth.Render(song.ToTune())));
            _note = T("Written to {0}", file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _note = T("Could not write the file: {0}", e.Message);
        }
    }
}
