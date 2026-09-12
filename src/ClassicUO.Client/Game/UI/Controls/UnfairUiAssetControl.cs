// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ClassicUO.Game.UI.Controls
{
    /// <summary>
    /// Renders UNFAIR-owned PNG UI art inside a server gump.
    ///
    /// Server marker:
    /// class=unfairui:<asset-key>:<width>:<height>
    ///
    /// Assets are loaded from:
    /// <ClassicUO executable folder>/Unfair/UI/<asset-key>.png
    ///
    /// Stock clients simply ignore the class marker and the server uses graphic 0,
    /// so the extension remains presentation-only and gameplay-safe.
    /// </summary>
    internal sealed class UnfairUiAssetControl : Control
    {
        private static readonly Dictionary<string, Texture2D> _textures =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Texture2D _texture;
        private readonly Rectangle _source;

        public UnfairUiAssetControl(List<string> parts, string classToken)
        {
            X = int.Parse(parts[1]);
            Y = int.Parse(parts[2]);
            IsFromServer = true;
            AcceptMouseInput = false;
            CanMove = false;

            string payload = classToken.StartsWith("class=", StringComparison.OrdinalIgnoreCase)
                ? classToken.Substring("class=".Length)
                : classToken;

            string[] tokens = payload.Split(':');
            if (tokens.Length < 4 ||
                !tokens[0].Equals("unfairui", StringComparison.OrdinalIgnoreCase))
            {
                Dispose();
                return;
            }

            string key = SanitizeKey(tokens[1]);
            if (string.IsNullOrWhiteSpace(key) ||
                !int.TryParse(tokens[2], out int width) ||
                !int.TryParse(tokens[3], out int height))
            {
                Dispose();
                return;
            }

            Width = Math.Clamp(width, 1, 1600);
            Height = Math.Clamp(height, 1, 1200);

            _texture = LoadTexture(key);
            if (_texture == null)
            {
                Dispose();
                return;
            }

            _source = new Rectangle(0, 0, _texture.Width, _texture.Height);
        }

        private static string SanitizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(
                value.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray()
            );
        }

        private static Texture2D LoadTexture(string key)
        {
            if (_textures.TryGetValue(key, out Texture2D cached) &&
                cached != null &&
                !cached.IsDisposed)
            {
                return cached;
            }

            string path = Path.Combine(AppContext.BaseDirectory, "Unfair", "UI", key + ".png");
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                using FileStream stream = File.OpenRead(path);
                Texture2D texture = Texture2D.FromStream(Client.Game.GraphicsDevice, stream);
                _textures[key] = texture;
                return texture;
            }
            catch
            {
                return null;
            }
        }

        public override bool AddToRenderLists(
            RenderLists renderLists,
            int x,
            int y,
            ref float layerDepthRef
        )
        {
            float layerDepth = layerDepthRef;

            if (IsDisposed || _texture == null || _texture.IsDisposed)
            {
                return false;
            }

            Vector3 hueVector = ShaderHueTranslator.GetHueVector(0, false, Alpha, true);

            renderLists.AddGumpWithAtlas(
                batcher =>
                {
                    batcher.Draw(
                        _texture,
                        new Rectangle(x, y, Width, Height),
                        _source,
                        hueVector,
                        layerDepth
                    );
                    return true;
                }
            );

            return base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
        }
    }
}
