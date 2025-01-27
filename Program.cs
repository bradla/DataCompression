// Bradford Arrington III Jan 2025
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.IO;

partial class Compressor
{
    public const int PACIFIER_COUNT = 2047;
    public class BitFile
    {
        public FileStream fileStream;
        public int rack;
        public byte mask;
        public int pacifierCounter;
        public bool isInput;

        public BitFile(string name, bool input)
        {
            isInput = input;
            fileStream = new FileStream(name, input ? FileMode.Open : FileMode.Create, input ? FileAccess.Read : FileAccess.Write);
            rack = 0;
            mask = 0x80;
            pacifierCounter = 0;
        }

        public static BitFile OpenOutputBitFile(string name)
        {
            return new BitFile(name, false);
        }

        public static BitFile OpenInputBitFile(string name)
        {
            return new BitFile(name, true);
        }

        public void CloseBitFile()
        {
            if (!isInput && mask != 0x80)
            {
                try
                {
                    fileStream.WriteByte((byte)rack);
                }
                catch
                {
                    throw new Exception("Fatal error in CloseBitFile!");
                    //Console.WriteLine("Error CloseBitFile");
                }
            }
            fileStream.Close();
        }

        public void OutputBit(int bit)
        {
            if (bit != 0)
            {
                rack |= mask;
            }
            mask >>= 1;
            if (mask == 0)
            {
                try
                {
                    fileStream.WriteByte((byte)rack);
                    if ((pacifierCounter++ & PACIFIER_COUNT) == 0)
                        Console.Write(".");
                }
                catch
                {
                    throw new Exception("Fatal error in OutputBit!");
                }
                rack = 0;
                mask = 0x80;
            }
        }

        public void OutputBits(uint code, int count)
        {
            uint maskCode = 1u << (count - 1);
            while (maskCode != 0)
            {
                if ((maskCode & code) != 0)
                {
                    rack |= mask;
                }
                mask >>= 1;
                if (mask == 0)
                {
                    try
                    {
                        fileStream.WriteByte((byte)rack);
                        if ((pacifierCounter++ & PACIFIER_COUNT) == 0)
                            Console.Write(".");
                    }
                    catch
                    {
                        throw new Exception("Fatal error in OutputBit!");
                    }
                    rack = 0;
                    mask = 0x80;
                }
                maskCode >>= 1;
            }
        }

        public int InputBit()
        {
            if (mask == 0x80)
            {
                int read = fileStream.ReadByte();
                if (read == -1)
                    throw new Exception("Fatal error in InputBit!");
                rack = read;
                if ((pacifierCounter++ & PACIFIER_COUNT) == 0)
                    Console.Write(".");
            }
            int value = rack & mask;
            mask >>= 1;
            if (mask == 0)
                mask = 0x80;
            return value != 0 ? 1 : 0;
        }
        private int _buffer;
        private int _bitCount;
        public int ReadBits(int bits)
        {
            int value = 0;
            while (bits > 0)
            {
                if (_bitCount == 0)
                {
                    int nextByte = fileStream.ReadByte();
                    if (nextByte == -1)
                    {
                        throw new EndOfStreamException();
                    }
                    _buffer = nextByte;
                    _bitCount = 8;
                }

                int shift = Math.Min(bits, _bitCount);
                value = (value << shift) | ((_buffer >> (_bitCount - shift)) & ((1 << shift) - 1));
                _bitCount -= shift;

                bits -= shift;
            }
            return value;
        }
        public uint InputBits(int bitCount)
        {
            uint maskCode = 1u << (bitCount - 1);
            uint returnValue = 0;
            while (maskCode != 0)
            {
                if (mask == 0x80)
                {
                    int read = fileStream.ReadByte();
                    if (read == -1)
                        throw new Exception("Fatal error in InputBit!");
                    rack = read;
                    if ((pacifierCounter++ & PACIFIER_COUNT) == 0)
                        Console.Write(".");
                }
                if ((rack & mask) != 0)
                {
                    returnValue |= maskCode;
                }
                maskCode >>= 1;
                mask >>= 1;
                if (mask == 0)
                    mask = 0x80;
            }
            return returnValue;
        }
        
    }
    const int END_OF_STREAM = 256;

