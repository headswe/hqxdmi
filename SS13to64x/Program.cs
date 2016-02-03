using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using log4net;
using NetSerializer;
using SS13to64x.DMI;

namespace SS13to64x
{
    static class Program
    {
        private const string Datafile = "dmi_info.dat";

        private static bool _parrallelEnabled;

        private static ILog _log;

        private static bool use4X = false;

        static void Main(string[] args)
        {
            log4net.Config.XmlConfigurator.Configure();
            _log = LogManager.GetLogger("Main");




            if (args.Contains("-help") || args.Contains("-?"))
            {
                PrintHelp();
                return;
            }



            if (args.Length < 2)
            {
                Console.WriteLine("Not enough arguments, two required.. use -help");
                return;
            }

            var inputFolder = args[0];
            var outputFolder = args[1];

            if (args.Contains("-p"))
                _parrallelEnabled = true;

            if (args.Contains("-4x"))
                use4X = true;


            var files = Directory.GetFiles(inputFolder, "*.dmi", SearchOption.AllDirectories).ToList();

            files = Generate(files, inputFolder, outputFolder);


            if(_parrallelEnabled)
                Parallel.ForEach(files, file => Rebuild(file, outputFolder));
            else
            {
                foreach (var file in files)
                {
                    Rebuild(file, outputFolder);
                }
            }


        }

        private static void PrintHelp()
        {
            Console.WriteLine("Mass DMI Rescaler");
            Console.WriteLine("Created by Head for Baystation ( Sebastian Broberg )");
            Console.WriteLine("########################");
            Console.WriteLine("Basic Usage: dmi_hqx.exe inputfolder outputfolder");
            Console.WriteLine("Addtional Arguments");
            Console.WriteLine("\t-p : Parallel processing enabled");
            Console.WriteLine("\t-4x : Set program to use HQx4 instead of x2");
            Console.ReadKey(true);
        }

        private static void Rebuild(string file, string outPath)
        {
            var ser =
                new Serializer(new List<Type>
                {
                    typeof (DmiImage),
                    typeof (DMIState),
                    typeof (DMIFrame),
                    typeof (DMIImageData)
                });

            var path = Path.GetDirectoryName(file);
            var relPath = path.Replace((outPath+"\\raw"), "");
            if (relPath.StartsWith("\\"))
                relPath = relPath.Substring(1);
            DmiImage dmi = null;

            try
            {
                using (var stream = File.OpenRead(file))
                {
                    dmi = (DmiImage)ser.Deserialize(stream);
                }
            }
            catch (Exception e)
            {
                _log.Error("Error during rebuild",e);
                throw;
            }
            if (!use4X)
            {
                dmi.StateHeight = dmi.StateHeight*2;
                dmi.StateWidth = dmi.StateWidth*2;
            }
            else
            {
                dmi.StateHeight = dmi.StateHeight * 4;
                dmi.StateWidth = dmi.StateWidth * 4;
            }
            var stateIndex = 0;
            foreach (var state in dmi.States)
            {
                var statePath = Path.Combine(path, stateIndex.ToString());
                var frameIndex = 0;
                foreach (var frame in state.GetFrames())
                {
                    var framePath = Path.Combine(statePath, frameIndex.ToString());
                    foreach (var image in frame.GetImages())
                    {
                        var imagePath = Path.Combine(framePath, image.Dir.ToString() + ".png");
                        if(File.Exists(imagePath))
                            image.Bitmap = new Bitmap(imagePath);
                        else
                        {
                            Console.WriteLine("File {0} not found!",imagePath);
                        }
                    }
                    frameIndex++;
                }
                stateIndex++;
            }
            DmiImage.Create(dmi, Path.Combine(outPath, "processed", relPath + ".dmi"));
        }

        private static void AskContinue()
        {
            throw new NotImplementedException();
        }

        static bool IsPowerOfTwo(int x)
        {
            return (x != 0) && ((x & (x - 1)) == 0);
        }
        private static List<String> Generate(IEnumerable<string> files, string inputFolder, string outputFolder)
        {
            outputFolder = Path.Combine(outputFolder, "raw");
            var processedFiles = new List<String>();
            if (_parrallelEnabled)
            {
                var bag = new ConcurrentBag<string>();
                Parallel.ForEach(files, file =>
                {
                    var f = ExtractDMI(inputFolder, outputFolder, file);
                    if(!String.IsNullOrEmpty(f))
                        bag.Add(f);
                });
                processedFiles = bag.ToList();
            }
            else
            {
                processedFiles.AddRange(files.Select(file => ExtractDMI(inputFolder, outputFolder, file)).Where(f => !String.IsNullOrEmpty(f)));
            }
            return processedFiles;
        }

        private static string ExtractDMI(string input, string outPath, string file)
        {
            var ser =
                new Serializer(new List<Type> {typeof (DmiImage), typeof (DMIState), typeof (DMIFrame), typeof (DMIImageData)});
            DmiImage dmi = null;
            try
            {
                dmi = new DmiImage(file);
            }
            catch (Exception e)
            {
                _log.Error("Error during extraction",e);
                return null;
            }
            // only parse power of two
            if (!IsPowerOfTwo(dmi.StateHeight) && !IsPowerOfTwo(dmi.StateWidth))
                return null;


            var oPath = Path.Combine(outPath, Path.GetDirectoryName(file.Replace(input + "\\", "")), dmi.DmiName);
            if (!Directory.Exists(oPath))
                Directory.CreateDirectory(oPath);


            using (var stream = File.Create(Path.Combine(oPath, Datafile)))
            {
                ser.Serialize(stream, dmi);
            }


            var stateIndex = 0;
            foreach (var dmiState in dmi.States)
            {
                var statePath = Path.Combine(oPath, stateIndex.ToString());
                if (!Directory.Exists(statePath))
                    Directory.CreateDirectory(statePath);
                int frameIndex = 0;
                foreach (var frame in dmiState.GetFrames())
                {
                    var framePath = Path.Combine(statePath, frameIndex.ToString());
                    if (!Directory.Exists(framePath))
                        Directory.CreateDirectory(framePath);
                    foreach (var image in frame.GetImages())
                    {
                        var imgPath = Path.Combine(framePath, image.Dir + ".png");
                        var bitmap = !use4X ? hqx.HqxSharp.Scale2(image.Bitmap) : hqx.HqxSharp.Scale4(image.Bitmap);
                        bitmap.Save(imgPath);
                    }
                    frameIndex++;
                }
                stateIndex++;
            }
            _log.InfoFormat("Extracted {0}",file);
            return Path.Combine(oPath, Datafile);
        }
    }
}
