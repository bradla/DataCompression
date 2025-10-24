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
			Compressor.BitFile output;
			FileStream input;
			using (new TimeMonitor("ReadFile"))
			{
				Compressor.BitFile output = Compressor.BitFile.OpenOutputBitFile(args[1]);
				FileStream input = new FileStream(args[0], FileMode.Open, FileAccess.Read);
			}
			Console.WriteLine($"\nCompressing {args[0]} to {args[1]}");
			Console.WriteLine($"Using {compressor.CompressionName}\n");
		    using (new TimeMonitor("CompressFile and Close"))
			{
				compressor.CompressFile(input, output, remainingArgs.Length, remainingArgs);
				output.CloseBitFile();
				input.Close();
			}
			PrintRatios(args[0], args[1]);
		}
    }

    public static long FileSize(string fileName)
    {
        try
        {
            var fileInfo = new FileInfo(fileName);
            return fileInfo.Length;
        }
        catch
        {
            return 0L;
        }
    }

    public static void PrintRatios(string inputFilePath, string outputFilePath)
    {
        long inputSize = FileSize(inputFilePath);
        if (inputSize == 0)
        {
            inputSize = 1;
        }

        long outputSize = FileSize(outputFilePath);
        int ratio = 100 - (int)(outputSize * 100L / inputSize);

        Console.WriteLine($"\nInput bytes:        {inputSize}");
        Console.WriteLine($"Output bytes:       {outputSize}");
        Console.WriteLine($"Compression ratio:  {ratio}%");
    }
}
