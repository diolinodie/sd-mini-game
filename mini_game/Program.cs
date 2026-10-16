using BarRaider.SdTools;
using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace MiniGame
{
    class Program
    {
        static void Main(string[] args)
        {
            #if DEBUG
                var seconds = 10;
                while (!System.Diagnostics.Debugger.IsAttached && seconds > 0)
                {
                    Console.WriteLine($"Waiting for debugger to be attached ({seconds}s)...");
                    Thread.Sleep(1000);
                    seconds--;
                }
            #endif

            var currentDirectory = Directory.GetCurrentDirectory();

            //從Resources解出必要的檔案
            Assembly asm = Assembly.GetExecutingAssembly();
            string[] names = asm.GetManifestResourceNames();

            foreach (string resourceName in names)
            {
                if (!resourceName.Contains("MiniGame.Resources."))
                    continue;

                string fileName = Path.Combine(currentDirectory, resourceName.Replace("MiniGame.Resources.", ""));
                string directoryName = Path.GetDirectoryName(fileName);

                if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
                {
                    Directory.CreateDirectory(directoryName);
                }

                if (!File.Exists(fileName))
                {
                    Console.WriteLine(fileName);
                    using (Stream stream = asm.GetManifestResourceStream(resourceName))
                    using (FileStream fileStream = new FileStream(fileName, FileMode.CreateNew))
                    {
                        if (stream != null)
                        {
                            stream.CopyTo(fileStream);
                        }
                    }
                }
            }

            for (int i = 0; i < args.Length; i++)
            {
                Console.WriteLine($"args[{i}] = {args[i]}");
            }

            SDWrapper.Run(args);
        }
    }
}