    public struct Node
    {
        public uint Count { get; set; }
        public uint SavedCount { get; set; }
        public int Child0 { get; set; }
        public int Child1 { get; set; }
    }
    public struct Code
    {
        public ulong CodeValue;
        public int CodeBits;
    }

    public string CompressionName = "static order 0 model with Huffman coding";
    static string Usage = "infile outfile [-d]\n\nSpecifying -d will dump the modeling data\n";
    public int Putc(int ch, FileStream fileStream)
    {
        try
        {
            fileStream.WriteByte((byte)ch); // Write the character to the file
            return ch; // Return the written character as an integer
        }
        catch (Exception)
        {
            return -1; // Return -1 to indicate an error, similar to C's `putc`
        }
    }
    public static int Getc(FileStream fileStream)
    {
        if (fileStream == null)
            throw new ArgumentNullException(nameof(fileStream));

        if (!fileStream.CanRead)
            throw new IOException("File stream is not readable");

        int nextChar = fileStream.ReadByte();
        return nextChar;
    }

    public static void FilePrintBinary(Stream file, uint code, int bits)
    {
        uint mask = 1u << (bits - 1);
        while (mask != 0)
        {
            if ((code & mask) != 0)
                file.WriteByte((byte)'1');
            else
                file.WriteByte((byte)'0');
            mask >>= 1;
        }
    }
   
    public  void CompressFile(FileStream input, BitFile output, int argc, string[] argv)
    {

       ulong[] counts = new ulong[256];
       for (int i = 0; i < counts.Length; i++)
       {
          counts[i] = (ulong)(i + 1); // Initialize with some values
       }
       Node[] nodes = new Node[514];
       Code[] codes = new Code[257];
       CountBytes(input, counts);
       ScaleCounts(counts, nodes);
       OutputCounts(output, nodes);
       int rootNode = BuildTree(nodes);
       ConvertTreeToCode(nodes, codes, 0, 0, rootNode);
       if (argc > 0 && argv[0] == "-d")
            PrintModel(nodes, codes);
       CompressData(input, output, codes);
    }

    public void ExpandFile(BitFile input, FileStream output, int argc, string[] argv)
    {
        Node[] nodes = new Node[514];
        //InitializeNodes(nodes);

        int rootNode = 0;
        InputCounts(input, nodes);
        rootNode = BuildTree(nodes);
        if (argc > 0 && argv[0] == "-d")
            PrintModel(nodes,null);
        ExpandData(input, output, nodes, rootNode);
    }

