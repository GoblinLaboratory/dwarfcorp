using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DwarfCorp.GameStates;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading;
using System.Collections.Concurrent;

namespace DwarfCorp
{
    /// <summary>
    /// Represents a collection of voxels with a surface mesh. Efficiently culls away
    /// invisible voxels, and properly constructs ramps.
    /// </summary>
    public class VoxelListPrimitive : GeometricPrimitive
    {
        private static Vector3[] FaceDeltas = null;

        // Describes which top face verts to grab for aligning fringe.
        private static int[,] FringeIndicies = new int[,]
        {
            // First four describe the vertex order
            // Last 2 are the indecies of the verts to use to extend in the fringe direction
            {3, 2, 1, 0, 2, 1}, // North
            {2, 1, 0, 3, 3, 2}, // East
            {1, 0, 3, 2, 0, 3 }, // South
            {0, 3, 2, 1, 1, 0 }, // West

            // First four describe vertex order
            {3,0,1,2,1,3 }, // North West
            {0,3,2,1,2,0 }, // North East
            {1,2,3,0,3,1 }, // South East
            {2,1,0,3,0,2 }, // South West
        };

        private static Vector2[] SideFringeUVScales = new Vector2[]
        {
            new Vector2(1.0f, 0.5f),
            new Vector2(0.5f, 1.0f),
            new Vector2(1.0f, 0.5f),
            new Vector2(0.5f, 1.0f)
        };

        protected void InitializeStatics()
        {
            if (FaceDeltas == null)
            {
                FaceDeltas = new Vector3[6];
                FaceDeltas[(int)BoxFace.Back] = new Vector3(0, 0, 1);
                FaceDeltas[(int)BoxFace.Front] = new Vector3(0, 0, -1);
                FaceDeltas[(int)BoxFace.Left] = new Vector3(-1, 0, 0);
                FaceDeltas[(int)BoxFace.Right] = new Vector3(1, 0, 0);
                FaceDeltas[(int)BoxFace.Top] = new Vector3(0, 1, 0);
                FaceDeltas[(int)BoxFace.Bottom] = new Vector3(0, -1, 0);
            }
        }

        public VoxelListPrimitive() :
            base()
        {
            InitializeStatics();
        }

        public static bool IsTopVertex(VoxelVertex v)
        {
            return v == VoxelVertex.BackTopLeft || v == VoxelVertex.FrontTopLeft || v == VoxelVertex.FrontTopRight || v == VoxelVertex.BackTopRight;
        }

        protected static bool ShouldDrawFace(BoxFace face, RampType neighborRamp, RampType myRamp)
        {
            switch (face)
            {
                case BoxFace.Top:
                case BoxFace.Bottom:
                    return true;
                case BoxFace.Front:
                    return CheckRamps(myRamp, RampType.TopBackLeft, RampType.TopBackRight,
                        neighborRamp, RampType.TopFrontLeft, RampType.TopFrontRight);
                case BoxFace.Back:
                    return CheckRamps(myRamp, RampType.TopFrontLeft, RampType.TopFrontRight,
                        neighborRamp, RampType.TopBackLeft, RampType.TopBackRight);
                case BoxFace.Left:
                    return CheckRamps(myRamp, RampType.TopBackLeft, RampType.TopFrontLeft,
                        neighborRamp, RampType.TopBackRight, RampType.TopFrontRight);
                case BoxFace.Right:
                    return CheckRamps(myRamp, RampType.TopBackRight, RampType.TopFrontRight,
                        neighborRamp, RampType.TopBackLeft, RampType.TopFrontLeft);
                default:
                    return false;
            }
        }

        private static bool RampSet(RampType ToCheck, RampType For)
        {
            return (ToCheck & For) != 0;
        }

        private static bool CheckRamps(RampType A, RampType A1, RampType A2, RampType B, RampType B1, RampType B2)
        {
            return (!RampSet(A, A1) && RampSet(B, B1)) || (!RampSet(A, A2) && RampSet(B, B2));
        }

        protected static bool IsSideFace(BoxFace face)
        {
            return face != BoxFace.Top && face != BoxFace.Bottom;
        }

        protected static bool IsFaceVisible(VoxelHandle voxel, VoxelHandle neighbor, BoxFace face)
        {
            return
                !neighbor.IsValid
                || (neighbor.IsExplored && neighbor.IsEmpty)
                || (neighbor.Type.IsTransparent && !voxel.Type.IsTransparent)
                || !neighbor.IsVisible
                || (
                    neighbor.Type.CanRamp
                    && neighbor.RampType != RampType.None
                    && IsSideFace(face)
                    && ShouldDrawFace(face, neighbor.RampType, voxel.RampType)
                );
        }

        private class Cache
        {
            public int[] AmbientValues = new int[4];
            public Dictionary<GlobalVoxelCoordinate, VertexColorInfo> LightCache = new Dictionary<GlobalVoxelCoordinate, VertexColorInfo>();
            public Dictionary<GlobalVoxelCoordinate, bool> ExploredCache = new Dictionary<GlobalVoxelCoordinate, bool>();

            public void Clear()
            {
                LightCache.Clear();
                ExploredCache.Clear();
            }
        }

