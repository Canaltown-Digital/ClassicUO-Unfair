using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Renderer.Arts
{
    /// <summary>
    /// A high-resolution world-only replacement for a legacy UO static.
    /// Texture dimensions are deliberately independent from LogicalWidth/LogicalHeight:
    /// the renderer draws the HD source into the exact legacy footprint and anchor.
    /// </summary>
    public readonly struct HdStaticArt
    {
        public HdStaticArt(Texture2D texture, Rectangle uv, int logicalWidth, int logicalHeight)
        {
            Texture = texture;
            UV = uv;
            LogicalWidth = logicalWidth;
            LogicalHeight = logicalHeight;
        }

        public Texture2D Texture { get; }
        public Rectangle UV { get; }
        public int LogicalWidth { get; }
        public int LogicalHeight { get; }
        public bool IsValid => Texture != null && LogicalWidth > 0 && LogicalHeight > 0;
    }
}