    public  void OutputCounts(BitFile output, Node[] nodes)
    {
	    int first;
	    int last;
	    int next;
	    int i;

	    first = 0;
	    while ( first < 255 && nodes[ first ].Count == 0 )
          	    first++;

	    for ( ; first < 256 ; first = next ) {
	       last = first + 1;
	       for ( ; ; ) {
                 for ( ; last < 256 ; last++ )
		     if ( nodes[ last ].Count == 0 )
		       break;
	         last--;
	         for ( next = last + 1; next < 256 ; next++ )
		     if ( nodes[ next ].Count != 0 )
		        break;
                 if ( next > 255 )
		    break;
	         if ( ( next - last ) > 3 )
		    break;
	         last = next;
	       }

	       if ( Putc( first, output.fileStream ) != first )
	         fatal_error( "Error writing byte counts\n" );
	       if ( Putc( last, output.fileStream ) != last )
	         fatal_error( "Error writing byte counts\n" );
	       for ( i = first ; i <= last ; i++ ) {
                 if ( Putc( (int)nodes[ i ].Count, output.fileStream ) != nodes[ i ].Count )
		    fatal_error( "Error writing byte counts\n" );
	       }
            }
            if ( Putc( 0, output.fileStream ) != 0 )
	       fatal_error( "Error writing byte counts\n" );
    }
    public void fatal_error(string statement)
    {
        Console.WriteLine($"\n{statement}");
        Environment.Exit(1);
    }
    const int EOF = -1; // Define a custom EOF
    public void InputCounts(BitFile input, Node[] nodes)
    {
        int first;
        int last;
        int i;
        int c;

        for (i = 0; i < 256; i++)
            nodes[i].Count = 0;
        if ((first = Getc(input.fileStream)) == EOF)
            fatal_error("Error reading byte counts\n");
        if ((last = Getc(input.fileStream)) == EOF)
            fatal_error("Error reading byte counts\n");
        for (; ; )
        {
            for (i = first; i <= last; i++)
                if ((c = Getc(input.fileStream)) == EOF)
                    fatal_error("Error reading byte counts\n");
                else
                    nodes[i].Count = (uint) c;
            if ((first = Getc(input.fileStream)) == EOF)
                fatal_error("Error reading byte counts\n");
            if (first == 0)
                break;
            if ((last = Getc(input.fileStream)) == EOF)
                fatal_error("Error reading byte counts\n");
        }
        nodes[END_OF_STREAM].Count = 1;
    }

    public void CountBytes(FileStream input, ulong[] counts)
    {
        long inputMarker = input.Position;
        int c;
        while ((c = input.ReadByte()) != -1)
        {
            counts[c]++;
        }
        input.Seek(inputMarker, SeekOrigin.Begin);
    }

    public void ScaleCounts(ulong[] counts, Node[] nodes)
    {
        ulong maxCount = 0;
        for (int i = 0; i < 256; i++)
            if (counts[i] > maxCount)
                maxCount = counts[i];

        if (maxCount == 0)
        {
            counts[0] = 1;
            maxCount = 1;
        }

        maxCount /= 255;
        maxCount += 1;

        for (int i = 0; i < 256; i++)
        {
            nodes[i].Count = (uint)(counts[i] / maxCount);
            if (nodes[i].Count == 0 && counts[i] != 0)
                nodes[i].Count = 1;
        }
        nodes[END_OF_STREAM].Count = 1;
    }

    public int BuildTree(Node[] nodes)
    {
        int next_free;
        int i;
        int min_1;
        int min_2;
        nodes[513].Count = 0xFFFF;

        for (next_free = END_OF_STREAM + 1; ; next_free++)
        {
            min_1 = 513;
            min_2 = 513;
            for (i = 0; i < next_free; i++)
                if (nodes[i].Count != 0)
                {
                    if (nodes[i].Count < nodes[min_1].Count)
                    {
                        min_2 = min_1;
                        min_1 = i;
                    }
                    else if (nodes[i].Count < nodes[min_2].Count)
                        min_2 = i;
                }
            if (min_2 == 513)
                break;

            nodes[next_free].Count = nodes[min_1].Count + nodes[min_2].Count;
            nodes[min_1].SavedCount = nodes[min_1].Count;
            nodes[min_1].Count = 0;
            nodes[min_2].SavedCount = nodes[min_2].Count;
            nodes[min_2].Count = 0;
            nodes[next_free].Child0 = min_1;
            nodes[next_free].Child1 = min_2;
	    //Console.WriteLine($" Child0 {min_1} Child1 {min_2}");
        }

        next_free--;
        nodes[next_free].SavedCount = nodes[next_free].Count;
        //PrintModel(nodes,null);
        return next_free;
    }

    public void ConvertTreeToCode(Node[] nodes, Code[] codes, uint codeSoFar, int bits, int node)
    {
        if (node <= END_OF_STREAM)
        {
            codes[node].CodeValue = codeSoFar;
            codes[node].CodeBits = bits;
            return;
        }
        codeSoFar <<= 1;
        bits++;
        ConvertTreeToCode(nodes, codes, codeSoFar, bits, nodes[node].Child0);
        ConvertTreeToCode(nodes, codes, codeSoFar | 1, bits, nodes[node].Child1);
    }