        public void InitializeFromChunk(VoxelChunk chunk)
        {
            if (chunk == null)
                return;

            BoxPrimitive bedrockModel = VoxelLibrary.GetPrimitive("Bedrock");
            var sliceStack = new List<RawPrimitive>();
            var cache = new Cache();

            for (var y = 0; y < chunk.Manager.World.Master.MaxViewingLevel; ++y)
            {
                RawPrimitive sliceGeometry = null;

                lock (chunk.Data.SliceCache)
                {
                    var cachedSlice = chunk.Data.SliceCache[y];

                    if (chunk.Data.VoxelsPresentInSlice[y] == 0)
                    {
                        cache.Clear();

                        if (cachedSlice != null)
                        {
                            chunk.Data.SliceCache[y] = null;
                        }
                        continue;
                    }

                    if (cachedSlice != null)
                    {
                        cache.Clear();

                        sliceStack.Add(cachedSlice);

                        if (GameSettings.Default.GrassMotes)
                            chunk.RebuildMoteLayerIfNull(y);

                        continue;
                    }

                    sliceGeometry = new RawPrimitive
                    {
                        Vertices = new ExtendedVertex[128],
                        Indexes = new short[128]
                    };

                    chunk.Data.SliceCache[y] = sliceGeometry;
                }

                if (GameSettings.Default.CalculateRamps)
                {
                    UpdateCornerRamps(chunk, y);
                    UpdateNeighborEdgeRamps(chunk, y);
                }

                if (GameSettings.Default.GrassMotes)
                    chunk.RebuildMoteLayer(y);

                BuildSliceGeometry(chunk, bedrockModel, cache, y, sliceGeometry);

                sliceStack.Add(sliceGeometry);
            }

            var combinedGeometry = RawPrimitive.Concat(sliceStack);

                Vertices = combinedGeometry.Vertices;
                VertexCount = combinedGeometry.VertexCount;
                Indexes = combinedGeometry.Indexes.Select(s => (ushort)s).ToArray();
                IndexCount = combinedGeometry.IndexCount;

                chunk.PrimitiveMutex.WaitOne();
                chunk.NewPrimitive = this;
                chunk.NewPrimitiveReceived = true;
                chunk.PrimitiveMutex.ReleaseMutex();
        }

        private static void BuildSliceGeometry(
            VoxelChunk chunk, 
            BoxPrimitive bedrockModel, 
            Cache Cache,
            int y, 
            RawPrimitive sliceGeometry)
        {
            for (var x = 0; x < VoxelConstants.ChunkSizeX; ++x)
            {
                for (var z = 0; z < VoxelConstants.ChunkSizeZ; ++z)
                {
                    BuildVoxelGeometry(sliceGeometry,
                        x, y, z, chunk, bedrockModel, Cache);
                }
            }
        }

        private static GlobalVoxelCoordinate GetCacheKey(VoxelHandle Handle, VoxelVertex Vertex)
        {
            var coord = Handle.Coordinate;

            if ((Vertex & VoxelVertex.Front) == VoxelVertex.Front)
                coord = new GlobalVoxelCoordinate(coord.X, coord.Y, coord.Z + 1);

            if ((Vertex & VoxelVertex.Top) == VoxelVertex.Top)
                coord = new GlobalVoxelCoordinate(coord.X, coord.Y + 1, coord.Z);

            if ((Vertex & VoxelVertex.Right) == VoxelVertex.Right)
                coord = new GlobalVoxelCoordinate(coord.X + 1, coord.Y, coord.Z);

            return coord;
        }

        private static void BuildVoxelGeometry(
            RawPrimitive Into,
            int X,
            int Y,
            int Z,
            VoxelChunk Chunk,
            BoxPrimitive BedrockModel,
            Cache Cache)
        {
            var v = new VoxelHandle(Chunk, new LocalVoxelCoordinate(X, Y, Z));

            if ((v.IsExplored && v.IsEmpty) || !v.IsVisible) return;

            var primitive = VoxelLibrary.GetPrimitive(v.Type);
            if (v.IsExplored && primitive == null) return;

            if (primitive == null) primitive = BedrockModel;

            var tint = v.Type.Tint;

            var uvs = primitive.UVs;

            if (v.Type.HasTransitionTextures && v.IsExplored)
                uvs = ComputeTransitionTexture(new VoxelHandle(v.Chunk.Manager.ChunkData, v.Coordinate));

            BuildVoxelTopFaceGeometry(Into,
                Chunk, Cache, primitive, v, uvs, 0);
            for (int i = 1; i < 6; i++)
                BuildVoxelFaceGeometry(Into, Chunk,
                    Cache, primitive, v, tint, uvs, i);
        }

