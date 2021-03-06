// DwarfGame.cs
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
using System.Diagnostics;
using System.IO;
using System.Threading;
using ContentGenerator;
using DwarfCorp.GameStates;
using DwarfCorp.Gui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Linq;
using Newtonsoft.Json;
#if !XNA_BUILD
using SDL2;
#endif
using SharpRaven;
using SharpRaven.Data;

namespace DwarfCorp
{

    public class DwarfGame : Game
    {
#if XNA_BUILD
        public static bool COMPRESSED_BINARY_SAVES = true;
#else
        public static bool COMPRESSED_BINARY_SAVES = false;
#endif

        public GameStateManager StateManager { get; set; }
        public GraphicsDeviceManager Graphics;
        public AssetManager TextureManager { get; set; }
        public static SpriteBatch SpriteBatch { get; set; }

        public static Gui.Input.GumInputMapper GumInputMapper;
        public static Gui.Input.Input GumInput;
        public static Gui.RenderData GuiSkin;

        public const string GameName = "DwarfCorp";
        public static bool HasRendered = false;
        private static StreamWriter _logwriter;
        private static TextWriter _initialOut;
        private static TextWriter _initialError;

        private static int MainThreadID;

#if SHARP_RAVEN && !DEBUG
        private RavenClient ravenClient;
#endif
        public DwarfGame()
        {

            //BoundingBox foo = new BoundingBox(new Vector3(0, 0, 0), new Vector3(1, 1, 1));
            //string serialized = FileUtils.SerializeBasicJSON(foo);
            //BoundingBox deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<BoundingBox>(serialized, new BoxConverter());
            //string code = ContentPathGenerator.GenerateCode();
            //Console.Out.Write(code);
            GameState.Game = this;
            //Content.RootDirectory = "Content";
            StateManager = new GameStateManager(this);
            Graphics = new GraphicsDeviceManager(this);
            Window.Title = "DwarfCorp";
            Window.AllowUserResizing = false;
            MainThreadID = System.Threading.Thread.CurrentThread.ManagedThreadId;
            GameSettings.Load();
            AssetManager.Initialize(Content, GraphicsDevice, GameSettings.Default);


            try
            {
#if SHARP_RAVEN && !DEBUG
                if (GameSettings.Default.AllowReporting)
                {
                    ravenClient =
                        new RavenClient(
                            "https://af78a676a448474dacee4c72a9197dd2:0dd0a01a9d4e4fa4abc6e89ac7538346@sentry.io/192119");
                    ravenClient.Tags["Version"] = Program.Version;
                }
#if XNA_BUILD
                ravenClient.Tags["Platform"] = "XNA";
#else
                ravenClient.Tags["Platform"] = "FNA";
#endif
#endif
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.ToString());
            }

            // Check GUI scale - if the settings are bad, fix.
            if (GameSettings.Default.GuiScale * 480 > GameSettings.Default.ResolutionY)
                GameSettings.Default.GuiScale = 1;

            Graphics.IsFullScreen = GameSettings.Default.Fullscreen;
            Graphics.PreferredBackBufferWidth = GameSettings.Default.Fullscreen ? GameSettings.Default.ResolutionX : Math.Min(GameSettings.Default.ResolutionX, GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width);
            Graphics.PreferredBackBufferHeight = GameSettings.Default.Fullscreen ? GameSettings.Default.ResolutionY : Math.Min(GameSettings.Default.ResolutionY,
                GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height);
            Graphics.SynchronizeWithVerticalRetrace = GameSettings.Default.VSync;
            MathFunctions.Random = new ThreadSafeRandom(new Random().Next());
            try
            {
                Graphics.ApplyChanges();
            }
            catch(NoSuitableGraphicsDeviceException exception)
            {
                Console.Error.WriteLine(exception.Message);
#if SHARP_RAVEN && !DEBUG
                if (ravenClient != null)
                    ravenClient.Capture(new SentryEvent(exception));
#endif
            }
        }

#if !XNA_BUILD
        public static string GetGameDirectory()
        {
            string platform = SDL.SDL_GetPlatform();
            if (platform.Equals("Windows"))
            {
                return Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.MyDocuments
                    ),
                    "SavedGames",
                    GameName
                );
            }
            else if (platform.Equals("Mac OS X"))
            {
                string osConfigDir = Environment.GetEnvironmentVariable("HOME");
                if (String.IsNullOrEmpty(osConfigDir))
                {
                    return "."; // Oh well.
                }
                osConfigDir += "/Library/Application Support";
                return Path.Combine(osConfigDir, GameName);
            }
            else if (platform.Equals("Linux"))
            {
                string osConfigDir = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                if (String.IsNullOrEmpty(osConfigDir))
                {
                    osConfigDir = Environment.GetEnvironmentVariable("HOME");
                    if (String.IsNullOrEmpty(osConfigDir))
                    {
                        return "."; // Oh well.
                    }
                    osConfigDir += "/.local/share";
                }
                return Path.Combine(osConfigDir, GameName);
            }
            throw new Exception("SDL platform unhandled: " + platform);
        }
