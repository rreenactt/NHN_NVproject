using System.Collections.Generic;
using UnityEngine;

namespace NV.Game.UI
{
    /// <summary>
    /// The full-map device's picture: both storeys side by side, drawn from the level grid into a
    /// texture, with a dot for every player.
    ///
    /// It is drawn rather than rendered on purpose. A top-down camera over this level photographs
    /// ceiling tiles, and stripping them would mean a second copy of the whole level on another
    /// layer for one six-second effect. The grid already knows the shape of the place.
    /// </summary>
    public sealed class MatchMapView
    {
        private const int PixelsPerCell = 5;
        private const int FloorGap = 4;

        private Texture2D _texture;
        private Color32[] _base;
        private Color32[] _pixels;
        private int _width, _height, _floorStride;

        public Texture2D Texture => _texture;

        public bool EnsureBuilt(BackroomsMapGenerator map)
        {
            if (map == null || !map.HasGrid) return false;
            if (_texture != null) return true;

            int grid = map.GridSize;
            int floors = map.FloorCount;

            _floorStride = grid * PixelsPerCell + FloorGap;
            _width = _floorStride * floors;
            _height = grid * PixelsPerCell;

            _texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                name = "Facility Schematic",
            };

            _base = new Color32[_width * _height];
            var wall = new Color32(22, 20, 15, 255);
            var open = new Color32(126, 113, 70, 255);

            for (int i = 0; i < _base.Length; i++) _base[i] = wall;

            for (int f = 0; f < floors; f++)
            for (int x = 0; x < grid; x++)
            for (int z = 0; z < grid; z++)
            {
                if (!map.IsStandable(f, x, z)) continue;

                int baseX = f * _floorStride + x * PixelsPerCell;
                int baseY = z * PixelsPerCell;
                for (int ox = 0; ox < PixelsPerCell; ox++)
                for (int oy = 0; oy < PixelsPerCell; oy++)
                {
                    int px = baseX + ox, py = baseY + oy;
                    if (px < _width && py < _height) _base[py * _width + px] = open;
                }
            }

            _pixels = new Color32[_base.Length];
            return true;
        }

        /// <summary>
        /// Repaints the dots. The door is only ever drawn for a Runner — the map device must not
        /// become the way the Seeker finds it.
        /// </summary>
        public void Refresh(MatchManager match, Role viewerRole)
        {
            if (_texture == null || match == null) return;
            BackroomsMapGenerator map = match.Map;
            if (map == null) return;

            System.Array.Copy(_base, _pixels, _base.Length);

            IReadOnlyList<PlayerAgent> agents = match.Agents;
            for (int i = 0; i < agents.Count; i++)
            {
                PlayerAgent agent = agents[i];
                if (agent == null || !agent.InPlay) continue;
                if (!map.TryWorldToCell(agent.FeetPosition, out int floor, out int x, out int z)) continue;

                Color32 colour = agent.Role == Role.Seeker
                    ? new Color32(232, 128, 74, 255)
                    : agent.isLocalPlayer
                        ? new Color32(178, 214, 228, 255)
                        : new Color32(226, 218, 182, 255);

                Blot(floor, x, z, agent.isLocalPlayer ? 3 : 2, colour);
            }

            if (viewerRole == Role.Runner && match.Door != null
                && map.TryWorldToCell(match.Door.Position, out int df, out int dx, out int dz))
            {
                Blot(df, dx, dz, 3, new Color32(150, 226, 160, 255));
            }

            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }

        private void Blot(int floor, int cellX, int cellZ, int radius, Color32 colour)
        {
            int cx = floor * _floorStride + cellX * PixelsPerCell + PixelsPerCell / 2;
            int cy = cellZ * PixelsPerCell + PixelsPerCell / 2;

            for (int x = cx - radius; x <= cx + radius; x++)
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                if (x < 0 || y < 0 || x >= _width || y >= _height) continue;
                _pixels[y * _width + x] = colour;
            }
        }

        public void Release()
        {
            if (_texture != null) Object.Destroy(_texture);
            _texture = null;
            _base = null;
            _pixels = null;
        }
    }
}
