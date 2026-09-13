// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game.GameObjects;

namespace ClassicUO.Game.Map
{
    /// <summary>
    /// UNFAIR-only, memory-resident land graphic overrides.
    /// These never modify UOP/MUL files. Removing an override exposes the
    /// normal underlying terrain again.
    /// </summary>
    internal static class UnfairTerrainOverrides
    {
        private readonly struct Key
        {
            public Key(int map, ushort x, ushort y)
            {
                Map = map;
                X = x;
                Y = y;
            }

            public readonly int Map;
            public readonly ushort X;
            public readonly ushort Y;

            public override int GetHashCode() => (Map * 397) ^ (X << 16) ^ Y;

            public override bool Equals(object obj) =>
                obj is Key other && other.Map == Map && other.X == X && other.Y == Y;
        }

        private static readonly Dictionary<Key, ushort> _land = new Dictionary<Key, ushort>();

        public static bool TryGet(int map, ushort x, ushort y, out ushort graphic) =>
            _land.TryGetValue(new Key(map, x, y), out graphic);

        public static void Set(int map, ushort x, ushort y, ushort graphic) =>
            _land[new Key(map, x, y)] = graphic;

        public static void Remove(int map, ushort x, ushort y) =>
            _land.Remove(new Key(map, x, y));

        public static HashSet<int> GetTouchedChunkKeys(int map, int blockYCount)
        {
            var chunks = new HashSet<int>();

            foreach (Key key in _land.Keys)
            {
                if (key.Map == map)
                    chunks.Add((key.X >> 3) * blockYCount + (key.Y >> 3));
            }

            return chunks;
        }

        public static void ClearMap(int map)
        {
            var remove = new List<Key>();

            foreach (Key key in _land.Keys)
            {
                if (key.Map == map)
                    remove.Add(key);
            }

            foreach (Key key in remove)
                _land.Remove(key);
        }

        public static void ReloadChunkPreservingDynamic(World world, int mapIndex, int chunkX, int chunkY)
        {
            if (world?.Map == null || world.Map.Index != mapIndex)
                return;

            Chunk chunk = world.Map.GetChunk2(chunkX, chunkY, false);
            if (chunk == null)
                return;

            var dynamicObjects = new List<GameObject>();

            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    GameObject obj = chunk.GetHeadObject(x, y);

                    while (obj != null)
                    {
                        GameObject next = obj.TNext;

                        if (!(obj is Land) && !(obj is Static))
                        {
                            dynamicObjects.Add(obj);
                            obj.RemoveFromTile();
                        }

                        obj = next;
                    }
                }
            }

            chunk.ClearForReload();
            chunk.Load(mapIndex);

            foreach (GameObject obj in dynamicObjects)
                chunk.AddGameObject(obj, obj.X & 7, obj.Y & 7);
        }
    }
}
