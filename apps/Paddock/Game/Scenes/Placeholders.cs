using static Games.UI.Tr;

namespace Games.Scenes;

// Stand-ins until the real screens land (plan tasks 16-20); each is deleted when its screen is built.
internal static class PlaceholderScenes
{
    public static MessageScene Soon(GameContext context, string title) => new(context, title, T("Coming soon"));
}