        private static void BuildVoxelFaceGeometry(
            RawPrimitive Into,
            VoxelChunk Chunk,
            Cache Cache,
            BoxPrimitive Primitive,
            VoxelHandle V,
            Color Tint,
            BoxPrimitive.BoxTextureCoords UVs,
            int i)
        {
            var face = (BoxFace)i;
            var delta = FaceDeltas[i];

            var faceVoxel = new VoxelHandle(Chunk.Manager.ChunkData,
                V.Coordinate + GlobalVoxelOffset.FromVector3(delta));

            if (!IsFaceVisible(V, faceVoxel, face))
                return;

            var faceDescriptor = Primitive.GetFace(face);
            var indexOffset = Into.VertexCount;

            for (int faceVertex = 0; faceVertex < faceDescriptor.VertexCount; faceVertex++)
            {
                var vertex = Primitive.Vertices[faceDescriptor.VertexOffset + faceVertex];
                var voxelVertex = Primitive.Deltas[faceDescriptor.VertexOffset + faceVertex];

                var cacheKey = GetCacheKey(V, voxelVertex);

                VertexColorInfo vertexColor;
                if (!Cache.LightCache.TryGetValue(cacheKey, out vertexColor))
                {
                    vertexColor = CalculateVertexLight(V, voxelVertex, Chunk.Manager);
                    Cache.LightCache.Add(cacheKey, vertexColor);
                }

                Cache.AmbientValues[faceVertex] = vertexColor.AmbientColor;

                var rampOffset = Vector3.Zero;
                if (V.Type.CanRamp && ShouldRamp(voxelVertex, V.RampType))
                    rampOffset = new Vector3(0, -V.Type.RampSize, 0);


                var worldPosition = V.WorldPosition + vertex.Position + rampOffset;

                Into.AddVertex(new ExtendedVertex(
                    worldPosition + VertexNoise.GetNoiseVectorFromRepeatingTexture(worldPosition),
                    vertexColor.AsColor(),
                    Tint,
                    UVs.Uvs[faceDescriptor.VertexOffset + faceVertex],
                    UVs.Bounds[faceDescriptor.IndexOffset / 6]));
            }

            bool flippedQuad = Cache.AmbientValues[0] + Cache.AmbientValues[2] >
                              Cache.AmbientValues[1] + Cache.AmbientValues[3];

            for (int idx = faceDescriptor.IndexOffset; idx < faceDescriptor.IndexCount +
                faceDescriptor.IndexOffset; idx++)
            {
                ushort offset = flippedQuad ? Primitive.FlippedIndexes[idx] : Primitive.Indexes[idx];
                ushort offset0 = flippedQuad ? Primitive.FlippedIndexes[faceDescriptor.IndexOffset] : Primitive.Indexes[faceDescriptor.IndexOffset];
                Into.AddIndex((short)(indexOffset + offset - offset0));
            }
        }

        private static void BuildVoxelTopFaceGeometry(
            RawPrimitive Into,
            VoxelChunk Chunk,
            Cache Cache,
            BoxPrimitive Primitive,
            VoxelHandle V,
            BoxPrimitive.BoxTextureCoords UVs,
            int i)
        {
            var face = (BoxFace)i;
            var delta = FaceDeltas[i];

            var faceVoxel = new VoxelHandle(Chunk.Manager.ChunkData,
                V.Coordinate + GlobalVoxelOffset.FromVector3(delta));

            if (!IsFaceVisible(V, faceVoxel, face))
                return;

            var faceDescriptor = Primitive.GetFace(face);
            int exploredVerts = 0;
            var vertexColors = new VertexColorInfo[4];
            var vertexTint = new Color[4];

            // Find all verticies to use for geometry later, and for the fringe
            var vertexPositions = new Vector3[4];

            for (int faceVertex = 0; faceVertex < faceDescriptor.VertexCount; faceVertex++)
            {
                var vertex = Primitive.Vertices[faceDescriptor.VertexOffset + faceVertex];
                var voxelVertex = Primitive.Deltas[faceDescriptor.VertexOffset + faceVertex];

                var rampOffset = Vector3.Zero;
                if (V.Type.CanRamp && ShouldRamp(voxelVertex, V.RampType))
                    rampOffset = new Vector3(0, -V.Type.RampSize, 0);

                var worldPosition = V.WorldPosition + vertex.Position + rampOffset;
                //worldPosition += VertexNoise.GetNoiseVectorFromRepeatingTexture(worldPosition);

                vertexPositions[faceVertex] = worldPosition;
            }

            if (V.IsExplored)
                exploredVerts = 4;
            else
            {
                for (int faceVertex = 0; faceVertex < faceDescriptor.VertexCount; ++faceVertex)
                {
                    var voxelVertex = Primitive.Deltas[faceDescriptor.VertexOffset + faceVertex];
                    var cacheKey = GetCacheKey(V, voxelVertex);
                    bool anyNeighborExplored = true;

                    if (!Cache.ExploredCache.TryGetValue(cacheKey, out anyNeighborExplored))
                    {
                        anyNeighborExplored = VoxelHelpers.EnumerateVertexNeighbors2D(V.Coordinate, voxelVertex)
                            .Select(c => new VoxelHandle(V.Chunk.Manager.ChunkData, c))
                            .Any(n => n.IsValid && n.IsExplored);
                        Cache.ExploredCache.Add(cacheKey, anyNeighborExplored);
                    }


                    if (anyNeighborExplored)
                        exploredVerts += 1;
                }

            }

            for (int faceVertex = 0; faceVertex < faceDescriptor.VertexCount; ++faceVertex)
            {
                var voxelVertex = Primitive.Deltas[faceDescriptor.VertexOffset + faceVertex];
                var cacheKey = GetCacheKey(V, voxelVertex);

                VertexColorInfo vertexColor;
                if (!Cache.LightCache.TryGetValue(cacheKey, out vertexColor))
                {
                    vertexColor = CalculateVertexLight(V, voxelVertex, Chunk.Manager);
                    Cache.LightCache.Add(cacheKey, vertexColor);
                }

                vertexColors[faceVertex] = vertexColor;

                vertexTint[faceVertex] = new Color(1.0f, 1.0f, 1.0f, 1.0f);
                if (exploredVerts != 4)
                {
                    bool anyNeighborExplored = true;
                    if (!Cache.ExploredCache.TryGetValue(cacheKey, out anyNeighborExplored))
                        throw new InvalidProgramException();

                    if (!anyNeighborExplored) vertexTint[faceVertex] = new Color(0.0f, 0.0f, 0.0f, 1.0f);
                }
                
                vertexTint[faceVertex] = new Color(vertexTint[faceVertex].ToVector4() * V.Type.Tint.ToVector4());
            }

            if (exploredVerts != 0)
            {
                var baseUVs = UVs.Uvs[11]; // EW

                var baseUVBounds = new Vector4(baseUVs.X + 0.001f, baseUVs.Y + 0.001f, baseUVs.X + (1.0f / 16.0f) - 0.001f, baseUVs.Y + (1.0f / 16.0f) - 0.001f);

                // Draw central top tile.
                AddTopFaceGeometry(Into,
                    Cache.AmbientValues, Primitive,
                    faceDescriptor,
                    vertexPositions,
                    vertexColors,
                    vertexTint,
                    Vector2.One,
                    baseUVs, baseUVBounds);

                if (V.GrassType != 0)
                    BuildGrassFringeGeometry(Into, Chunk, Cache, Primitive, V, vertexColors,
                        vertexTint, vertexPositions, faceDescriptor, exploredVerts);

                if (V.Decal != 0)
                {
                    var decalTypeID = DecalType.DecodeDecalType(V.Decal);
                    var decalOrientation = DecalType.DecodeDecalOrientation(V.Decal);
                    var decalType = DecalLibrary.GetGrassType(decalTypeID);

                    AddDecalGeometry(Into, Cache.AmbientValues, Primitive, V, faceDescriptor, exploredVerts, vertexPositions, vertexColors, vertexTint, decalType, (byte)decalOrientation);
                }
            }
            else
            {
                var indexOffset = Into.VertexCount;

                for (int faceVertex = 0; faceVertex < faceDescriptor.VertexCount; faceVertex++)
                {
                    Into.AddVertex(new ExtendedVertex(
                        vertexPositions[faceVertex] + VertexNoise.GetNoiseVectorFromRepeatingTexture(vertexPositions[faceVertex]),
                        new Color(0, 0, 0, 255),
                        new Color(0, 0, 0, 255),
                        new Vector2(12.5f / 16.0f, 0.5f / 16.0f),
                        new Vector4(12.0f / 16.0f, 0.0f, 13.0f / 16.0f, 1.0f / 16.0f)));
                }

                for (int idx = faceDescriptor.IndexOffset; idx < faceDescriptor.IndexCount +
                    faceDescriptor.IndexOffset; idx++)
                {
                    ushort offset = Primitive.Indexes[idx];
                    ushort offset0 = Primitive.Indexes[faceDescriptor.IndexOffset];
                    Into.AddIndex((short)(indexOffset + offset - offset0));
                }
            }
        }

