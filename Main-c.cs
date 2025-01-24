using System;
using System.IO;
using System.Collections.Generic;

class Program
{
    private const string Usage = "in-file out-file";

    public static void Main(string[] args)
    {
	string[] arguments = Environment.GetCommandLineArgs();
	if (args.Length < 2){
	  UsageExit(arguments[0]);
        }

	string[] remainingArgs = new string[args.Length - 2];
	Array.Copy(args, 2, remainingArgs, 0, remainingArgs.Length);
	var compressor = new Compressor();

        Compressor.BitFile output = Compressor.BitFile.OpenOutputBitFile(args[1]);
        FileStream input = new FileStream(args[0], FileMode.Open, FileAccess.Read);
        Console.WriteLine($"\nCompressing {args[0]} to {args[1]}");
	Console.WriteLine($"Using {compressor.CompressionName}\n");
	compressor.CompressFile(input, output, remainingArgs.Length, remainingArgs);
	output.CloseBitFile();
	input.Close();
	PrintRatios(args[0], args[1]);
    }

    public static void UsageExit(string progName)
    {
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
        Console.WriteLine($"\nUsage:  {shortName} {Usage}");
        Environment.Exit(0);
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
