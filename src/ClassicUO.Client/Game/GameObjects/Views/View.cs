// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Runtime.CompilerServices;
using ClassicUO.Configuration;
using ClassicUO.IO;
using ClassicUO.Assets;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.GameObjects
{
    enum ObjectHandlesStatus
    {
        NONE,
        OPEN,
        CLOSED,
        DISPLAYING
    }

    internal abstract partial class GameObject
    {
        public byte AlphaHue;
        public bool AllowedToDraw = true;
        public bool InChunkMesh;
        public int MeshSpriteIndex = -1;
        public ObjectHandlesStatus ObjectHandlesStatus;
        public Rectangle FrameInfo;
        protected bool IsFlipped;

        public abstract bool Draw(UltimaBatcher2D batcher, int posX, int posY, float depth);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float CalculateDepthZ()
        {
            int x = X;
            int y = Y;
            int z = PriorityZ;

            // Offsets are in SCREEN coordinates
            if (Offset.X > 0 && Offset.Y < 0)
            {
                // North
            }
            else if (Offset.X > 0 && Offset.Y == 0)
            {
                // Northeast
                x++;
            }
            else if (Offset.X > 0 && Offset.Y > 0)
            {
                // East
                z += Math.Max(0, (int)Offset.Z);
                x++;
            }
            else if (Offset.X == 0 && Offset.Y > 0)
            {
                // Southeast
                x++;
                y++;
            }
            else if (Offset.X < 0 && Offset.Y > 0)
            {
                // South
                z += Math.Max(0, (int)Offset.Z);
                y++;
            }
            else if (Offset.X < 0 && Offset.Y == 0)
            {
                // Southwest
                y++;
            }
            else if (Offset.X < 0 && Offset.Y > 0)
            {
                // West
            }
            else if (Offset.X == 0 && Offset.Y < 0)
            {
                // Northwest
            }

            return (x + y) + (127 + z) * 0.01f;
        }

        public Rectangle GetOnScreenRectangle()
        {
            Rectangle prect = Rectangle.Empty;

            prect.X = (int)(RealScreenPosition.X - FrameInfo.X + 22 + Offset.X);
            prect.Y = (int)(RealScreenPosition.Y - FrameInfo.Y + 22 + (Offset.Y - Offset.Z));
            prect.Width = FrameInfo.Width;
            prect.Height = FrameInfo.Height;

            return prect;
        }

        public virtual bool TransparentTest(int z)
        {
            return false;
        }

        /// <summary>
        /// Resolve world art without changing the legacy Art.GetArt() contract used by UI,
        /// item-browser previews and pixel picking. HD textures can therefore have many more
        /// source pixels while still drawing into the original UO logical width/height.
        /// </summary>
        private static bool TryGetStaticRenderData(
            ushort graphic,
            out Texture2D texture,
            out Rectangle source,
            out int logicalWidth,
            out int logicalHeight,
            out Vector2 baseScale,
            out bool isHd
        )
        {
            if (Client.Game.UO.Arts.TryGetHdStatic(graphic, out var hd) && hd.IsValid)
            {
                texture = hd.Texture;
                source = hd.UV;
                logicalWidth = hd.LogicalWidth;
                logicalHeight = hd.LogicalHeight;
                baseScale = new Vector2(
                    logicalWidth / (float)source.Width,
                    logicalHeight / (float)source.Height
                );
                isHd = true;
                return true;
            }

            ref readonly var artInfo = ref Client.Game.UO.Arts.GetArt(graphic);
            if (artInfo.Texture == null)
            {
                texture = null;
                source = Rectangle.Empty;
                logicalWidth = logicalHeight = 0;
                baseScale = Vector2.One;
                isHd = false;
                return false;
            }

            texture = artInfo.Texture;
            source = artInfo.UV;
            logicalWidth = source.Width;
            logicalHeight = source.Height;
            baseScale = Vector2.One;
            isHd = false;
            return true;
        }

        protected static void DrawStatic(
            UltimaBatcher2D batcher,
            ushort graphic,
            int x,
            int y,
            Vector3 hue,
            float depth,
            bool isWet = false
        )
        {
            if (
                TryGetStaticRenderData(
                    graphic,
                    out var texture,
                    out var source,
                    out int logicalWidth,
                    out int logicalHeight,
                    out var baseScale,
                    out _
                )
            )
            {
                ref var index = ref Client.Game.UO.FileManager.Arts.File.GetValidRefEntry(graphic + 0x4000);
                index.Width = (short)((logicalWidth >> 1) - 22);
                index.Height = (short)(logicalHeight - 44);

                x -= index.Width;
                y -= index.Height;

                var pos = new Vector2(x, y);
                var scale = baseScale;
                if (isWet)
                {
                    batcher.Draw(
                        texture,
                        pos,
                        source,
                        hue,
                        0f,
                        Vector2.Zero,
                        scale,
                        SpriteEffects.None,
                        depth + 0.5f
                    );

                    var sin = (float)Math.Sin(Time.Ticks / 1000f);
                    var cos = (float)Math.Cos(Time.Ticks / 1000f);
                    scale *= new Vector2(1.1f + sin * 0.1f, 1.1f + cos * 0.5f * 0.1f);
                }

                batcher.Draw(
                    texture,
                    pos,
                    source,
                    hue,
                    0f,
                    Vector2.Zero,
                    scale,
                    SpriteEffects.None,
                    depth + 0.5f
                );
            }
        }

        protected static void DrawGump(
            UltimaBatcher2D batcher,
            ushort graphic,
            int x,
            int y,
            Vector3 hue,
            float depth
        )
        {
            ref readonly var gumpInfo = ref Client.Game.UO.Gumps.GetGump(graphic);

            if (gumpInfo.Texture != null)
            {
                batcher.Draw(
                    gumpInfo.Texture,
                    new Vector2(x, y),
                    gumpInfo.UV,
                    hue,
                    0f,
                    Vector2.Zero,
                    1f,
                    SpriteEffects.None,
                    depth + 0.5f
                );
            }
        }

        protected static void DrawStaticRotated(
            UltimaBatcher2D batcher,
            ushort graphic,
            int x,
            int y,
            float angle,
            Vector3 hue,
            float depth
        )
        {
            if (
                TryGetStaticRenderData(
                    graphic,
                    out var texture,
                    out var source,
                    out int logicalWidth,
                    out int logicalHeight,
                    out _,
                    out _
                )
            )
            {
                ref var index = ref Client.Game.UO.FileManager.Arts.File.GetValidRefEntry(graphic + 0x4000);
                index.Width = (short)((logicalWidth >> 1) - 22);
                index.Height = (short)(logicalHeight - 44);

                batcher.Draw(
                    texture,
                    new Rectangle(
                        x - index.Width,
                        y - index.Height,
                        logicalWidth,
                        logicalHeight
                    ),
                    source,
                    hue,
                    angle,
                    Vector2.Zero,
                    SpriteEffects.None,
                    depth + 0.5f
                );
            }
        }

        protected static void DrawStaticAnimated(
            UltimaBatcher2D batcher,
            ushort graphic,
            int x,
            int y,
            Vector3 hue,
            bool shadow,
            float depth,
            bool isWet = false
        )
        {
            ref UOFileIndex index = ref Client.Game.UO.FileManager.Arts.File.GetValidRefEntry(graphic + 0x4000);

            graphic = (ushort)(graphic + index.AnimOffset);

            if (
                TryGetStaticRenderData(
                    graphic,
                    out var texture,
                    out var source,
                    out int logicalWidth,
                    out int logicalHeight,
                    out var baseScale,
                    out bool isHd
                )
            )
            {
                index = ref Client.Game.UO.FileManager.Arts.File.GetValidRefEntry(graphic + 0x4000);
                index.Width = (short)((logicalWidth >> 1) - 22);
                index.Height = (short)(logicalHeight - 44);

                x -= index.Width;
                y -= index.Height;

                Vector2 pos = new Vector2(x, y);

                // DrawShadow currently derives world size directly from source pixel dimensions.
                // Until that helper gets its own logical-size overload, do not let an HD texture
                // accidentally cast an 8x shadow. Walls do not use static shadows; trees/rocks can
                // opt into HD once the shadow path is upgraded.
                if (shadow && !isHd)
                {
                    batcher.DrawShadow(texture, pos, source, false, depth + 0.25f);
                }

                var scale = baseScale;
                if (isWet)
                {
                    batcher.Draw(
                        texture,
                        pos,
                        source,
                        hue,
                        0f,
                        Vector2.Zero,
                        scale,
                        SpriteEffects.None,
                        depth + 0.5f
                    );

                    var sin = (float)Math.Sin(Time.Ticks / 1000f);
                    var cos = (float)Math.Cos(Time.Ticks / 1000f);
                    scale *= new Vector2(1.1f + sin * 0.1f, 1.1f + cos * 0.5f * 0.1f);
                }

                batcher.Draw(
                    texture,
                    pos,
                    source,
                    hue,
                    0f,
                    Vector2.Zero,
                    scale,
                    SpriteEffects.None,
                    depth + 0.5f
                );
            }
        }
    }
}