        private static void BuildGrassFringeGeometry(
            RawPrimitive Into,
            VoxelChunk Chunk,
            Cache Cache,
            BoxPrimitive Primitive,
            VoxelHandle V,
            VertexColorInfo[] VertexColors,
            Color[] VertexTint,
            Vector3[] VertexPositions,
            BoxPrimitive.FaceDescriptor Face,
            int ExploredVerts)
        {
            if (V.GrassType == 0) return;

            var decalType = GrassLibrary.GetGrassType(V.GrassType);

            AddGrassGeometry(Into, Cache.AmbientValues, Primitive, V, Face, ExploredVerts, VertexPositions, VertexColors, VertexTint, decalType);

            // Draw fringe
            if (decalType.FringeTransitionUVs == null) return;

            for (var s = 0; s < 4; ++s)
            {
                var neighborCoord = V.Coordinate + VoxelHelpers.ManhattanNeighbors2D[s];
                var neighbor = new VoxelHandle(Chunk.Manager.ChunkData, neighborCoord);

                if (!neighbor.IsValid) continue;

                var aboveNeighbor = new VoxelHandle(Chunk.Manager.ChunkData, neighborCoord + new GlobalVoxelOffset(0, 1, 0));

                if (!aboveNeighbor.IsValid || aboveNeighbor.IsEmpty)
                {
                    // Draw horizontal fringe.
                    if (!neighbor.IsEmpty)
                    {
                        if (neighbor.GrassType != 0 &&
                            GrassLibrary.GetGrassType(neighbor.GrassType).FringePrecedence >= decalType.FringePrecedence)
                            continue;
                    }

                    // Twizzle vertex positions.
                    var newPositions = new Vector3[4];
                    newPositions[FringeIndicies[s, 0]] = VertexPositions[FringeIndicies[s, 4]];
                    newPositions[FringeIndicies[s, 1]] = VertexPositions[FringeIndicies[s, 4]]
                        + (VoxelHelpers.ManhattanNeighbors2D[s].AsVector3() * 0.5f);
                    newPositions[FringeIndicies[s, 2]] = VertexPositions[FringeIndicies[s, 5]]
                        + (VoxelHelpers.ManhattanNeighbors2D[s].AsVector3() * 0.5f);
                    newPositions[FringeIndicies[s, 3]] = VertexPositions[FringeIndicies[s, 5]];

                    var newColors = new VertexColorInfo[4];
                    newColors[FringeIndicies[s, 0]] = VertexColors[FringeIndicies[s, 4]];
                    newColors[FringeIndicies[s, 1]] = VertexColors[FringeIndicies[s, 4]];
                    newColors[FringeIndicies[s, 2]] = VertexColors[FringeIndicies[s, 5]];
                    newColors[FringeIndicies[s, 3]] = VertexColors[FringeIndicies[s, 5]];

                    var slopeTweak = new Vector3(0.0f, 0.0f, 0.0f);
                    if (neighbor.IsEmpty)
                        slopeTweak.Y = -0.5f;
                    else
                        slopeTweak.Y = 0.125f;

                    newPositions[FringeIndicies[s, 1]] += slopeTweak;
                    newPositions[FringeIndicies[s, 2]] += slopeTweak;

                    var newTints = new Color[4];
                    newTints[FringeIndicies[s, 0]] = VertexTint[FringeIndicies[s, 4]];
                    newTints[FringeIndicies[s, 1]] = VertexTint[FringeIndicies[s, 4]];
                    newTints[FringeIndicies[s, 2]] = VertexTint[FringeIndicies[s, 5]];
                    newTints[FringeIndicies[s, 3]] = VertexTint[FringeIndicies[s, 5]];

                    AddTopFaceGeometry(Into,
                        Cache.AmbientValues, Primitive,
                        Face,
                        newPositions,
                        newColors,
                        newTints,
                        SideFringeUVScales[s],
                        decalType.FringeTransitionUVs[s].UV,
                        decalType.FringeTransitionUVs[s].Bounds);
                }
                else
                {
                    // Draw vertical fringe!

                    var newPositions = new Vector3[4];
                    newPositions[FringeIndicies[s, 0]] = VertexPositions[FringeIndicies[s, 4]];
                    newPositions[FringeIndicies[s, 1]] = VertexPositions[FringeIndicies[s, 4]]
                        + new Vector3(0.0f, 0.5f, 0.0f)
                        + (VoxelHelpers.ManhattanNeighbors2D[s].AsVector3() * -0.05f);
                    newPositions[FringeIndicies[s, 2]] = VertexPositions[FringeIndicies[s, 5]]
                        + new Vector3(0.0f, 0.5f, 0.0f)
                        + (VoxelHelpers.ManhattanNeighbors2D[s].AsVector3() * -0.05f);
                    newPositions[FringeIndicies[s, 3]] = VertexPositions[FringeIndicies[s, 5]];

                    var newColors = new VertexColorInfo[4];
                    newColors[FringeIndicies[s, 0]] = VertexColors[FringeIndicies[s, 4]];
                    newColors[FringeIndicies[s, 1]] = VertexColors[FringeIndicies[s, 4]];
                    newColors[FringeIndicies[s, 2]] = VertexColors[FringeIndicies[s, 5]];
                    newColors[FringeIndicies[s, 3]] = VertexColors[FringeIndicies[s, 5]];

                    var newTints = new Color[4];
                    newTints[FringeIndicies[s, 0]] = VertexTint[FringeIndicies[s, 4]];
                    newTints[FringeIndicies[s, 1]] = VertexTint[FringeIndicies[s, 4]];
                    newTints[FringeIndicies[s, 2]] = VertexTint[FringeIndicies[s, 5]];
                    newTints[FringeIndicies[s, 3]] = VertexTint[FringeIndicies[s, 5]];

                    AddTopFaceGeometry(Into,
                        Cache.AmbientValues, Primitive,
                        Face,
                        newPositions,
                        newColors,
                        newTints,
                        SideFringeUVScales[s],
                        decalType.FringeTransitionUVs[s].UV,
                        decalType.FringeTransitionUVs[s].Bounds);
                }
            }

            for (var s = 0; s < 4; ++s)
            {
                var neighborCoord = V.Coordinate + VoxelHelpers.DiagonalNeighbors2D[s];
                var handle = new VoxelHandle(Chunk.Manager.ChunkData, neighborCoord);

                if (handle.IsValid)
                {
                    if (!handle.IsEmpty)
                    {
                        if (handle.GrassType != 0 &&
                            GrassLibrary.GetGrassType(handle.GrassType).FringePrecedence >= decalType.FringePrecedence)
                            continue;
                    }

                    var manhattanA = new VoxelHandle(Chunk.Manager.ChunkData,
                        V.Coordinate + VoxelHelpers.ManhattanNeighbors2D[s]);
                    if (!manhattanA.IsValid || (manhattanA.GrassType == V.GrassType))
                        continue;

                    manhattanA = new VoxelHandle(Chunk.Manager.ChunkData,
                        V.Coordinate + VoxelHelpers.ManhattanNeighbors2D[FringeIndicies[4 + s, 5]]);
                    if (!manhattanA.IsValid || (manhattanA.GrassType == V.GrassType))
                        continue;

                    // Twizzle vertex positions.
                    var newPositions = new Vector3[4];
                    var pivot = VertexPositions[FringeIndicies[4 + s, 4]];
                    var nDelta = VoxelHelpers.DiagonalNeighbors2D[s].AsVector3();

                    newPositions[FringeIndicies[4 + s, 0]] = pivot;
                    newPositions[FringeIndicies[4 + s, 1]] = pivot + new Vector3(nDelta.X * 0.5f, 0, 0);
                    newPositions[FringeIndicies[4 + s, 2]] = pivot + new Vector3(nDelta.X * 0.5f, 0, nDelta.Z * 0.5f);
                    newPositions[FringeIndicies[4 + s, 3]] = pivot + new Vector3(0, 0, nDelta.Z * 0.5f);

                    var slopeTweak = new Vector3(0.0f, 0.0f, 0.0f);
                    if (handle.IsEmpty)
                        slopeTweak.Y = -0.5f;
                    else
                        slopeTweak.Y = 0.125f;

                    newPositions[FringeIndicies[4 + s, 1]] += slopeTweak;
                    newPositions[FringeIndicies[4 + s, 2]] += slopeTweak;
                    newPositions[FringeIndicies[4 + s, 3]] += slopeTweak;

                    var newColors = new VertexColorInfo[4];
                    newColors[FringeIndicies[4 + s, 0]] = VertexColors[FringeIndicies[4 + s, 4]];
                    newColors[FringeIndicies[4 + s, 1]] = VertexColors[FringeIndicies[4 + s, 4]];
                    newColors[FringeIndicies[4 + s, 2]] = VertexColors[FringeIndicies[4 + s, 4]];
                    newColors[FringeIndicies[4 + s, 3]] = VertexColors[FringeIndicies[4 + s, 4]];

                    var newTints = new Color[4];
                    newTints[FringeIndicies[4 + s, 0]] = VertexTint[FringeIndicies[4 + s, 4]];
                    newTints[FringeIndicies[4 + s, 1]] = VertexTint[FringeIndicies[4 + s, 4]];
                    newTints[FringeIndicies[4 + s, 2]] = VertexTint[FringeIndicies[4 + s, 4]];
                    newTints[FringeIndicies[4 + s, 3]] = VertexTint[FringeIndicies[4 + s, 4]];

                    AddTopFaceGeometry(Into,
                        Cache.AmbientValues, Primitive,
                        Face,
                        newPositions,
                        newColors,
                        newTints,
                        new Vector2(0.5f, 0.5f),
                        decalType.FringeTransitionUVs[4 + s].UV,
                        decalType.FringeTransitionUVs[4 + s].Bounds);
                }
            }
        }

