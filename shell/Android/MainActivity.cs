using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Jentl.Core.Storage;
using Microsoft.Xna.Framework;

namespace Games;

/// <summary>
/// The app on a phone: one activity, filling the screen in either orientation, running the shell with the app's games.
/// Saves go to the app's own storage; files shipped with the game are read from the package (the title container).
/// </summary>
[Activity(
    Label = "@string/app_name",
    Icon = "@mipmap/ic_launcher",
    MainLauncher = true,
    AlwaysRetainTaskState = true,
    LaunchMode = LaunchMode.SingleInstance,
    ScreenOrientation = ScreenOrientation.FullUser,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.UiMode)]
public sealed class MainActivity : AndroidGameActivity
{
    private ShellGame? _game;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var app = Entry.Create();
        _game = new ShellGame(app, new GameOptions(), SaveStore.ForApp(app.SaveName), PlatformFeatures.Android)
        {
            ToBackground = () => MoveTaskToBack(true),
        };
        var view = _game.Services.GetService<View>();
        view.SetOnApplyWindowInsetsListener(new InsetsListener(_game));
        SetContentView(view);

        // From Android 13 the system takes the back gesture itself unless the app asks for it: the game's Back.
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(0, new BackCallback(_game));
        }

        _game.Run();
    }

    protected override void OnPause()
    {
        _game?.Away(true);
        base.OnPause();
    }

    protected override void OnResume()
    {
        base.OnResume();
        _game?.Away(false);
    }

    private sealed class BackCallback(ShellGame game) : Java.Lang.Object, Android.Window.IOnBackInvokedCallback
    {
        public void OnBackInvoked() => game.PressBack();
    }

    /// <summary>
    /// Keeps the game clear of the camera's cut-out and of the navigation bar: the game fills the screen and the status
    /// bar is hidden, but the gesture handle stays, drawn over the bottom edge.
    /// </summary>
    private sealed class InsetsListener(ShellGame game) : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var cutout = insets.GetInsets(WindowInsets.Type.DisplayCutout());
                var navigation = insets.GetInsetsIgnoringVisibility(WindowInsets.Type.NavigationBars());
                game.Insets = new Jentl.Core.Insets(
                    Math.Max(cutout.Left, navigation.Left),
                    cutout.Top,
                    Math.Max(cutout.Right, navigation.Right),
                    Math.Max(cutout.Bottom, navigation.Bottom));
            }

            return view.OnApplyWindowInsets(insets) ?? insets;
        }
    }
}
