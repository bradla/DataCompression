using System;
using System.IO;

partial class Compressor
{
    public string CompressionName = "Sound sample companding";
    public static string Usage = "infile outfile [n]\n\n n optionally sets the bits per sample\n\n";

    public void CompressFile(Stream input, BitFile output,  int argc, string[] argv)
    {
        int[] compress = new int[256];
        int steps;
    	int bits;
    	int value;
    	int i;
    	int j;
    	int c;
    	
	if (argv.Length > 0)
    		bits = int.Parse(argv[0]);
	else
    		bits = 4;
	Console.WriteLine($"Compressing using {bits} bits per sample...");
        
        steps = (1 << (bits - 1));
            
        // Write bits per sample and file length
        output.OutputBits((uint)bits, 8);
        output.OutputBits((uint)input.Length, 32); // get file lenght??
            
        // Build compression table
        for (i = steps; i > 0; i--)
        {
            value = (int)(128.0 * (Math.Pow(2.0, (double)i / steps) - 1.0) + 0.5);
            for ( j = value; j > 0; j--)
            {
                compress[j + 127] = i + steps - 1;
                compress[128 - j] = steps - i;
            }
        }
            
        // Compress data        
        while ((c = input.ReadByte()) != -1)
        {
            output.OutputBits((uint)compress[c], bits);
        }
    
        while (argc-- > 0)
    		Console.WriteLine($"Unknown argument: {argv[argv.Length - argc - 1]}");
    }

    public void ExpandFile(BitFile input, Stream output, int argc, string[] argv)
    {
    	int steps;
   	int bits;
   	int value;
    	int lastValue;
    	int i;
    	int c;
    	long count;
        int[] expand = new int[256];

	bits = (int)input.InputBits(8);
        Console.WriteLine($"Expanding using {bits} bits per sample...");
            
        steps = (1 << (bits - 1));
        lastValue = 0;
            
        // Build expansion table
        for (i = 1; i <= steps; i++)
        {
            value = (int)(128.0 * (Math.Pow(2.0, (double)i / steps) - 1.0) + 0.5);
            expand[steps + i - 1] = 128 + (value + lastValue) / 2;
            expand[steps - i] = 127 - (value + lastValue) / 2;
            lastValue = value;
        }
            
        // Read file length and expand data
        count = input.ReadBits(32);
        for (i = 0; i < count; i++)
        {
            c = (int)input.ReadBits(bits);
            output.WriteByte((byte)expand[c]);
        }
    }
}