        private static void AddTopFaceGeometry(
            RawPrimitive Into,
            int[] AmbientScratchSpace,
            BoxPrimitive Primitive,
            BoxPrimitive.FaceDescriptor faceDescriptor,
            Vector3[] VertexPositions,
            VertexColorInfo[] VertexColors,
            Color[] VertexTints,
            Vector2 UVScale,
            Vector2 UV,
            Vector4 UVBounds)
        {
            var indexOffset = Into.VertexCount;

            for (int faceVertex = 0; faceVertex < faceDescriptor.VertexCount; faceVertex++)
            {
                var vertex = Primitive.Vertices[faceDescriptor.VertexOffset + faceVertex];
               
                AmbientScratchSpace[faceVertex] = VertexColors[faceVertex].AmbientColor;

                Into.AddVertex(new ExtendedVertex(
                    VertexPositions[faceVertex] + VertexNoise.GetNoiseVectorFromRepeatingTexture(VertexPositions[faceVertex]),
                    VertexColors[faceVertex].AsColor(),
                    VertexTints[faceVertex],
                    UV + new Vector2(vertex.Position.X / 16.0f * UVScale.X, vertex.Position.Z / 16.0f * UVScale.Y),
                    UVBounds));
            }

            bool flippedQuad = AmbientScratchSpace[0] + AmbientScratchSpace[2] >
                              AmbientScratchSpace[1] + AmbientScratchSpace[3];

            for (int idx = faceDescriptor.IndexOffset; idx < faceDescriptor.IndexCount +
                faceDescriptor.IndexOffset; idx++)
            {
                ushort offset = flippedQuad ? Primitive.FlippedIndexes[idx] : Primitive.Indexes[idx];
                ushort offset0 = flippedQuad ? Primitive.FlippedIndexes[faceDescriptor.IndexOffset] : Primitive.Indexes[faceDescriptor.IndexOffset];
                Into.AddIndex((short)(indexOffset + offset - offset0));
            }
        }

