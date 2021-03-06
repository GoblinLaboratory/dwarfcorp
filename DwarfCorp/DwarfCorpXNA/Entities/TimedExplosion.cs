// BearTrap.cs
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
using System.Runtime.Serialization;
using System.Text;
using DwarfCorp.GameStates;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using System.Threading;

namespace DwarfCorp
{
    public class TimedExplosion : CraftedBody, IUpdateableComponent
    {
        [EntityFactory("Explosion Large")]
        private static GameComponent __factory00(ComponentManager Manager, Vector3 Position, Blackboard Data)
        {
            return new TimedExplosion(Manager, Position, Data.GetData<List<ResourceAmount>>("Resources", null))
            {
                VoxelRadius = 10
            };
        }

        [EntityFactory("Explosion Med")]
        private static GameComponent __factory01(ComponentManager Manager, Vector3 Position, Blackboard Data)
        {
            return new TimedExplosion(Manager, Position, Data.GetData<List<ResourceAmount>>("Resources", null))
            {
                VoxelRadius = 5
            };
        }

        [EntityFactory("Explosion small")]
        private static GameComponent __factory02(ComponentManager Manager, Vector3 Position, Blackboard Data)
        {
            return new TimedExplosion(Manager, Position, Data.GetData<List<ResourceAmount>>("Resources", null))
            {
                VoxelRadius = 2
            };
        }

        public float DamageAmount;
        public Timer DeathTimer;
        public int VoxelRadius = 5;
        public int VoxelsPerTick = 5;

        private Thread PrepThread;

        [JsonProperty]
        private List<GlobalVoxelCoordinate> OrderedExplosionList;
        
        public enum State
        {
            Initializing,
            Prep,
            Ready,
            Exploding,
            Done
        }

        [JsonProperty]
        private State _state = State.Initializing;

        public int ExplosionProgress = 0;


        public TimedExplosion()
        {

        }

        public TimedExplosion(ComponentManager manager, Vector3 pos, List<ResourceAmount> resources) :
            base(manager,
            "BearTrap", Matrix.CreateTranslation(pos),
            new Vector3(1.0f, 1.0f, 1.0f), Vector3.Zero, new DwarfCorp.CraftDetails(manager, "Bear Trap", resources))
        {
            DeathTimer = new Timer(2.0f, true);
            DamageAmount = 200;
            CreateCosmeticChildren(manager);

            DeathTimer.Reset(DeathTimer.TargetTimeSeconds);
        }

        public override void CreateCosmeticChildren(ComponentManager manager)
        {
            AddChild(new Shadow(manager));

            var spriteSheet = new SpriteSheet(ContentPaths.Entities.DwarfObjects.beartrap, 32);

            var sprite = AddChild(new AnimatedSprite(Manager, "Sprite", Matrix.Identity, false)) as AnimatedSprite;

            sprite.AddAnimation(AnimationLibrary.CreateAnimation(spriteSheet, new List<Point> { Point.Zero }, "BearTrapIdle"));

            var sprung = AnimationLibrary.CreateAnimation
                (spriteSheet, new List<Point>
                {
                    new Point(0,1),
                    new Point(1,1),
                    new Point(2,1),
                    new Point(3,1)
                }, "BearTrapTrigger");

            sprung.FrameHZ = 6.6f;

            sprite.AddAnimation(sprung);

            sprite.SetFlag(Flag.ShouldSerialize, false);
            sprite.SetCurrentAnimation("BearTrapIdle", false);

            base.CreateCosmeticChildren(manager);
        }
        
        public override void Update(DwarfTime gameTime, ChunkManager chunks, Camera camera)
        {
            if (Active)
            {

                DeathTimer.Update(gameTime);

                switch (_state)
                {
                    case State.Initializing:
                        if (PrepThread != null)
                            PrepThread.Abort();
                        PrepThread = new Thread(PrepareForExplosion);
                        _state = State.Prep;
                        PrepThread.Start();
                        break;

                    case State.Prep:
                        if (PrepThread == null) // Must have been saved mid-prep.
                            _state = State.Initializing;
                        break;

                    case State.Ready:
                        if (OrderedExplosionList == null) // Just a failsafe.
                            throw new InvalidOperationException();

                        if (DeathTimer.HasTriggered)
                        {
                            _state = State.Exploding;

                            EnumerateChildren().OfType<AnimatedSprite>().FirstOrDefault().SetCurrentAnimation("BearTrapTrigger", true);
                            SoundManager.PlaySound(ContentPaths.Audio.Oscar.sfx_trap_destroyed, GlobalTransform.Translation, false);

                            foreach (Body body in Manager.World.CollisionManager.EnumerateIntersectingObjects(
                                new BoundingBox(LocalPosition - new Vector3(VoxelRadius, VoxelRadius, VoxelRadius), LocalPosition + new Vector3(VoxelRadius, VoxelRadius, VoxelRadius)), CollisionManager.CollisionType.Both))
                            {
                                var health = body.EnumerateAll().OfType<Health>().FirstOrDefault();
                                if (health != null)
                                    health.Damage(DamageAmount);
                            }
                        }

                        break;

                    case State.Exploding:
                        if (OrderedExplosionList == null)
                            throw new InvalidOperationException();

                        var voxelsExploded = 0;
                        while (true)
                        {
                            if (voxelsExploded >= VoxelsPerTick)
                                break;

                            if (ExplosionProgress >= OrderedExplosionList.Count)
                            {
                                Delete();
                                _state = State.Done;
                                break;
                            }

                            var nextVoxel = new VoxelHandle(Manager.World.ChunkManager.ChunkData, OrderedExplosionList[ExplosionProgress]);
                            ExplosionProgress += 1;

                            if (nextVoxel.IsValid)
                            {
                                if (!nextVoxel.Type.IsInvincible && !nextVoxel.IsEmpty)
                                {
                                    voxelsExploded += 1;
                                    nextVoxel.Type = VoxelLibrary.emptyType;
                                    Manager.World.ParticleManager.Effects["explode"].Trigger(1, nextVoxel.Coordinate.ToVector3() + new Vector3(0.5f, 0.5f, 0.5f), Color.White);
                                }
                            }
                        }

                        break;

                    case State.Done:
                    default:
                        if (PrepThread != null)
                            PrepThread.Abort();
                        break;
                }                
            }

            base.Update(gameTime, chunks, camera);
        }

        private void PrepareForExplosion()
        {
            var pos = LocalPosition;
            var explodeList = new List<Tuple<GlobalVoxelCoordinate, float>>();

            for (var x = (int)(pos.X - VoxelRadius); x < (int)(pos.X + VoxelRadius); ++x)
                for (var y = (int)(pos.Y - VoxelRadius); y < (int)(pos.Y + VoxelRadius); ++y)
                    for (var z = (int)(pos.Z - VoxelRadius); z < (int)(pos.Z + VoxelRadius); ++z)
                    {
                        var voxelCenter = new Vector3(x, y, z) + new Vector3(0.5f, 0.5f, 0.5f);
                        var distance = (voxelCenter - pos).Length();
                        if (distance <= VoxelRadius)
                            explodeList.Add(Tuple.Create(new GlobalVoxelCoordinate(x, y, z), distance));
                    }

            OrderedExplosionList = explodeList.OrderBy(t => t.Item2).Select(t => t.Item1).ToList();
            _state = State.Ready;
        }
    }
}
