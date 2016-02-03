using DreamEdit.DMI;
using FreeImageAPI;
using FreeImageAPI.Metadata;
using Hjg.Pngcs;
using Hjg.Pngcs.Chunks;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using Color = System.Drawing.Color;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace SS13to64x.DMI
{
    [Serializable]
    public class DmiImage
    {
        public string DmiName = "";
        public List<DMIState> States = new List<DMIState>();
        public int StateWidth = 32;
        public int StateHeight = 32;

        private int _pixelX = 0;
        private int _pixelY = 0;

        public DmiImage(string file)
        {
            DmiName = Path.GetFileNameWithoutExtension(file);
            using (var stream = File.OpenRead(file))
            {
                var imageData = new FreeImageBitmap(stream);
                var lines = new Queue<String>(imageData.Metadata.List[0].List[0].Value.ToString().Split('\n'));
                var start = lines.Dequeue();
                if (start != "# BEGIN DMI")
                    throw new Exception("NOT DMI");
                ReadVersion(lines);
                while (lines.Count > 0)
                {
                    var line = lines.Dequeue();
                    if (line == "# END DMI")
                        break;
                    if (line.StartsWith("state ="))
                    {
                        ReadState(GetValue(line), lines);
                    }
                }
                _pixelY = 0;
                foreach (var state in States)
                {
                    GetFrames(state, imageData);
                }
            }
        }

        public override string ToString()
        {
            return DmiName;
        }

        private void GetFrames(DMIState state, FreeImageBitmap img)
        {
            for (int i = 0; i < state.Frames; i++)
            {
                int[] dirs = { Directions.SOUTH, Directions.NORTH, Directions.EAST, Directions.WEST, Directions.SOUTHEAST, Directions.SOUTHWEST, Directions.NORTHEAST, Directions.NORTHWEST };
                var frame = new DMIFrame(state.GetDelay(i));
                for (int j = 0; j < state.Dir; j++)
                {
                    int dir = dirs[j];

                    if (_pixelX >= img.Width)
                    {
                        _pixelX = 0;
                        _pixelY += StateHeight;
                    }

                    Bitmap frameBitmap;
                    frameBitmap = img.Copy(new Rectangle(_pixelX, _pixelY, StateWidth, StateHeight)).ToBitmap();
                    frame.Add(new DMIImageData(frameBitmap, dir));
                    _pixelX += StateWidth;
                }
                state.Add(frame);
            }
        }

        private void ReadState(string name, Queue<string> lines)
        {
            var dirs = int.Parse(GetValue(lines.Dequeue()));
            var frames = int.Parse(GetValue(lines.Dequeue()));

            var delay = new List<float>();
            var rewind = 0;
            if (lines.Peek().Contains("delay"))
                delay = GetIntList(lines.Dequeue());
            if (lines.Peek().Contains("rewind"))
                rewind = int.Parse(GetValue(lines.Dequeue()));
            var state = new DMIState(name, dirs, frames, delay, rewind);
            States.Add(state);
        }

        private List<float> GetIntList(string dequeue)
        {
            var arr = GetValue(dequeue).Split(',');
            var list = new List<float>();
            foreach (var s in arr)
            {
                list.Add(float.Parse(s));
            }
            return list;
        }

        private void ReadVersion(Queue<string> lines)
        {
            var version = GetValue(lines.Dequeue());
            if (lines.Peek().Contains("width"))
            {
                StateWidth = int.Parse(GetValue(lines.Dequeue()));
                StateHeight = int.Parse(GetValue(lines.Dequeue()));
            }
        }

        private static string GetValue(string str)
        {
            str = str.Substring(str.IndexOf('=') + 2);
            if (str.StartsWith("\"") && str.EndsWith("\""))
                str = str.Substring(1, str.Length - 2);
            return str;
        }

        public static void Create(DmiImage dmi, string path)
        {
            var builder = new StringBuilder("# BEGIN DMI\n");

            builder.Append("version = 4.0\n");
            builder.Append("\twidth = " + dmi.StateWidth + "\n");
            builder.Append("\theight = " + dmi.StateHeight + "\n");

            var totalImages = dmi.States.Sum(x => x.GetFrames().Sum(y => y.GetImages().Count));
            var xY = Math.Min(10, totalImages);
            var totalWidth = (dmi.StateWidth * xY);
            var totalHeight = dmi.StateHeight * (int)Math.Ceiling(totalImages / (float)xY);
            int pixelX = 0;
            int pixelY = totalHeight - 1;
            var img = new FreeImageBitmap(totalWidth, totalHeight, PixelFormat.Format32bppPArgb);
            img.FillBackground(Color.FromArgb(0, 0, 0, 0));

            foreach (var state in dmi.States)
            {
                builder.AppendFormat("state = \"{0}\"\n", state.Name);
                builder.AppendFormat("\tdirs = {0}\n", state.Dir);
                builder.AppendFormat("\tframes = {0}\n", state.Frames);
                if (state.HasDelay)
                    builder.AppendFormat("\tdelay = {0}\n", state.GetDelayString);
                if (state.Rewind > 0)
                    builder.AppendFormat("\trewind = {0}\n", state.Rewind);
                foreach (var frame in state.GetFrames())
                {
                    foreach (var image in frame.GetImages())
                    {
                        for (int x = 0; x < dmi.StateWidth; x++)
                        {
                            for (int y = 0; y < dmi.StateHeight; y++)
                            {
                                img.SetPixel(pixelX + x, pixelY - y, image.Bitmap.GetPixel(x, y));
                            }
                        }
                        pixelX += dmi.StateWidth;
                        if (pixelX >= totalWidth)
                        {
                            pixelY -= dmi.StateHeight;
                            pixelX = 0;
                        }
                    }
                }
            }
            builder.AppendLine("# END DMI");

            if (!Directory.Exists(Path.GetDirectoryName(path)))
                Directory.CreateDirectory(Path.GetDirectoryName(path));
            img.Save(path, FREE_IMAGE_FORMAT.FIF_PNG, FREE_IMAGE_SAVE_FLAGS.PNG_Z_DEFAULT_COMPRESSION);

            // Work around because FREEIMAGE saves metatags as unicode.
            AddMetadata(path, "Description", builder.ToString());
        }

        public static void AddMetadata(String origFilename, Dictionary<string, string> data)
        {
            String destFilename = Path.GetRandomFileName();
            PngReader pngr = FileHelper.CreatePngReader(origFilename); 
            PngWriter pngw = FileHelper.CreatePngWriter(destFilename, pngr.ImgInfo, true); 

            int chunkBehav = ChunkCopyBehaviour.COPY_ALL_SAFE; // tell to copy all 'safe' chunks
            pngw.CopyChunksFirst(pngr, chunkBehav);          // copy some metadata from reader
            foreach (string key in data.Keys)
            {
                PngChunk chunk = pngw.GetMetadata().SetText(key, data[key], true, true);
                chunk.Priority = true;
            }

            int channels = pngr.ImgInfo.Channels;
            if (channels < 3)
                throw new Exception("This example works only with RGB/RGBA images");
            for (int row = 0; row < pngr.ImgInfo.Rows; row++)
            {
                ImageLine l1 = pngr.ReadRowInt(row); // format: RGBRGB... or RGBARGBA...
                pngw.WriteRow(l1, row);
            }
            pngw.CopyChunksLast(pngr, chunkBehav); // metadata after the image pixels? can happen
            pngw.End(); // dont forget this
            pngr.End();
            File.Delete(origFilename);
            File.Move(destFilename, origFilename);
        }

        public static void AddMetadata(String origFilename, string key, string value)
        {
            var data = new Dictionary<string, string>();
            data.Add(key, value);
            AddMetadata(origFilename, data);
        }

        private static int PowerOfTwo(int v)
        {
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            v++;
            return v;
        }
    }

    [Serializable]
    public class DMIState
    {
        private string _name;
        private int _dir;
        private int _frames;
        private List<float> _delay;
        private int _rewind;
        private List<DMIFrame> _framesData = new List<DMIFrame>();

        public DMIState(string name, int dir, int frames, List<float> delay, int rewind = 0)
        {
            Name = name;
            _dir = dir;
            _frames = frames;
            _delay = delay;
            Rewind = rewind;
        }

        public int Frames { get { return _frames; } }

        public int Dir
        {
            get { return _dir; }
        }

        public string Name
        {
            get { return _name; }
            set { _name = value; }
        }

        public bool HasDelay
        {
            get { return _delay.Count != 0; }
        }

        public string GetDelayString
        {
            get
            {
                if (_delay.Count == 1)
                    return _delay.ToString();
                var str = "";
                for (int i = 0; i < _delay.Count; i++)
                {
                    str += _delay[i];
                    if (i != _delay.Count - 1)
                        str += ",";
                }
                return str;
            }
        }

        public int Rewind
        {
            get { return _rewind; }
            set { _rewind = value; }
        }

        public float GetDelay(int i)
        {
            if (_delay.Count > i)
                return _delay[i];
            if (_delay.Count >= 1)
                return _delay[0];
            return 0;
        }

        public override string ToString()
        {
            return Name;
        }

        public void Add(DMIFrame dmiFrame)
        {
            _framesData.Add(dmiFrame);
        }

        public List<DMIFrame> GetFrames()
        {
            return _framesData;
        }
    }

    [Serializable]
    public class DMIFrame
    {
        private float _delay;
        private List<DMIImageData> _images = new List<DMIImageData>();

        public DMIFrame(float delay)
        {
            _delay = delay;
        }

        public void Add(DMIImageData data)
        {
            _images.Add(data);
        }

        public List<DMIImageData> GetImages()
        {
            return _images;
        }
    }

    [Serializable]
    public class DMIImageData
    {
        [NonSerialized]
        private Bitmap _bitmap;

        private int _dir;

        public DMIImageData(Bitmap bitmap, int dir)
        {
            _bitmap = bitmap;
            _dir = dir;
        }

        public int Dir
        {
            get { return _dir; }
        }

        public Bitmap Bitmap
        {
            get { return _bitmap; }
            set { _bitmap = value; }
        }

        public void Save(string imgPath)
        {
            Bitmap.Save(imgPath);
        }
    }
}