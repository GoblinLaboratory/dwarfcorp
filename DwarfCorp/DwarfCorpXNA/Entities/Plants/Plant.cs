// Tree.cs
// 
//  Modified MIT License (MIT)
//  
//  Copyright (c) 2015 Completely Fair Games Ltd.
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// The following content pieces are considered PROPRIETARY and may not be used
// in any derivative works, commercial or non commercial, without explicit 
// written permission from Completely Fair Games:
// 
// * Images (sprites, textures, etc.)
// * 3D Models
// * Sound Effects
// * Music
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using DwarfCorp.GameStates;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;

namespace DwarfCorp
{
    public class Plant : Body
    {
        public int GrowthHours { get; set; }
        public bool IsGrown { get; set; }
        public string MeshAsset { get; set; }
        public float MeshScale { get; set; }
        public Vector3 BasePosition = Vector3.Zero;
        public float RandomAngle = 0.0f;
        public FarmTile Farm;

        public Plant()
        {
            GrowthHours = 12;
            IsGrown = false;
        }

        public Plant(ComponentManager Manager, string name, Vector3 Position, float RandomAngle, Vector3 bboxSize,
           string meshAsset, float meshScale) :
            base(Manager, name, Matrix.Identity, bboxSize, new Vector3(0.0f, bboxSize.Y / 2, 0.0f), true)
        {
            MeshAsset = meshAsset;
            MeshScale = meshScale;
            GrowthHours = 12;
            IsGrown = false;
            BasePosition = Position;
            this.RandomAngle = RandomAngle;

            // Needs this to ensure plants are initially placed correctly. Listener below only fires when voxels change.
            var under = new VoxelHandle(Manager.World.ChunkManager.ChunkData, GlobalVoxelCoordinate.FromVector3(BasePosition - new Vector3(0.0f, 0.5f, 0.0f)));
            if (under.IsValid && under.RampType != RampType.None)
                LocalTransform = Matrix.CreateRotationY(RandomAngle) * Matrix.CreateTranslation(BasePosition - new Vector3(0.0f, 0.5f, 0.0f));
            else
                LocalTransform = Matrix.CreateRotationY(RandomAngle) * Matrix.CreateTranslation(BasePosition);

            impl_CreateCosmeticChildren(Manager);
        }
        
        private void impl_CreateCosmeticChildren(ComponentManager Manager)
        {
            PropogateTransforms();
            var mesh = AddChild(new InstanceMesh(Manager, "Model", Matrix.CreateRotationY((float)(MathFunctions.Random.NextDouble() * Math.PI)) * Matrix.CreateScale(MeshScale, MeshScale, MeshScale) * Matrix.CreateTranslation(GetBoundingBox().Center() - Position), MeshAsset, false));
            mesh.SetFlag(Flag.ShouldSerialize, false);

            AddChild(new GenericVoxelListener(Manager,
                Matrix.Identity,
                new Vector3(0.25f, 0.25f, 0.25f), // Position just below surface.
                new Vector3(0.0f, -0.30f, 0.0f),
                (v) =>
                {
                    if (v.Type == VoxelChangeEventType.VoxelTypeChanged
                        && (v.NewVoxelType == 0 || !VoxelLibrary.GetVoxelType(v.NewVoxelType).IsSoil))
                    {
                        Die();
                    }
                    else if (v.Type == VoxelChangeEventType.RampsChanged)
                    {
                        if (v.OldRamps != RampType.None && v.NewRamps == RampType.None)
                            LocalTransform = Matrix.CreateRotationY(RandomAngle) * Matrix.CreateTranslation(BasePosition);
                        else if (v.OldRamps == RampType.None && v.NewRamps != RampType.None)
                            LocalTransform = Matrix.CreateRotationY(RandomAngle) * Matrix.CreateTranslation(BasePosition - new Vector3(0.0f, 0.5f, 0.0f));
                    }
                }))
                .SetFlag(Flag.ShouldSerialize, false);
        }

        public override void CreateCosmeticChildren(ComponentManager Manager)
        {
            impl_CreateCosmeticChildren(Manager);
            base.CreateCosmeticChildren(Manager);
        }

        public override void Die()
        {
            if (Farm != null && !(this is Seedling))
            {
                Farm.Plant = null;
                Farm.TriggerAutoReplant(World);
            }
            base.Die();
        }
    }
}