    public  void PrintModel(Node[] nodes, Code[] codes)
    {
        for (int i = 0; i < 513; i++)
        {
            if (nodes[i].SavedCount != 0)
            {
                Console.Write("node=");
                PrintChar(i);
                Console.Write($"  count={nodes[i].SavedCount,3}  child_0=");
                PrintChar(nodes[i].Child0);
                Console.Write("  child_1=");
                PrintChar(nodes[i].Child1);
                if (codes != null && i <= END_OF_STREAM)
                {
                    Console.Write("  Huffman code=");
                    FilePrintBinary(Console.OpenStandardOutput(), (uint)codes[i].CodeValue, codes[i].CodeBits);
                }
                Console.WriteLine();
            }
        }
    }

    public static void PrintChar(int c)
    {
        if (c >= 0x20 && c < 127)
            Console.Write($"'{(char)c}'");
        else
            Console.Write($"{c,3}");
    }

    public  void CompressData(FileStream input, BitFile output, Code[] codes)
    {
        int c;
        while ((c = input.ReadByte()) != EOF)
            output.OutputBits( (uint)codes[c].CodeValue, codes[c].CodeBits);
        output.OutputBits((uint)codes[END_OF_STREAM].CodeValue, codes[END_OF_STREAM].CodeBits);
    }

    public void ExpandData(BitFile input, FileStream output, Node[] nodes, int rootNode)
    {
        int node;

        for (; ; )
        {
            node = rootNode;
            //Console.WriteLine($" root_node {rootNode}");
            //Console.WriteLine("1 child1 {0}", nodes[node].Child1.ToString());
            //Console.WriteLine("1 child0 {0}", nodes[node].Child0.ToString());
            do
            {
                var x = input.InputBit();
                //Console.WriteLine(" x {0}", x.ToString());
                if (x == 1)
                {
                    node = nodes[node].Child1;
                    //Console.WriteLine(" child1 {0}", node.ToString());
                }
                else {
                    node = nodes[node].Child0;
                    //Console.WriteLine(" child0 {0}", node.ToString());
                }
            } while (node > END_OF_STREAM);
            if (node == END_OF_STREAM)
                break;
            //Console.WriteLine(" node {0}",node.ToString());
            if ((Putc(node, output)) != node)
                fatal_error("Error trying to write expanded byte to output");
        }
    }
}

class Program
{
    private const string Usage = "in-file out-file";
//var remainingArgs = args.Length > 2 ? args[2..] : Array.Empty<string>();
    public static void Main(string[] args)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        if (args.Length < 2)
        {
            UsageExit(arguments[0]);
        }

        string[] remainingArgs = new string[args.Length - 2];
        Array.Copy(args, 2, remainingArgs, 0, remainingArgs.Length);
        var compressor = new Compressor();

        /*Compressor.BitFile output = Compressor.BitFile.OpenOutputBitFile(args[1]);
        FileStream input = new FileStream(args[0], FileMode.Open, FileAccess.Read);
        Console.WriteLine($"\nCompressing {args[0]} to {args[1]}");
        Console.WriteLine($"Using {compressor.CompressionName}\n");
        compressor.CompressFile(input, output, remainingArgs.Length, remainingArgs);
        output.CloseBitFile();
        input.Close();
        PrintRatios(args[0], args[1]);
        */
        Compressor.BitFile input = Compressor.BitFile.OpenInputBitFile(arguments[1]);
        FileStream output = new FileStream(arguments[2], FileMode.Create, FileAccess.Write);
        Console.WriteLine($"\nDecompressing {arguments[1]} to {arguments[2]}");
        Console.WriteLine($"Using {compressor.CompressionName}\n");
        
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

