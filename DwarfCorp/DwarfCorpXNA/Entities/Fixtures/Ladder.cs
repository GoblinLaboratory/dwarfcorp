// Ladder.cs
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
using System.Text;
using DwarfCorp.GameStates;
using Microsoft.Xna.Framework;

namespace DwarfCorp
{
    public class Ladder : CraftedFixture
    {
        [EntityFactory("Ladder")]
        private static GameComponent __factory(ComponentManager Manager, Vector3 Position, Blackboard Data)
        {
            return new Ladder(
                Manager,
                Position,
                Data.GetData<List<ResourceAmount>>("Resources", new List<ResourceAmount>() { new ResourceAmount(ResourceType.Wood) }));
        }

        protected static Dictionary<Resource.ResourceTags, Point> Sprites = new Dictionary<Resource.ResourceTags, Point>()
        {
            {
                Resource.ResourceTags.Metal,
                new Point(3, 8)
            },
            {
                Resource.ResourceTags.Stone,
                new Point(2, 8)
            },
            {
                Resource.ResourceTags.Wood,
                new Point(2, 0)
            }
        };

        protected static Point DefaultSprite = new Point(2, 8);


        public Ladder()
        {

        }

        public Ladder(ComponentManager manager, Vector3 position, List<ResourceAmount> resourceType) :
            base(manager, position, new SpriteSheet(ContentPaths.Entities.Furniture.interior_furniture, 32, 32), new FixtureCraftDetails(manager)
            {
                Resources = resourceType.ConvertAll(p => new ResourceAmount(p)),
                Sprites = Ladder.Sprites,
                DefaultSpriteFrame = Ladder.DefaultSprite,
                CraftType = "Ladder"
            }, SimpleSprite.OrientMode.Fixed)
        {
            Name = resourceType[0].ResourceType + " Ladder";
            Tags.Add("Climbable");
            OrientToWalls();
            CollisionType = CollisionManager.CollisionType.Static;
        }

        public override void CreateCosmeticChildren(ComponentManager manager)
        {
            base.CreateCosmeticChildren(manager);

            GetComponent<SimpleSprite>().OrientationType = SimpleSprite.OrientMode.Fixed;
            GetComponent<SimpleSprite>().LocalTransform = Matrix.CreateTranslation(new Vector3(0, 0, 0.45f)) * Matrix.CreateRotationY(0.0f);
        }
    }

}