#endif

#if XNA_BUILD
        public static string GetGameDirectory()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + ProgramData.DirChar + GameName;
        }
#endif

        public static string GetSaveDirectory()
        {
            if (String.IsNullOrEmpty(GameSettings.Default.SaveLocation))
                return DwarfGame.GetGameDirectory() + Path.DirectorySeparatorChar + "Saves";
            else
                return GameSettings.Default.SaveLocation + Path.DirectorySeparatorChar + "Saves";
        }

        public static string GetWorldDirectory()
        {
            if (String.IsNullOrEmpty(GameSettings.Default.SaveLocation))
                return DwarfGame.GetGameDirectory() + Path.DirectorySeparatorChar + "Worlds";
            else
                return GameSettings.Default.SaveLocation + Path.DirectorySeparatorChar + "Worlds";
        }

        public static void InitializeLogger()
        {
            try
            {
                Trace.Listeners.Clear();
                var dir = GetGameDirectory();
                if (!System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }
                var path = ProgramData.CreatePath(dir, "log.txt");
                if (!File.Exists(path))
                {
                    File.Create(path).Close();
                }
                
                var logFile = new FileInfo(path);
                if (logFile.Length > 5e7)
                {
                    Console.Out.WriteLine("Log file at {0} was too large ({1} bytes). Clearing it.", path, logFile.Length);
                    System.IO.File.WriteAllText(path, string.Empty);
                }
                FileStream writerOutput = new FileStream(path, FileMode.Append, FileAccess.Write);
                _logwriter = new StreamWriter(writerOutput) { AutoFlush = true };
                _initialOut = Console.Out;
                _initialError = Console.Error;
                Console.SetOut(_logwriter);
                Console.SetError(_logwriter);
                Console.Out.WriteLine("Game started at " + DateTime.Now.ToShortDateString() + " : " + DateTime.Now.ToShortTimeString());
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Failed to initialize logger: {0}", exception.ToString());
            }
        }

        protected override void Initialize()
        {
#if SHARP_RAVEN && !DEBUG
            try
            {
#endif
            var dir = GetGameDirectory();
                if (!System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }
                InitializeLogger();
                Thread.CurrentThread.Name = "Main";
                // Goes before anything else so we can track from the very start.
                GamePerformance.Initialize(this);

                SpriteBatch = new SpriteBatch(GraphicsDevice);
                base.Initialize();
#if SHARP_RAVEN && !DEBUG
            }
            catch (Exception exception)
            {
                if (ravenClient != null)
                    ravenClient.Capture(new SentryEvent(exception));
                throw;
            }
#endif
            }

        protected override void LoadContent()
        {
#if SHARP_RAVEN && !DEBUG
            try
            {
#endif
            // Prepare GemGui
             GumInputMapper = new Gui.Input.GumInputMapper(Window.Handle);
                GumInput = new Gui.Input.Input(GumInputMapper);

                // Register all bindable actions with the input system.
                //GumInput.AddAction("TEST", Gui.Input.KeyBindingType.Pressed);

                GuiSkin = new RenderData(GraphicsDevice, Content);

                if (SoundManager.Content == null)
                {
                    SoundManager.Content = Content;
                    SoundManager.LoadDefaultSounds();
#if XNA_BUILD
                    //SoundManager.SetActiveSongs(ContentPaths.Music.dwarfcorp, ContentPaths.Music.dwarfcorp_2,
                    //    ContentPaths.Music.dwarfcorp_3, ContentPaths.Music.dwarfcorp_4, ContentPaths.Music.dwarfcorp_5);
#endif
                }

                if (GameSettings.Default.DisplayIntro)
                {
                    StateManager.PushState(new IntroState(this, StateManager));
                }
                else
                {
                    StateManager.PushState(new MainMenuState(this, StateManager));
                }

                BiomeLibrary.InitializeStatics();
                EmbarkmentLibrary.InitializeDefaultLibrary();
                VoxelChunk.InitializeStatics();
                ControlSettings.Load();
                Drawer2D.Initialize(Content, GraphicsDevice);
                ResourceLibrary.Initialize();
                base.LoadContent();
#if SHARP_RAVEN && !DEBUG
            }
            catch (Exception exception)
            {
                if (ravenClient != null)
                    ravenClient.Capture(new SentryEvent(exception));
                throw;
            }
#endif
            }

        public void CaptureException(Exception exception)
        {
#if SHARP_RAVEN && !DEBUG
            if (ravenClient != null)
                ravenClient.Capture(new SentryEvent(exception));
#endif
        }

        protected override void Update(GameTime time)
        {
            if (!IsActive)
            {
                base.Update(time);
                return;
            }
#if SHARP_RAVEN && !DEBUG
            try
            {
#endif
                GamePerformance.Instance.PreUpdate();
                DwarfTime.LastTime.Update(time);
                StateManager.Update(DwarfTime.LastTime);
                base.Update(time);
                GamePerformance.Instance.PostUpdate();
#if SHARP_RAVEN && !DEBUG
            }
            catch (Exception exception)
            {
                if (ravenClient != null)
                    ravenClient.Capture(new SentryEvent(exception));
                throw;
            }
#endif
            HasRendered = false;
        }

        protected override void Draw(GameTime time)
        {
            HasRendered = true;
#if SHARP_RAVEN && !DEBUG
            try
            {
#endif
            GamePerformance.Instance.PreRender();
                StateManager.Render(DwarfTime.LastTime);
                GraphicsDevice.SetRenderTarget(null);
                base.Draw(time);
                GamePerformance.Instance.PostRender();
                GamePerformance.Instance.Render(SpriteBatch);
#if SHARP_RAVEN && !DEBUG
            }
            catch (Exception exception)
            {
                if (ravenClient != null)
                    ravenClient.Capture(new SentryEvent(exception));
                throw;
            }
#endif
        }

        public static void SafeSpriteBatchBegin(SpriteSortMode sortMode, BlendState blendState, SamplerState samplerstate, 
            DepthStencilState depthState, RasterizerState rasterState, Effect effect, Matrix world)
        {
            Debug.Assert(IsMainThread);
            if (SpriteBatch.GraphicsDevice.IsDisposed || SpriteBatch.IsDisposed)
            {
                SpriteBatch = new SpriteBatch(GameState.Game.GraphicsDevice);
            }

            try
            {
                SpriteBatch.Begin(sortMode,
                    blendState,
                    samplerstate,
                    depthState,
                    rasterState,
                    effect,
                    world);
            }
            catch (InvalidOperationException exception)
            {
                Console.Error.Write(exception);
                SpriteBatch.Dispose();
                SpriteBatch = new SpriteBatch(GameState.Game.GraphicsDevice);
                SpriteBatch.Begin(sortMode,
                    blendState,
                    samplerstate,
                    depthState,
                    rasterState,
                    effect,
                    world);
            }
        }

        protected override void OnExiting(object sender, EventArgs args)
        {
            Console.SetOut(_initialOut);
            Console.SetError(_initialError);
            _logwriter.Dispose();
            ExitGame = true;
            Program.SignalShutdown();
            base.OnExiting(sender, args);
        }

        // If called in the non main thread, will return false;
        public static bool IsMainThread
        {
            get { return System.Threading.Thread.CurrentThread.ManagedThreadId == MainThreadID; }
        }

        public static bool ExitGame = false;
    }

}