        private static void AddGrassGeometry(
            RawPrimitive Into,
            int[] AmbientScratchSpace,
            BoxPrimitive Primitive,
            VoxelHandle V,
            BoxPrimitive.FaceDescriptor faceDescriptor,
            int exploredVerts,
            Vector3[] VertexPositions,
            VertexColorInfo[] VertexColors,
            Color[] VertexTints,
            GrassType Decal)
        {
            var indexOffset = Into.VertexCount;
            var UV = new Vector2(Decal.Tile.X * (1.0f / 16.0f), Decal.Tile.Y * (1.0f / 16.0f));
            var UVBounds = new Vector4(UV.X + 0.001f, UV.Y + 0.001f, UV.X + (1.0f / 16.0f) - 0.001f, UV.Y + (1.0f / 16.0f) - 0.001f);

            for (int faceVertex = 0; faceVertex < faceDescriptor.VertexCount; faceVertex++)
            {
                var vertex = Primitive.Vertices[faceDescriptor.VertexOffset + faceVertex];

                AmbientScratchSpace[faceVertex] = VertexColors[faceVertex].AmbientColor;

                Into.AddVertex(new ExtendedVertex(
                    VertexPositions[faceVertex] + VertexNoise.GetNoiseVectorFromRepeatingTexture(VertexPositions[faceVertex]),
                    VertexColors[faceVertex].AsColor(),
                    VertexTints[faceVertex],
                    UV + new Vector2(vertex.Position.X / 16.0f, vertex.Position.Z / 16.0f),
                    UVBounds));
            }

            bool flippedQuad = AmbientScratchSpace[0] + AmbientScratchSpace[2] >
                              AmbientScratchSpace[1] + AmbientScratchSpace[3];

            for (int idx = faceDescriptor.IndexOffset; idx < faceDescriptor.IndexCount +
                faceDescriptor.IndexOffset; idx++)
            {
                ushort offset = flippedQuad ? Primitive.FlippedIndexes[idx] : Primitive.Indexes[idx];
                ushort offset0 = flippedQuad ? Primitive.FlippedIndexes[faceDescriptor.IndexOffset] : Primitive.Indexes[faceDescriptor.IndexOffset];
                Into.AddIndex((short)(indexOffset + offset - offset0));
            }
        }

