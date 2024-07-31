﻿using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace FlowTiles.Examples {
    [MaterialProperty("_BaseColor")]
    public struct ColorOverride : IComponentData {
        public float4 Value;
    }

}