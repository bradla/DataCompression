// Bradford Arrington 2025
using System;
using System.IO;
using System.Collections.Generic;

class Program
{
    public static void Main(string[] args)
    {
		using (new AdvancedPerformanceMonitor("Detail Operations"))
		{
			var compressor = new Compressor();
			string[] arguments = Environment.GetCommandLineArgs();
			if (args.Length < 2)
			{
			   string progName = arguments[0];
			   string shortName = progName;
			   int lastSlash = progName.LastIndexOf('\\');
			   if (lastSlash == -1)
					lastSlash = progName.LastIndexOf('/');
			   if (lastSlash == -1)
					lastSlash = progName.LastIndexOf(':');
			   if (lastSlash != -1)
					shortName = progName.Substring(lastSlash + 1);
			   int extension = shortName.LastIndexOf('.');
			   if (extension != -1)
					shortName = shortName.Substring(0, extension);
			   Console.WriteLine($"\nUsage:  {shortName} {Compressor.Usage}");
			   Environment.Exit(0);
			}

			string[] remainingArgs = new string[args.Length - 2];
			Array.Copy(args, 2, remainingArgs, 0, remainingArgs.Length);
			Compressor.BitFile input;
			FileStream output;
			using (new TimeMonitor("ReadFile"))
			{
				input = Compressor.BitFile.OpenInputBitFile(arguments[1]);
				output = new FileStream(arguments[2], FileMode.Create, FileAccess.Write);
			}
			Console.WriteLine($"\nDecompressing {arguments[1]} to {arguments[2]}");
			Console.WriteLine($"Using {compressor.CompressionName}\n");
			using (new TimeMonitor("ExpandFile and Close"))
			{
				compressor.ExpandFile(input, output, remainingArgs.Length, remainingArgs); 
			}
		}
    }
}
