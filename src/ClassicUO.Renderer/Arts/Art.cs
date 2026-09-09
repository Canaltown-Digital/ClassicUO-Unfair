using ClassicUO.Assets;
using ClassicUO.Utility;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SDL3;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;

namespace ClassicUO.Renderer.Arts
{
    public sealed class Art
    {
        private readonly SpriteInfo[] _spriteInfos;
        private readonly TextureAtlas _atlas;
        private readonly PixelPicker _picker = new PixelPicker();
        private readonly Rectangle[] _realArtBounds;
        private readonly ArtLoader _artLoader;
        private readonly HuesLoader _huesLoader;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly string _hdStaticsRoot;
        private readonly Dictionary<uint, HdStaticArt> _hdStatics = new Dictionary<uint, HdStaticArt>();
        private readonly HashSet<uint> _hdStaticsChecked = new HashSet<uint>();

        public Art(ArtLoader artLoader, HuesLoader huesLoader, GraphicsDevice device)
        {
            _artLoader = artLoader;
            _huesLoader = huesLoader;
            _graphicsDevice = device;
            _atlas = new TextureAtlas(device, 4096, 4096, SurfaceFormat.Color);
            _spriteInfos = new SpriteInfo[_artLoader.File.Entries.Length];
            _realArtBounds = new Rectangle[_spriteInfos.Length];

            string root = Environment.GetEnvironmentVariable("UNFAIR_HD_ART_ROOT");
            if (!string.IsNullOrWhiteSpace(root))
            {
                _hdStaticsRoot = Path.Combine(root, "HD", "Statics");
                Log.Info($"UNFAIR HD art root: {_hdStaticsRoot}");
            }
        }

        public ref readonly SpriteInfo GetLand(uint idx)
            => ref Get((uint)(idx & ~0x4000));

        public ref readonly SpriteInfo GetArt(uint idx)
            => ref Get(idx + 0x4000);

        /// <summary>
        /// Returns an optional high-resolution texture for world rendering while preserving
        /// the legacy static's exact logical dimensions. UI previews, mouse picking and all
        /// other legacy art consumers continue to use GetArt(), so introducing HD art does
        /// not change TileData, placement, browser geometry or selection semantics.
        /// </summary>
        public bool TryGetHdStatic(uint itemId, out HdStaticArt hdArt)
        {
            if (_hdStatics.TryGetValue(itemId, out hdArt))
            {
                return true;
            }

            if (_hdStaticsChecked.Contains(itemId))
            {
                hdArt = default;
                return false;
            }

            _hdStaticsChecked.Add(itemId);
            hdArt = default;

            if (string.IsNullOrWhiteSpace(_hdStaticsRoot))
            {
                return false;
            }

            string path = Path.Combine(_hdStaticsRoot, $"{itemId}.png");
            if (!File.Exists(path))
            {
                return false;
            }

            // The archive remains authoritative for logical geometry. The HD PNG is only
            // allowed to replace pixels, never the object's UO footprint or anchor.
            var legacy = _artLoader.GetArt(itemId + ArtLoader.MAX_LAND_DATA_INDEX_COUNT);
            if (legacy.Pixels.IsEmpty || legacy.Width <= 0 || legacy.Height <= 0)
            {
                Log.Warn($"UNFAIR HD static {itemId}.png has no valid legacy art to anchor to");
                return false;
            }

            Texture2D texture = null;
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    texture = Texture2D.FromStream(_graphicsDevice, stream);
                }

                if (texture == null || texture.Width <= 0 || texture.Height <= 0)
                {
                    texture?.Dispose();
                    Log.Warn($"UNFAIR HD static {itemId}.png could not be decoded");
                    return false;
                }

                // Keep the first implementation conservative. 4K per axis is already far
                // beyond legacy UO art and matches the renderer atlas ceiling elsewhere.
                if (texture.Width > 4096 || texture.Height > 4096)
                {
                    Log.Warn($"UNFAIR HD static {itemId}.png is too large: {texture.Width}x{texture.Height}");
                    texture.Dispose();
                    return false;
                }

                hdArt = new HdStaticArt(
                    texture,
                    new Rectangle(0, 0, texture.Width, texture.Height),
                    legacy.Width,
                    legacy.Height
                );
                _hdStatics[itemId] = hdArt;

                Log.Info(
                    $"UNFAIR HD static 0x{itemId:X4}: {texture.Width}x{texture.Height} -> logical {legacy.Width}x{legacy.Height}"
                );

