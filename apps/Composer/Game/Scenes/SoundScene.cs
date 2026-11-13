using Composer;
using Games.Input;
using Games.UI;
using Jentl.Core.Audio;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static Games.UI.Tr;

namespace Games.Scenes;

/// <summary>
/// The channel's instrument, edited: its wave, its envelope (attack, decay, sustain, release), vibrato and slide, a
/// filter, copies played together, breath, and the settings of the wave chosen. Every change is heard at once, on
/// the pattern's lowest note; the preset can be had back.
/// </summary>
internal sealed class SoundScene : Scene
{
    private const string Sample = "composer-sound-edit";

    private static readonly Wave[] Waves = Enum.GetValues<Wave>();

    private readonly GameContext _context;
    private readonly ComposerSession _session;
    private Form _form = null!;

    public SoundScene(GameContext context, ComposerSession session)
    {
        _context = context;
        _session = session;
        Build();
    }

    public override bool IsOverlay => true;

    private Instrument Sound => _session.Current.Custom ?? _session.Current.Preset;

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
        List<PanelLine> lines =
        [
            new(_session.Current.Custom is null ? T("The preset, as it comes") : T("Edited from the preset"), Palette.Muted),
        ];
        OverlayPanel.Draw(batch, viewport, _context.Fonts, _session.Current.Instrument, lines, _form);
    }

    /// <summary>The rows, those for the wave chosen among them; built again when the wave changes.</summary>
    private void Build()
    {
        var focused = _form?.Focused ?? 0;
        List<FormRow> rows =
        [
            new ChoiceRow(T("Wave"), [.. Waves.Select(WaveName)], () => Array.IndexOf(Waves, Sound.Wave), i =>
            {
                Edit(s => s with { Wave = Waves[i], Partials = Waves[i] == Wave.Partials ? s.Partials ?? [1, 0.5f, 0.33f, 0.25f] : s.Partials });
                Build();
            }),
            Time(T("Attack"), () => Sound.Attack, v => Edit(s => s with { Attack = v })),
            Time(T("Decay"), () => Sound.Decay, v => Edit(s => s with { Decay = v })),
            new SliderRow(T("Sustain"), () => Sound.Sustain, v => Edit(s => s with { Sustain = v }), 0, 1, 0.05f),
            Time(T("Release"), () => Sound.Release, v => Edit(s => s with { Release = v })),
            new ToggleRow(T("Rings away"), () => Sound.Ring, v => Edit(s => s with { Ring = v })),
            new SliderRow(T("Vibrato"), () => Sound.Vibrato, v => Edit(s => s with { Vibrato = v }), 0, 1, 0.05f, v => T("{0} semitones", v.ToString("0.##", Tr.Culture))),
            new SliderRow(T("Slide"), () => Sound.Slide, v => Edit(s => s with { Slide = v }), -24, 12, 1, v => T("{0} semitones", v.ToString("0", Tr.Culture))),
            new SliderRow(T("Filter"), () => FilterPosition(Sound.Cutoff), v => Edit(s => s with { Cutoff = Cutoff(v) }), 0, 1, 0.05f, v => v <= 0 ? T("Open") : T("{0} Hz", Cutoff(v).ToString("0", Tr.Culture))),
            new SliderRow(T("Copies"), () => Sound.Unison, v => Edit(s => s with { Unison = (int)v }), 1, 6, 1, v => v.ToString("0", Tr.Culture)),
            new SliderRow(T("Detune"), () => Sound.Detune, v => Edit(s => s with { Detune = v }), 0, 40, 5, v => T("{0} cents", v.ToString("0", Tr.Culture))),
            new SliderRow(T("Breath"), () => Sound.Breath, v => Edit(s => s with { Breath = v }), 0, 1, 0.05f),
        ];

        if (Sound.Wave == Wave.Pluck)
        {
            rows.Add(new SliderRow(T("Brightness"), () => Sound.Brightness, v => Edit(s => s with { Brightness = v }), 0, 1, 0.1f));
        }

        if (Sound.Wave == Wave.Fm)
        {
            rows.Add(new SliderRow(T("Ratio"), () => Sound.Ratio, v => Edit(s => s with { Ratio = v }), 0.5f, 8, 0.5f, v => v.ToString("0.#", Tr.Culture)));
            rows.Add(new SliderRow(T("Depth"), () => Sound.Index, v => Edit(s => s with { Index = v }), 0, 8, 0.5f, v => v.ToString("0.#", Tr.Culture)));
        }

        rows.Add(new ButtonRow(T("Listen"), Listen));
        rows.Add(new ButtonRow(T("Back to the preset"), () =>
        {
            _session.ResetSound();
            Listen();
            Build();
        }));
        rows.Add(new ButtonRow(T("Done"), () => Scenes.Pop()));
        _form = new Form(_context.Sounds, [.. rows]) { Focused = Math.Min(focused, rows.Count - 1) };
    }

    /// <summary>A time in seconds on a slider that gives as much room to a few milliseconds as to a second or two.</summary>
    private static SliderRow Time(string label, Func<float> get, Action<float> set) => new(
        label,
        () => MathF.Log(Math.Max(get(), 0.002f) / 0.002f) / MathF.Log(1000),
        v => set(MathF.Round(0.002f * MathF.Pow(1000, v), 3)),
        0,
        1,
        0.05f,
        v => Seconds(0.002f * MathF.Pow(1000, v)));

    private static string Seconds(float seconds) => seconds < 0.1f
        ? T("{0} ms", (seconds * 1000).ToString("0", Tr.Culture))
        : T("{0} s", seconds.ToString("0.##", Tr.Culture));

    /// <summary>The filter on a slider: off at the left, then 100 Hz to 10 kHz, evenly by ear.</summary>
    private static float Cutoff(float position) => position <= 0 ? 0 : MathF.Round(100 * MathF.Pow(100, position));

    private static float FilterPosition(float cutoff) => cutoff <= 0 ? 0 : Math.Clamp(MathF.Log(cutoff / 100) / MathF.Log(100), 0.05f, 1);

    private static string WaveName(Wave wave) => wave switch
    {
        Wave.Square => T("square"),
        Wave.Pulse => T("pulse"),
        Wave.Triangle => T("triangle"),
        Wave.Sine => T("sine"),
        Wave.Saw => T("saw"),
        Wave.Noise => T("noise"),
        Wave.Pluck => T("plucked string"),
        Wave.Fm => T("FM"),
        Wave.Partials => T("harmonics"),
        _ => T("choir"),
    };

    private void Edit(Func<Instrument, Instrument> change)
    {
        _session.EditSound(change);
        Listen();
    }

    /// <summary>The sound as it is now, on the pattern's lowest note (or middle C).</summary>
    private void Listen()
    {
        var notes = _session.CurrentPattern.Notes;
        var pitch = notes.Count == 0 ? 60 : notes.Min(n => n.Pitch);
        _context.Sounds.Add(Sample, Synth.Render(new Tune(120, new Voice(_session.Current.Sound(), $"{Composition.NoteName(pitch)} - -"))));
        _context.Sounds.Play(Sample);
    }
}