        private static void AddDecalGeometry(
            RawPrimitive Into,
            int[] AmbientScratchSpace,
            BoxPrimitive Primitive,
            VoxelHandle V,
            BoxPrimitive.FaceDescriptor faceDescriptor,
            int exploredVerts,
            Vector3[] VertexPositions,
            VertexColorInfo[] VertexColors,
            Color[] VertexTints,
            DecalType Decal,
            byte Orientation)
        {
            var indexOffset = Into.VertexCount;
            var UV = new Vector2(Decal.Tile.X * (1.0f / 16.0f), Decal.Tile.Y * (1.0f / 16.0f));
            var UVBounds = new Vector4(UV.X + 0.001f, UV.Y + 0.001f, UV.X + (1.0f / 16.0f) - 0.001f, UV.Y + (1.0f / 16.0f) - 0.001f);
            var UVs = new Vector2[faceDescriptor.VertexCount];

            for (int faceVertex = 0; faceVertex < faceDescriptor.VertexCount; faceVertex++)
            {
                var vertex = Primitive.Vertices[faceDescriptor.VertexOffset + faceVertex];
                UVs[faceVertex] = UV + new Vector2(vertex.Position.X / 16.0f, vertex.Position.Z / 16.0f);
            }

            for (int faceVertex = 0; faceVertex < faceDescriptor.VertexCount; faceVertex++)
            {
                var vertex = Primitive.Vertices[faceDescriptor.VertexOffset + faceVertex];

                AmbientScratchSpace[faceVertex] = VertexColors[faceVertex].AmbientColor;

                var uv = faceVertex + Orientation;
                uv %= faceDescriptor.VertexCount; // This will only work if the face has 4 verts...

                Into.AddVertex(new ExtendedVertex(
                    VertexPositions[faceVertex] + VertexNoise.GetNoiseVectorFromRepeatingTexture(VertexPositions[faceVertex]),
                    VertexColors[faceVertex].AsColor(),
                    VertexTints[faceVertex],
                    UVs[uv],
                    UVBounds));
            }

            bool flippedQuad = AmbientScratchSpace[0] + AmbientScratchSpace[2] >
                              AmbientScratchSpace[1] + AmbientScratchSpace[3];

            for (int idx = faceDescriptor.IndexOffset; idx < faceDescriptor.IndexCount +
                faceDescriptor.IndexOffset; idx++)
            {
                ushort offset = flippedQuad ? Primitive.FlippedIndexes[idx] : Primitive.Indexes[idx];
                ushort offset0 = flippedQuad ? Primitive.FlippedIndexes[faceDescriptor.IndexOffset] : Primitive.Indexes[faceDescriptor.IndexOffset];
                Into.AddIndex((short)(indexOffset + offset - offset0));
            }
        }



        public static bool ShouldRamp(VoxelVertex vertex, RampType rampType)
        {
            bool toReturn = false;

            if ((rampType & RampType.TopFrontRight) == RampType.TopFrontRight)
                toReturn = (vertex == VoxelVertex.FrontTopRight);

            if ((rampType & RampType.TopBackRight) == RampType.TopBackRight)
                toReturn = toReturn || (vertex == VoxelVertex.BackTopRight);

            if ((rampType & RampType.TopFrontLeft) == RampType.TopFrontLeft)
                toReturn = toReturn || (vertex == VoxelVertex.FrontTopLeft);

            if ((rampType & RampType.TopBackLeft) == RampType.TopBackLeft)
                toReturn = toReturn || (vertex == VoxelVertex.BackTopLeft);

            return toReturn;
        }

        private static VoxelVertex[] TopVerticies = new VoxelVertex[]
            {
                VoxelVertex.FrontTopLeft,
                VoxelVertex.FrontTopRight,
                VoxelVertex.BackTopLeft,
                VoxelVertex.BackTopRight
            };
        
        private static void UpdateVoxelRamps(VoxelHandle V)
        {
            if (V.IsEmpty || !V.IsVisible || !V.Type.CanRamp)
            {
                V.RampType = RampType.None;
                return;
            }

            if (V.Coordinate.Y < VoxelConstants.ChunkSizeY - 1)
            {
                var lCoord = V.Coordinate.GetLocalVoxelCoordinate();
                var vAbove = new VoxelHandle(V.Chunk, new LocalVoxelCoordinate(lCoord.X, lCoord.Y + 1, lCoord.Z));
                if (!vAbove.IsEmpty)
                {
                    V.RampType = RampType.None;
                    return;
                }
            }

            var compositeRamp = RampType.None;

            foreach (var vertex in TopVerticies)
            {
                // If there are no empty neighbors, no slope.
                if (!VoxelHelpers.EnumerateVertexNeighbors2D(V.Coordinate, vertex)
                    .Any(n =>
                    {
                        var handle = new VoxelHandle(V.Chunk.Manager.ChunkData, n);
                        return !handle.IsValid || handle.IsEmpty;
                    }))
                    continue;

                switch (vertex)
                {
                    case VoxelVertex.FrontTopLeft:
                        compositeRamp |= RampType.TopFrontLeft;
                        break;
                    case VoxelVertex.FrontTopRight:
                        compositeRamp |= RampType.TopFrontRight;
                        break;
                    case VoxelVertex.BackTopLeft:
                        compositeRamp |= RampType.TopBackLeft;
                        break;
                    case VoxelVertex.BackTopRight:
                        compositeRamp |= RampType.TopBackRight;
                        break;
                }
            }

            V.RampType = compositeRamp;
        }

        public static void UpdateCornerRamps(VoxelChunk Chunk, int Y)
        {
            for (int x = 0; x < VoxelConstants.ChunkSizeX; x++)
                for (int z = 0; z < VoxelConstants.ChunkSizeZ; z++)
                    UpdateVoxelRamps(new VoxelHandle(Chunk, new LocalVoxelCoordinate(x, Y, z)));               
        }