                return true;
            }
            catch (Exception e)
            {
                texture?.Dispose();
                Log.Warn($"UNFAIR HD static {itemId}.png failed: {e.Message}");
                return false;
            }
        }

        private ref readonly SpriteInfo Get(uint idx)
        {
            if (idx >= _spriteInfos.Length)
                return ref SpriteInfo.Empty;

            ref var spriteInfo = ref _spriteInfos[idx];

            if (spriteInfo.Texture == null)
            {
                var artInfo = _artLoader.GetArt(idx);

                if (artInfo.Pixels.IsEmpty && idx > 0)
                {
                    // Trying to load a texture that does not exist in the client MULs
                    // Degrading gracefully and only crash if not even the fallback ItemID exists
                    Log.Error(
                        $"Texture not found for sprite: idx: {idx}; itemid: {(idx > 0x4000 ? idx - 0x4000 : '-')}"
                    );
                    return ref Get(0); // ItemID of "UNUSED" placeholder
                }

                spriteInfo.Texture = _atlas.AddSprite(
                    artInfo.Pixels,
                    artInfo.Width,
                    artInfo.Height,
                    out spriteInfo.UV
                );

                if (idx > 0x4000)
                {
                    idx -= 0x4000;
                    _picker.Set(idx, artInfo.Width, artInfo.Height, artInfo.Pixels);

                    var pos1 = 0;
                    int minX = artInfo.Width,
                        minY = artInfo.Height,
                        maxX = 0,
                        maxY = 0;

                    for (int y = 0; y < artInfo.Height; ++y)
                    {
                        for (int x = 0; x < artInfo.Width; ++x)
                        {
                            if (artInfo.Pixels[pos1++] != 0)
                            {
                                minX = Math.Min(minX, x);
                                maxX = Math.Max(maxX, x);
                                minY = Math.Min(minY, y);
                                maxY = Math.Max(maxY, y);
                            }
                        }
                    }

                    _realArtBounds[idx] = new Rectangle(minX, minY, maxX - minX, maxY - minY);
                }
                
            }

            return ref spriteInfo;
        }

        public unsafe IntPtr CreateCursorSurfacePtr(
            int index,
            ushort customHue,
            out int hotX,
            out int hotY,
            float dpiScale
        )
        {
            hotX = hotY = 0;

            var artInfo = _artLoader.GetArt((uint)(index + 0x4000));

            if (artInfo.Pixels.IsEmpty)
            {
                return IntPtr.Zero;
            }

            int srcWidth = artInfo.Width;
            int srcHeight = artInfo.Height;

            // Make a copy of pixels to avoid modifying the original
            var rentedBuffer = ArrayPool<uint>.Shared.Rent(artInfo.Pixels.Length);
            try
            {
                var pixelsCopy = rentedBuffer.AsSpan(0, artInfo.Pixels.Length);
                artInfo.Pixels.CopyTo(pixelsCopy);

                // Process the copy: find hotX/Y and clear marker pixels
                for (int y = 0; y < srcHeight; y++)
                {
                    for (int x = 0; x < srcWidth; x++)
                    {
                        int idx = y * srcWidth + x;
                        uint pixel = pixelsCopy[idx];

                        if (pixel == 0)
                            continue;

                        // Clear black marker pixels
                        if (pixel == 0xFF_00_00_00)
                        {
                            pixelsCopy[idx] = 0;
                            continue;
                        }

                        // Check for green hotspot marker in first row/column
                        if (pixel == 0xFF_00_FF_00)
                        {
                            if (x == 0)
                                hotY = y;
                            if (y == 0)
                                hotX = x;
                            pixelsCopy[idx] = 0;
                            continue;
                        }

                        // Clear edge pixels (first/last row and column)
                        if (x == 0 || y == 0 || x == srcWidth - 1 || y == srcHeight - 1)
                        {
                            pixelsCopy[idx] = 0;
                            continue;
                        }

                        // Apply custom hue if needed
                        if (customHue > 0)
                        {
                            Color c = default;
                            c.PackedValue = pixel;
                            pixelsCopy[idx] = HuesHelper.Color16To32(
                                _huesLoader.GetColor16(
                                    HuesHelper.ColorToHue(c),
                                    customHue
                                )
                            ) | 0xFF_00_00_00;
                        }
                    }
                }

                // Scale hotX/Y by dpiScale
                hotX = (int)(hotX * dpiScale);
                hotY = (int)(hotY * dpiScale);

                // Now create the surface from cleaned pixels
                fixed (uint* ptr = pixelsCopy)
                {
                    SDL.SDL_Surface* surface = (SDL.SDL_Surface*)
                        SDL.SDL_CreateSurfaceFrom(
                            srcWidth,
                            srcHeight,
                            SDL.SDL_PixelFormat.SDL_PIXELFORMAT_ABGR8888,
                            (IntPtr)ptr,
                            4 * srcWidth);

                    if (dpiScale != 1f)
                    {
                        int width = (int)(srcWidth * dpiScale);
                        int height = (int)(srcHeight * dpiScale);

                        SDL.SDL_Surface* newSurface = (SDL.SDL_Surface*)SDL.SDL_ScaleSurface(
                            (nint)surface,
                            width,
                            height,
                            SDL.SDL_ScaleMode.SDL_SCALEMODE_NEAREST);

                        SDL.SDL_DestroySurface((nint)surface);
                        surface = newSurface;
                    }

                    return (IntPtr)surface;
                }
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(rentedBuffer);
            } 
        }

        public Rectangle GetRealArtBounds(uint idx) =>
            idx < 0 || idx >= _realArtBounds.Length
                ? Rectangle.Empty
                : _realArtBounds[idx];

        public bool PixelCheck(uint idx, int x, int y) => _picker.Get(idx, x, y);
    }
}
