﻿/*
Copyright (c) 2011, Sean Kasun
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF
THE POSSIBILITY OF SUCH DAMAGE.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Collections;

namespace Terrafirma
{
    struct TileInfo
    {
        public string name;
        public UInt32 color;
        public bool hasExtra;
    }
    struct WallInfo
    {
        public string name;
        public UInt32 color;
    }
    struct Tile
    {
        public bool isActive;
        public byte type;
        public bool hasLight;
        public byte wall;
        public byte liquid;
        public bool isLava;
        public Int16 u, v, wallu, wallv;
    }
    struct ChestItem
    {
        public byte stack;
        public string name;
    }
    struct Chest
    {
        public Int32 x, y;
        public ChestItem[] items;
    }
    struct Sign
    {
        public string text;
        public Int32 x, y;
    }
    struct NPC
    {
        public string name;
        public float x, y;
        public bool isHomeless;
        public Int32 homeX, homeY;
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        const UInt32 MapVersion=2;
        const double MaxScale = 16.0;
        const double MinScale = 1.0;

        double curX, curY, curScale;
        byte[] bits;
        WriteableBitmap mapbits;
        DispatcherTimer resizeTimer,hiliteTimer;
        int curWidth, curHeight, newWidth, newHeight;
        bool loaded = false;
        Tile[] tiles;
        Int32 tilesWide, tilesHigh;
        Int32 spawnX, spawnY;
        Int32 groundLevel,rockLevel;
        string[] worlds;
        string currentWorld;
        List<Chest> chests = new List<Chest>();
        List<Sign> signs = new List<Sign>();
        List<NPC> npcs = new List<NPC>();
        Render render;

        TileInfo[] tileInfo;
        WallInfo[] wallInfo;
        UInt32 skyColor, earthColor, rockColor, hellColor, lavaColor, waterColor;
        byte hilight=0;
        int hilightTick = 0;
        bool isHilight = false;

        public MainWindow()
        {
            InitializeComponent();

            fetchWorlds();

           

            XmlDocument xml=new XmlDocument();
            string xmlData = string.Empty;
            using (Stream stream = this.GetType().Assembly.GetManifestResourceStream("Terrafirma.tiles.xml"))
            {
                xml.Load(stream);
            }
            XmlNodeList tileList=xml.GetElementsByTagName("tile");
            tileInfo = new TileInfo[tileList.Count];
            for (int i = 0; i < tileList.Count; i++)
            {
                int id = Convert.ToInt32(tileList[i].Attributes["num"].Value);
                tileInfo[id].name = tileList[i].Attributes["name"].Value;
                tileInfo[id].color = parseColor(tileList[i].Attributes["color"].Value);
                tileInfo[id].hasExtra = tileList[i].Attributes["hasExtra"] != null;
            }
            XmlNodeList wallList = xml.GetElementsByTagName("wall");
            wallInfo = new WallInfo[wallList.Count+1];
            for (int i = 0; i < wallList.Count; i++)
            {
                int id = Convert.ToInt32(wallList[i].Attributes["num"].Value);
                wallInfo[id].name = wallList[i].Attributes["name"].Value;
                wallInfo[id].color = parseColor(wallList[i].Attributes["color"].Value);
            }
            XmlNodeList globalList = xml.GetElementsByTagName("global");
            for (int i = 0; i < globalList.Count; i++)
            {
                string kind = globalList[i].Attributes["id"].Value;
                UInt32 color=parseColor(globalList[i].Attributes["color"].Value);
                switch (kind)
                {
                    case "sky":
                        skyColor = color;
                        break;
                    case "earth":
                        earthColor = color;
                        break;
                    case "rock":
                        rockColor = color;
                        break;
                    case "hell":
                        hellColor = color;
                        break;
                    case "water":
                        waterColor = color;
                        break;
                    case "lava":
                        lavaColor = color;
                        break;
                }
            }

            render = new Render(tileInfo,wallInfo,skyColor,earthColor,rockColor,hellColor,waterColor,lavaColor);
            //this resize timer is used so we don't get killed on the resize
            resizeTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(20), DispatcherPriority.Normal,
                delegate
                {
                    resizeTimer.IsEnabled = false;
                    curWidth = newWidth;
                    curHeight = newHeight;
                    mapbits = new WriteableBitmap(curWidth, curHeight, 96.0, 96.0, PixelFormats.Bgr32, null);
                    Map.Source = mapbits;
                    bits = new byte[curWidth * curHeight * 4];
                    Map.Width = curWidth;
                    Map.Height = curHeight;
                    if (loaded)
                        RenderMap();
                    else
                    {
                        var rect=new Int32Rect(0,0,curWidth,curHeight);
                        for (int i=0;i<curWidth*curHeight*4;i++)
                            bits[i]=0xff;
                        mapbits.WritePixels(rect,bits,curWidth*4,0);
                    }
                },
                Dispatcher) {IsEnabled=false};
            hiliteTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(50), DispatcherPriority.Normal,
                delegate
                {
                    hilightTick++;
                    hilightTick &= 15;
                    RenderMap();
                }, Dispatcher) { IsEnabled = false };
            curWidth = 496;
            curHeight = 400;
            newWidth = 496;
            newHeight = 400;
            mapbits = new WriteableBitmap(curWidth, curHeight, 96.0, 96.0, PixelFormats.Bgr32, null);
            Map.Source = mapbits;
            bits = new byte[curWidth * curHeight * 4];
            curX = curY = 0;
            curScale = 1.0;
        }
        private void fetchWorlds()
        {
            string path=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            path = Path.Combine(path, "My Games");
            path = Path.Combine(path, "Terraria");
            path = Path.Combine(path, "Worlds");
            if (Directory.Exists(path))
                worlds = Directory.GetFiles(path, "*.wld");
            else
                worlds = new string[0];
            int max = worlds.Length;
            if (max > 9) max = 9;
            for (int i = 0; i < max; i++)
            {
                MenuItem item = new MenuItem();
                item.Header = Path.GetFileNameWithoutExtension(worlds[i]);
                item.Command = MapCommands.OpenWorld;
                item.CommandParameter = i;
                CommandBindings.Add(new CommandBinding(MapCommands.OpenWorld, OpenWorld));
                item.InputGestureText = String.Format("Ctrl+{0}", (i + 1));
                InputBinding inp = new InputBinding(MapCommands.OpenWorld, new KeyGesture(Key.D1 + i, ModifierKeys.Control));
                inp.CommandParameter = i;
                InputBindings.Add(inp);
                Worlds.Items.Add(item);
            }
        }

        private UInt32 parseColor(string color)
        {
            UInt32 c = 0;
            for (int j = 0; j < color.Length; j++)
            {
                c <<= 4;
                if (color[j] >= '0' && color[j] <= '9')
                    c |= (byte)(color[j] - '0');
                else if (color[j] >= 'A' && color[j] <= 'F')
                    c |= (byte)(10 + color[j] - 'A');
                else if (color[j] >= 'a' && color[j] <= 'f')
                    c |= (byte)(10 + color[j] - 'a');
            }
            return c;
        }

        private void Load(string world)
        {
            currentWorld = world;
            using (BinaryReader b = new BinaryReader(File.OpenRead(world)))
            {
                if (b.ReadUInt32() != MapVersion)
                {
                    //error
                }
                Title = b.ReadString();
                b.BaseStream.Seek(20, SeekOrigin.Current); //skip id and bounds
                tilesHigh = b.ReadInt32();
                tilesWide = b.ReadInt32();
                spawnX = b.ReadInt32();
                spawnY = b.ReadInt32();
                groundLevel = (int)b.ReadDouble();
                rockLevel = (int)b.ReadDouble();
                b.BaseStream.Seek(48, SeekOrigin.Current); //skip flags and other settings
                tiles = new Tile[tilesWide * tilesHigh];
                for (int i = 0; i < tilesWide * tilesHigh; i++)
                {
                    tiles[i].isActive = b.ReadBoolean();
                    if (tiles[i].isActive)
                    {
                        tiles[i].type = b.ReadByte();
                        if (tileInfo[tiles[i].type].hasExtra)
                        {
                            tiles[i].u = b.ReadInt16();
                            tiles[i].v = b.ReadInt16();
                        }
                        else
                        {
                            tiles[i].u = -1;
                            tiles[i].v = -1;
                        }
                    }
                    tiles[i].hasLight = b.ReadBoolean();
                    if (b.ReadBoolean())
                    {
                        tiles[i].wall = b.ReadByte();
                        tiles[i].wallu = -1;
                        tiles[i].wallv = -1;
                    }
                    else
                        tiles[i].wall = 0;
                    if (b.ReadBoolean())
                    {
                        tiles[i].liquid = b.ReadByte();
                        tiles[i].isLava = b.ReadBoolean();
                    }
                    else
                        tiles[i].liquid = 0;
                }
                chests.Clear();
                for (int i = 0; i < 1000; i++)
                {
                    if (b.ReadBoolean())
                    {
                        Chest chest = new Chest();
                        chest.items = new ChestItem[20];
                        chest.x = b.ReadInt32();
                        chest.y = b.ReadInt32();
                        for (int ii = 0; ii < 20; ii++)
                        {
                            chest.items[ii].stack = b.ReadByte();
                            if (chest.items[ii].stack > 0)
                                chest.items[ii].name = b.ReadString();
                        }
                        chests.Add(chest);
                    }
                }
                signs.Clear();
                for (int i = 0; i < 1000; i++)
                {
                    if (b.ReadBoolean())
                    {
                        Sign sign = new Sign();
                        sign.text = b.ReadString();
                        sign.x = b.ReadInt32();
                        sign.y = b.ReadInt32();
                        signs.Add(sign);
                    }
                }
                npcs.Clear();
                while (b.ReadBoolean())
                {
                    NPC npc = new NPC();
                    npc.name = b.ReadString();
                    npc.x = b.ReadSingle();
                    npc.y = b.ReadSingle();
                    npc.isHomeless = b.ReadBoolean();
                    npc.homeX = b.ReadInt32();
                    npc.homeY = b.ReadInt32();
                    npcs.Add(npc);

                    if (!npc.isHomeless)
                    {
                        MenuItem item = new MenuItem();
                        item.Header = String.Format("Jump to {0}'s Home", npc.name);
                        item.Click += new RoutedEventHandler(jumpNPC);
                        item.Tag = npc;
                        NPCs.Items.Add(item);
                        NPCs.IsEnabled = true;
                    }
                }
            }

            render.SetWorld(tiles, tilesWide, tilesHigh, groundLevel, rockLevel);
            //load info
            loaded = true;
        }

        void jumpNPC(object sender, RoutedEventArgs e)
        {
            MenuItem item = sender as MenuItem;
            NPC npc = (NPC)item.Tag;
            curX = npc.homeX;
            curY = npc.homeY;
            RenderMap();
        }
        


        private void RenderMap()
        {
            var rect = new Int32Rect(0, 0, curWidth, curHeight);

            double startx = curX - (curWidth / (2 * curScale));
            double starty = curY - (curHeight / (2 * curScale));
            render.Draw(curWidth, curHeight, startx, starty, curScale, bits,
                isHilight,hilight,hilightTick,Lighting.IsChecked,
                UseTextures.IsChecked && curScale>2.0);

            //draw map here with curX,curY,curScale
            mapbits.WritePixels(rect, bits, curWidth * 4, 0);
        }

        private void Map_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.HeightChanged)
                newHeight = (int)e.NewSize.Height;
            if (e.WidthChanged)
                newWidth = (int)e.NewSize.Width;
            if (e.WidthChanged || e.HeightChanged)
            {
                resizeTimer.IsEnabled = true;
                resizeTimer.Stop();
                resizeTimer.Start();
            }
            e.Handled = true;
        }

        private void Map_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            curScale += (double)e.Delta / 500.0;
            if (curScale < MinScale)
                curScale = MinScale;
            if (curScale > MaxScale)
                curScale = MaxScale;
            if (loaded)
                RenderMap();

        }

        private void Map_MouseMove(object sender, MouseEventArgs e)
        {
            if (Map.IsMouseCaptured)
            {
                Point curPos = e.GetPosition(Map);
                Vector v = start - curPos;
                curX += v.X / curScale;
                curY += v.Y / curScale;
                if (curX < 0) curX = 0;
                if (curY < 0) curY = 0;
                if (curX > tilesWide) curX = tilesWide;
                if (curY > tilesHigh) curY = tilesHigh;
                start = curPos;
                if (loaded)
                    RenderMap();
            }
            else
            {
                Point curPos = e.GetPosition(Map);
                Vector v = start - curPos;
                if (v.X > 50 || v.Y > 50)
                    CloseAllPops();

                double startx = curX - (curWidth / (2 * curScale));
                double starty = curY - (curHeight / (2 * curScale));
                int sy = (int)(curPos.Y / curScale + starty);
                int sx = (int)(curPos.X / curScale + startx);
                if (sx >= 0 && sx < tilesWide && sy >= 0 && sy < tilesHigh)
                {
                    int offset = sy + sx * tilesHigh;
                    string label = "Nothing";
                    if (tiles[offset].wall > 0)
                        label = wallInfo[tiles[offset].wall].name;
                    if (tiles[offset].liquid > 0)
                        label = tiles[offset].isLava ? "Lava" : "Water";
                    if (tiles[offset].isActive)
                        label = tileInfo[tiles[offset].type].name;
                    statusText.Text = String.Format("{0},{1} {2}", sx, sy, label);
                }
                else
                    statusText.Text = "";
            }
        }

        Point start;
        private void Map_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CloseAllPops();

            Map.Focus();
            Map.CaptureMouse();
            start = e.GetPosition(Map);
        }

        private void Map_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Map.ReleaseMouseCapture();
        }
        private SignPopup signPop=null;
        private ChestPopup chestPop=null;

        private void CloseAllPops()
        {
            if (signPop != null)
            {
                signPop.IsOpen = false;
                signPop = null;
            }
            if (chestPop != null)
            {
                chestPop.IsOpen = false;
                chestPop = null;
            }
        }

        private void Map_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            CloseAllPops();
            Point curPos = e.GetPosition(Map);
            start = curPos;
            double startx = curX - (curWidth / (2 * curScale));
            double starty = curY - (curHeight / (2 * curScale));
            int sy = (int)(curPos.Y / curScale + starty);
            int sx = (int)(curPos.X / curScale + startx);
            foreach (Chest c in chests)
            {
                //chests are 2x2, and their x/y is upper left corner
                if ((c.x == sx || c.x + 1 == sx) && (c.y == sy || c.y + 1 == sy))
                {
                    ArrayList items = new ArrayList();
                    for (int i = 0; i < c.items.Length; i++)
                    {
                        if (c.items[i].stack > 0)
                            items.Add(String.Format("{0} {1}", c.items[i].stack, c.items[i].name));
                    }
                    chestPop = new ChestPopup(items);
                    chestPop.IsOpen = true;
                }
            }
            foreach (Sign s in signs)
            {
                //signs are 2x2, and their x/y is upper left corner
                if ((s.x == sx || s.x + 1 == sx) && (s.y == sy || s.y + 1 == sy))
                {
                    signPop = new SignPopup(s.text);
                    signPop.IsOpen = true;
                }
            }
        }

        int moving = 0; //moving bitmask
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            bool changed = false;
            switch (e.Key)
            {
                case Key.Up:
                case Key.W:
                    moving |= 1;
                    break;
                case Key.Down:
                case Key.S:
                    moving |= 2;
                    break;
                case Key.Left:
                case Key.A:
                    moving |= 4;
                    break;
                case Key.Right:
                case Key.D:
                    moving |= 8;
                    break;
                case Key.PageUp:
                case Key.E:
                    curScale += 0.5;
                    if (curScale > MaxScale)
                        curScale = MaxScale;
                    changed = true;
                    break;
                case Key.PageDown:
                case Key.Q:
                    curScale -= 0.5;
                    if (curScale < MinScale)
                        curScale = MinScale;
                    changed = true;
                    break;
            }
            if (moving != 0)
            {
                double speed = 10.0;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    speed *= 2;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                    speed *= 10.0;
                if ((moving & 1) != 0) //up
                    curY -= speed / curScale;
                if ((moving & 2) != 0) //down
                    curY += speed / curScale;
                if ((moving & 4) != 0) //left
                    curX -= speed / curScale;
                if ((moving & 8) != 0) //right
                    curX += speed / curScale;

                if (curX < 0) curX = 0;
                if (curY < 0) curY = 0;
                if (curX > tilesWide) curX = tilesWide;
                if (curY > tilesHigh) curY = tilesHigh;
                changed = true;
            }
            if (changed)
            {
                e.Handled = true;
                if (loaded)
                    RenderMap();
            }
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Up:
                case Key.W:
                    moving &= ~1;
                    break;
                case Key.Down:
                case Key.S:
                    moving &= ~2;
                    break;
                case Key.Left:
                case Key.A:
                    moving &= ~4;
                    break;
                case Key.Right:
                case Key.D:
                    moving &= ~8;
                    break;
            }
        }

        private void Open_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "Terraria Worlds|*.wld";
            var result = dlg.ShowDialog();
            if (result == true)
            {
                Loading load = new Loading();
                load.Show();
                Load(dlg.FileName);
                curX = spawnX;
                curY = spawnY;
                RenderMap();
                load.Close();
            }
        }
        private void OpenWorld(object sender, ExecutedRoutedEventArgs e)
        {
            int id = (int)e.Parameter;
            Loading load = new Loading();
            load.Show();
            Load(worlds[id]);
            curX = spawnX;
            curY = spawnY;
            RenderMap();
            load.Close();
        }
        private void Open_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }
        private void OpenWorld_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }
        private void Close_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            this.Close();
        }
        private void Close_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }
        private void JumpToSpawn_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            curX = spawnX;
            curY = spawnY;
            RenderMap();
        }
        private void Lighting_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            RenderMap();
        }
        private void Texture_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (UseTextures.IsChecked)
                UseTextures.IsChecked = false;
            else
                UseTextures.IsChecked = true;
            RenderMap();
        }

        private void Refresh_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Loading load = new Loading();
            load.Show();
            Load(currentWorld);
            RenderMap();
            load.Close();
        }

        private void Hilight_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ArrayList items = new ArrayList();
            for (int i = 0; i < tileInfo.Length; i++)
                items.Add(tileInfo[i].name);
            HilightWin h = new HilightWin(items);
            if (h.ShowDialog() == true)
            {
                hilight = (byte)(h.SelectedItem);
                isHilight = true;
                hiliteTimer.Start();
            }
        }

        private void HilightStop_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            isHilight = false;
            hiliteTimer.Stop();
            RenderMap();
        }
        private void IsHilighting(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = isHilight;
        }
        private void Save_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog();
            dlg.DefaultExt = ".png";
            dlg.Filter = "Png Image|*.png";
            dlg.Title = "Save Map Image";
            var result = dlg.ShowDialog();
            if (result == true)
            {
                byte[] pixels = new byte[tilesWide * tilesHigh * 4];

                render.Draw(tilesWide, tilesHigh, 0, 0, 1.0, pixels,
                    false, 0, 0, false, false);

                BitmapSource source = BitmapSource.Create(tilesWide, tilesHigh, 96.0, 96.0,
                    PixelFormats.Bgr32, null, pixels, tilesWide * 4);
                FileStream stream = new FileStream(dlg.FileName, FileMode.Create);
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                encoder.Save(stream);
                stream.Close();
            }

        }
        private void MapLoaded(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = loaded;
        }

        private void initWindow(object sender, EventArgs e)
        {
            HwndSource hwnd = HwndSource.FromVisual(Map) as HwndSource;

            render.Textures = new Textures(hwnd.Handle);
        }

       
    }
}