        private static void UpdateNeighborEdgeRamps(VoxelChunk Chunk, int Y)
        {
            var startChunkCorner = new GlobalVoxelCoordinate(Chunk.ID, new LocalVoxelCoordinate(0, 0, 0))
                + new GlobalVoxelOffset(-1, 0, -1);
            var endChunkCorner = new GlobalVoxelCoordinate(Chunk.ID, new LocalVoxelCoordinate(0, 0, 0))
                + new GlobalVoxelOffset(VoxelConstants.ChunkSizeX, 0, VoxelConstants.ChunkSizeZ);

            for (int x = startChunkCorner.X; x <= endChunkCorner.X; ++x)
            {
                var v1 = new VoxelHandle(Chunk.Manager.ChunkData,
                    new GlobalVoxelCoordinate(x, Y, startChunkCorner.Z));
                if (v1.IsValid) UpdateVoxelRamps(v1);

                var v2 = new VoxelHandle(Chunk.Manager.ChunkData,
                    new GlobalVoxelCoordinate(x, Y, endChunkCorner.Z));
                if (v2.IsValid) UpdateVoxelRamps(v2);
            }

            for (int z = startChunkCorner.Z + 1; z < endChunkCorner.Z; ++z)
            {
                var v1 = new VoxelHandle(Chunk.Manager.ChunkData,
                    new GlobalVoxelCoordinate(startChunkCorner.X, Y, z));
                if (v1.IsValid) UpdateVoxelRamps(v1);

                var v2 = new VoxelHandle(Chunk.Manager.ChunkData,
                    new GlobalVoxelCoordinate(endChunkCorner.X, Y, z));
                if (v2.IsValid) UpdateVoxelRamps(v2);
            }
        }

        private struct VertexColorInfo
        {
            public int SunColor;
            public int AmbientColor;
            public int DynamicColor;

            public Color AsColor()
            {
                return new Color(SunColor, AmbientColor, DynamicColor);
            }
        }

        private static VertexColorInfo CalculateVertexLight(VoxelHandle Vox, VoxelVertex Vertex,
            ChunkManager chunks)
        {
            int neighborsEmpty = 0;
            int neighborsChecked = 0;

            var color = new VertexColorInfo();
            color.DynamicColor = 0;
            color.SunColor = 0;

            foreach (var c in VoxelHelpers.EnumerateVertexNeighbors(Vox.Coordinate, Vertex))
            {
                var v = new VoxelHandle(chunks.ChunkData, c);
                if (!v.IsValid) continue;

                color.SunColor += v.SunColor;
                if (!v.IsEmpty || !v.IsExplored)
                {
                    if (v.Type.EmitsLight) color.DynamicColor = 255;
                    neighborsEmpty += 1;
                    neighborsChecked += 1;
                }
                else
                    neighborsChecked += 1;
            }

            float proportionHit = (float)neighborsEmpty / (float)neighborsChecked;
            color.AmbientColor = (int)Math.Min((1.0f - proportionHit) * 255.0f, 255);
            color.SunColor = (int)Math.Min((float)color.SunColor / (float)neighborsChecked, 255);

            return color;
        }

        private static BoxPrimitive.BoxTextureCoords ComputeTransitionTexture(VoxelHandle V)
        {
            var type = V.Type;
            var primitive = VoxelLibrary.GetPrimitive(type);

            if (!type.HasTransitionTextures && primitive != null)
                return primitive.UVs;
            else if (primitive == null)
                return null;
            else
            {
                var transition = ComputeTransitions(V.Chunk.Manager.ChunkData, V, type);
                return type.TransitionTextures[transition];
            }
        }

        private static BoxTransition ComputeTransitions(
            ChunkData Data,
            VoxelHandle V,
            VoxelType Type)
        {
            if (Type.Transitions == VoxelType.TransitionType.Horizontal)
            {
                var value = ComputeTransitionValueOnPlane(
                    VoxelHelpers.EnumerateManhattanNeighbors2D(V.Coordinate)
                    .Select(c => new VoxelHandle(Data, c)), Type);

                return new BoxTransition()
                {
                    Top = (TransitionTexture)value
                };
            }
            else
            {
                var transitionFrontBack = ComputeTransitionValueOnPlane(
                    VoxelHelpers.EnumerateManhattanNeighbors2D(V.Coordinate, ChunkManager.SliceMode.Z)
                    .Select(c => new VoxelHandle(Data, c)),
                    Type);

                var transitionLeftRight = ComputeTransitionValueOnPlane(
                    VoxelHelpers.EnumerateManhattanNeighbors2D(V.Coordinate, ChunkManager.SliceMode.X)
                    .Select(c => new VoxelHandle(Data, c)),
                    Type);

                return new BoxTransition()
                {
                    Front = (TransitionTexture)transitionFrontBack,
                    Back = (TransitionTexture)transitionFrontBack,
                    Left = (TransitionTexture)transitionLeftRight,
                    Right = (TransitionTexture)transitionLeftRight
                };
            }
        }

        // Todo: Reorder 2d neighbors to make this unecessary.
        private static int[] TransitionMultipliers = new int[] { 1, 2, 4, 8 };

        private static int ComputeTransitionValueOnPlane(IEnumerable<VoxelHandle> Neighbors, VoxelType Type)
        {
            int index = 0;
            int accumulator = 0;
            foreach (var v in Neighbors)
            {
                if (v.IsValid && !v.IsEmpty && v.Type == Type)
                    accumulator += TransitionMultipliers[index];
                index += 1;
            }
            return accumulator;
        }
    }
}