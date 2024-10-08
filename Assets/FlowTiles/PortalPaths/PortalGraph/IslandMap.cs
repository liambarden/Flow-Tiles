﻿using FlowTiles.Utils;
using Unity.Collections;
using Unity.Mathematics;

namespace FlowTiles.PortalPaths {
    public struct IslandMap {

        public readonly int Index;
        public readonly CellRect Bounds;

        public UnsafeField<short> Cells;
        public short NumIslands;

        public IslandMap(int index, CellRect boundaries) {
            Index = index;
            Bounds = new CellRect();

            Bounds = boundaries;
            Cells = new UnsafeField<short>(Bounds.SizeCells, Allocator.Persistent, initialiseTo: 0);
            NumIslands = 0;
        }

        public void Dispose() {
            Cells.Dispose();
        }

        public void CalculateIslands(CostMap costs) {
            FloodFillAll(costs);
        }

        public bool Contains(int2 pos) {
            return pos.x >= Bounds.MinCell.x &&
                pos.x <= Bounds.MaxCell.x &&
                pos.y >= Bounds.MinCell.y &&
                pos.y <= Bounds.MaxCell.y;
        }

        public short GetIslandAt(int2 pos) {
            var localPos = pos - Bounds.MinCell;
            return Cells[localPos.x, localPos.y];
        }

        // --------------------------------------------------------------

        private void FloodFillAll(CostMap costs) {
            var cellsInSector = Bounds.SizeCells.x * Bounds.SizeCells.y;

            // Divide into open areas and walls
            for (int x = 0; x < Cells.Size.x; x++) {
                for (var y = 0; y < Cells.Size.y; y++) {
                    Cells[x, y] = 0;

                    var cost = costs.Cells[x, y];
                    var blocked = cost == PathableLevel.WALL_COST;
                    if (blocked) Cells[x, y] = -1;
                }
            }

            // Fill open areas
            NumIslands = 0;
            for (int x = 0; x < Cells.Size.x; x++) {
                for (var y = 0; y < Cells.Size.y; y++) {
                    if (Cells[x, y] == 0) {
                        NumIslands++;
                        FloodFill(costs, new int2(x, y), 0, NumIslands, cellsInSector);
                    }
                }
            }

            // Expand fills into walls
            for (int x = 0; x < Cells.Size.x; x++) {
                for (var y = 0; y < Cells.Size.y; y++) {
                    if (Cells[x, y] == -1) {
                        var c1 = TryGetColor(x - 1, y);
                        var c2 = TryGetColor(x + 1, y);
                        var c3 = TryGetColor(x, y - 1);
                        var c4 = TryGetColor(x, y + 1);
                        var bestNeighbor = math.max(math.max(c1, c2), math.max(c3, c4));
                        if (bestNeighbor > 0) {
                            FloodFill(costs, new int2(x, y), -1, (short)bestNeighbor, cellsInSector);
                        };
                    }
                }
            }
        }

        // Flood fill using the scanline method. Based on...
        // https://simpledevcode.wordpress.com/2015/12/29/flood-fill-algorithm-using-c-net/
        private void FloodFill(CostMap costs, int2 startPoint, short oldColorIndex, short newColorIndex, int cellsInSector) {
            NativeStack<int2> points = new NativeStack<int2>(cellsInSector, Allocator.Temp);
            NativeHashSet<int2> visited = new NativeHashSet<int2>(cellsInSector, Allocator.Temp);

            points.Push(startPoint);
            visited.Add(startPoint);

            while (points.Count != 0) {
                int2 temp = points.Pop();
                int row = temp.y;
                while (row >= 0 && Cells[temp.x, row] == oldColorIndex) {
                    row--;
                }
                row++;
                bool spanLeft = false;
                bool spanRight = false;

                while (row < Cells.Size.y && Cells[temp.x, row] == oldColorIndex) {
                    Cells[temp.x, row] = newColorIndex;

                    if (!spanLeft && temp.x > 0 && Cells[temp.x - 1, row] == oldColorIndex) {
                        var next = new int2(temp.x - 1, row);
                        if (!visited.Contains(next)) {
                            visited.Add(next);
                            points.Push(next);
                        }
                        spanLeft = true;
                    }
                    else if (spanLeft && (temp.x - 1 == 0 || Cells[temp.x - 1, row] != oldColorIndex)) {
                        spanLeft = false;
                    }

                    if (!spanRight && temp.x < Cells.Size.x - 1 && Cells[temp.x + 1, row] == oldColorIndex) {
                        var next = new int2(temp.x + 1, row);
                        if (!visited.Contains(next)) {
                            visited.Add(next);
                            points.Push(next);
                        }
                        spanRight = true;
                    }
                    else if (spanRight && (temp.x < Cells.Size.x - 1 && Cells[temp.x + 1, row] != oldColorIndex)) {
                        spanRight = false;
                    }
                    row++;
                }
            }
        }

        private short TryGetColor(int x, int y) {
            if (x < 0 || y < 0 || x >= Cells.Size.x || y >= Cells.Size.y) {
                return -1;
            }
            return Cells[x, y];
        }

    }

}