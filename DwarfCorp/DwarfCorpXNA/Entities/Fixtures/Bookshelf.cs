using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DwarfCorp.GameStates;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;

namespace DwarfCorp
{
    public class Bookshelf : CraftedBody, IRenderableComponent
    {
        [EntityFactory("Bookshelf")]
        private static GameComponent __factory(ComponentManager Manager, Vector3 Position, Blackboard Data)
        {
            return new Bookshelf(Manager, Position, Data.GetData<List<ResourceAmount>>("Resources", null)) { Tags = new List<string>() { "Research" } };
        }

        public Bookshelf()
        {
            Tags.Add("Books");
            CollisionType = CollisionManager.CollisionType.Static;
        }

        public Bookshelf(ComponentManager manager, Vector3 position, List<ResourceAmount> resources) :
            base(manager, "Bookshelf", Matrix.CreateTranslation(position), new Vector3(32.0f / 32.0f, 8.0f / 32.0f, 20.0f / 32.0f), new Vector3(0.5f, 0.5f, 0.5f), new CraftDetails(manager, "Bookshelf", resources))
        {
            Tags.Add("Books");
            CollisionType = CollisionManager.CollisionType.Static;
            SetFlag(Flag.RotateBoundingBox, true);

            CreateCosmeticChildren(manager);
            OrientToWalls();
        }

        public override void RenderSelectionBuffer(DwarfTime gameTime, ChunkManager chunks, Camera camera, SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, Shader effect)
        {
            effect.SelectionBufferColor = this.GetGlobalIDColor().ToVector4();
            GetComponent<Box>().Render(gameTime, chunks, camera, spriteBatch, graphicsDevice, effect, false);
        }

#if !DEBUG
        public void Render(DwarfTime gameTime, ChunkManager chunks, Camera camera, SpriteBatch spriteBatch,
            GraphicsDevice graphicsDevice, Shader effect, bool renderingForWater)
        {
            // Only renders to selection buffer
        }
#endif

        public override void CreateCosmeticChildren(ComponentManager Manager)
        {
            base.CreateCosmeticChildren(Manager);

            var spriteSheet = AssetManager.GetContentTexture(ContentPaths.Entities.Furniture.bookshelf);

            AddChild(new Box(Manager,
                "model",
                Matrix.CreateTranslation(new Vector3(-0.3f, 0, 0.2f)),
                new Vector3(1.0f, 1.0f, 0.3f),
                new Vector3(0.0f, 0.0f, 0.0f),
                "bookshelf",
                spriteSheet)).SetFlag(Flag.ShouldSerialize, false);

            AddChild(new GenericVoxelListener(Manager, Matrix.Identity, new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.0f, -0.5f, 0.0f), (changeEvent) =>
            {
                if (changeEvent.Type == VoxelChangeEventType.VoxelTypeChanged && changeEvent.NewVoxelType == 0)
                    Die();
            })).SetFlag(Flag.ShouldSerialize, false).SetFlag(Flag.RotateBoundingBox, true);

        }
    }
}
