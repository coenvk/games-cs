using Jentl.Core.Cards;
using Jentl.Core.UI;
using Microsoft.Xna.Framework.Graphics;

namespace Games;

/// <summary>The card art shared by the card apps (Solitaire, Card Table).</summary>
internal static class CardTableArt
{
    public static void Load(GameContext context, GraphicsDevice device)
    {
        var art = context.Add(new CardArt(device));
        context.Add(new CardRenderer(art, new FontTextDrawer(context.Fonts)));
    }

    extension(GameContext context)
    {
        public CardRenderer Cards => context.Get<CardRenderer>();
    }
}
