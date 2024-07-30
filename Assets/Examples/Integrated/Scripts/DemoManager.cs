using FlowField;
using FlowTiles.PortalGraphs;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace FlowTiles.Examples {

    public class DemoManager : MonoBehaviour {

        private static float4[] graphColorings = new float4[] { 
            new float4(0.8f, 1f, 0.8f, 1),
            new float4(0.8f, 0.8f, 1f, 1),
            new float4(1f, 1f, 0.8f, 1),
            new float4(0.8f, 1f, 1f, 1),
            new float4(1f, 0.8f, 1f, 1),
            new float4(1f, 0.8f, 0.8f, 1),
        };

        // -----------------------------------------

        public int LevelSize = 1000;
        public int Resolution = 10;
        public bool VisualiseConnections;

        private bool[,] WallMap;
        private NativeArray<bool> WallData;
        private NativeArray<float4> ColorData;
        private NativeArray<float2> FlowData;
        private PortalGraph Graph;


        void Start() {
            WallData = new NativeArray<bool>(LevelSize * LevelSize, Allocator.Persistent);
            ColorData = new NativeArray<float4>(LevelSize * LevelSize, Allocator.Persistent);
            FlowData = new NativeArray<float2>(LevelSize * LevelSize, Allocator.Persistent);

            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            var singleton = em.CreateEntity();
            em.AddComponent<LevelSetup>(singleton);
            em.SetComponentData(singleton, new LevelSetup {
                Size = LevelSize,
                Walls = WallData,
                Colors = ColorData,
                Flows = FlowData,
            });

            var halfViewedSize = (LevelSize - 1) / 2f;
            Camera.main.orthographicSize = LevelSize / 2f * 1.05f + 1;
            Camera.main.transform.position = new Vector3(halfViewedSize, halfViewedSize, -20);

            WallMap = new bool[LevelSize, LevelSize];
            for (int i = 0; i < LevelSize; i++) {
                for (int j = 0; j < LevelSize; j++) {
                    if (i == 0 && j == 0) continue;
                    if (UnityEngine.Random.value < 0.2f) WallMap[i, j] = true;
                }
            }

            for (int i = 0; i < LevelSize; i++) {
                for (int j = 0; j < LevelSize; j++) {
                    var index = i + j * LevelSize;
                    WallData[index] = WallMap[i, j];
                }
            }

            var map = Map.CreateMap(WallMap);
            Graph = new PortalGraph(map, Resolution);

            for (int y = 0; y < LevelSize; y++) {
                for (int x = 0; x < LevelSize; x++) {
                    var sector = Graph.GetSector(x, y);
                    var color = sector.Colors.GetColor(x % Resolution, y % Resolution);
                    var index = x + y * LevelSize;
                    if (color > 0) {
                        ColorData[index] = graphColorings[(color - 1) % graphColorings.Length];
                    }
                }
            }

        }

        void Update() {

            var position = Camera.main.ScreenToWorldPoint(Input.mousePosition); 
            var mouseCell = new int2((int)(position.x + 0.5f), (int)(position.y + 0.5f));
            if (mouseCell.x >= 0 && mouseCell.y >= 0 && mouseCell.x < LevelSize && mouseCell.y < LevelSize) {

                // Update the agent
                var em = World.DefaultGameObjectInjectionWorld.EntityManager;
                var agents = em.CreateEntityQuery(new ComponentType[] { typeof(AgentData) });
                if (agents.TryGetSingletonEntity<AgentData>(out Entity agent)) {
                    em.SetComponentData(agent, new PathfindingData {
                        OriginCell = 0,
                        DestCell = mouseCell
                    });
                }

                // Modify the grid
                if (Input.GetMouseButtonDown(0)) {
                    var cellIndex = mouseCell.x + mouseCell.y * LevelSize;
                    var flip = !WallMap[mouseCell.x, mouseCell.y];
                    WallData[cellIndex] = flip;
                    WallMap[mouseCell.x, mouseCell.y] = flip;
                }

                for (int i = 0; i < LevelSize * LevelSize; i++) {
                    FlowData[i] = 0;
                }

                // Find a path
                var start = new int2(0, 0);
                var dest = mouseCell;
                var path = PortalPathfinder.FindPortalPath(Graph, start, dest);

                if (path.Count > 0) {

                    // Create flow tiles
                    for (int i = 0; i < path.Count; i++) {
                        var pos = path[i];
                        var sector = Graph.sectors[pos.SectorIndex];
                        var corner = sector.Boundaries.Min;
                        Vector2Int[] goals;
                        int2 exitDirection = 0;

                        if (i < path.Count - 1) {
                            var portal = sector.EdgePortals[pos.Cell];
                            var cell = portal.Position.Cell - corner;
                            goals = new Vector2Int[] { new Vector2Int(cell.x, cell.y) };
                            exitDirection = portal.Direction;
                        }
                        else {
                            var goal = pos.Cell - corner;
                            goals = new Vector2Int[] { new Vector2Int(goal.x, goal.y) };
                        }

                        var flow = FlowCalculationController.RequestCalculation(sector.Costs, goals, exitDirection);

                        // Visualise flow data
                        VisualiseFlowField(sector, flow);

                        if (i < path.Count - 1) {
                            DrawPortalLink(
                                new Vector3(path[i].Cell.x, path[i].Cell.y),
                                new Vector3(path[i + 1].Cell.x,
                                path[i + 1].Cell.y));
                        }
                    }

                    DrawPortalLink(new Vector3(start.x, start.y), new Vector3(path[0].Cell.x, path[0].Cell.y));

                }

            }

            // Visualise the graph
            var clusters = Graph.sectors;
            for (int c = 0;  c < clusters.Length; c++) {
                var cluster = clusters[c];
                var nodes = cluster.EdgePortals;

                DrawClusterBoundaries(cluster);
                if (VisualiseConnections) {
                    DrawClusterConnections(nodes);
                }
            }
        }

        private void VisualiseFlowField(Sector sector, FlowFieldTile flowField) {
            for (int x = 0; x < sector.Size.x; x++) {
                for (int y = 0; y < sector.Size.y; y++) {
                    var mapIndex = (x + sector.Boundaries.Min.x) + (y + sector.Boundaries.Min.y) * LevelSize;
                    var flow = flowField.GetFlow(x, y);
                    FlowData[mapIndex] = flow;
                }
            }
        }

        private static void DrawClusterConnections(Dictionary<int2, Portal> nodes) {
            foreach (var node in nodes.Values) {
                for (int e = 0; e < node.Edges.Length; e++) {
                    var edge = node.Edges[e];
                    var pos1 = edge.start.Cell;
                    var pos2 = edge.end.Cell;
                    Debug.DrawLine(
                        new Vector3(pos1.x, pos1.y),
                        new Vector3(pos2.x, pos2.y),
                        Color.red);
                }
            }
        }

        private static void DrawClusterBoundaries(Sector cluster) {
            Debug.DrawLine(
                new Vector3(cluster.Boundaries.Min.x - 0.5f, cluster.Boundaries.Min.y - 0.5f),
                new Vector3(cluster.Boundaries.Max.x + 0.5f, cluster.Boundaries.Min.y - 0.5f),
                Color.blue);
            Debug.DrawLine(
                new Vector3(cluster.Boundaries.Min.x - 0.5f, cluster.Boundaries.Min.y - 0.5f),
                new Vector3(cluster.Boundaries.Min.x - 0.5f, cluster.Boundaries.Max.y + 0.5f),
                Color.blue);
            Debug.DrawLine(
                new Vector3(cluster.Boundaries.Max.x + 0.5f, cluster.Boundaries.Max.y + 0.5f),
                new Vector3(cluster.Boundaries.Max.x + 0.5f, cluster.Boundaries.Min.y - 0.5f),
                Color.blue);
            Debug.DrawLine(
                new Vector3(cluster.Boundaries.Max.x + 0.5f, cluster.Boundaries.Max.y + 0.5f),
                new Vector3(cluster.Boundaries.Min.x - 0.5f, cluster.Boundaries.Max.y + 0.5f),
                Color.blue);
        }

        private static void DrawPortalLink (Vector3 pos1, Vector3 pos2) {
            Debug.DrawLine(pos1, pos2, Color.green);

            Debug.DrawLine(pos2 + new Vector3(-0.4f, -0.4f), pos2 + new Vector3(0.4f, -0.4f), Color.green);
            Debug.DrawLine(pos2 + new Vector3(-0.4f, -0.4f), pos2 + new Vector3(-0.4f, 0.4f), Color.green);
            Debug.DrawLine(pos2 + new Vector3(0.4f, 0.4f), pos2 + new Vector3(0.4f, -0.4f), Color.green);
            Debug.DrawLine(pos2 + new Vector3(0.4f, 0.4f), pos2 + new Vector3(-0.4f, 0.4f), Color.green);
        }

    }

}