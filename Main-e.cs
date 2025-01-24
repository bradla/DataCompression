using System;
using System.IO;
using System.Collections.Generic;

class Program
{
    private const string Usage = "in-file out-file";

    public static void Main(string[] args)
    {
    	if (args.Length < 2)
	  UsageExit(args[0]);
        string[] remainingArgs = new string[args.Length - 2];
	Array.Copy(args, 2, remainingArgs, 0, remainingArgs.Length);
	var compressor = new Compressor();

	Compressor.BitFile input = Compressor.BitFile.OpenInputBitFile(args[0]);
	FileStream output = new FileStream(args[1], FileMode.Create, FileAccess.Write);
	Console.WriteLine($"\nDecompressing {args[0]} to {args[1]}");
	Console.WriteLine($"Using {compressor.CompressionName}\n");
	//var remainingArgs = args.Length > 2 ? args[2..] : Array.Empty<string>();
	compressor.ExpandFile(input, output, remainingArgs.Length, remainingArgs);
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
}
