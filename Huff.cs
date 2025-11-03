// Bradford Arrington 2025
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

partial class Compressor
{
    const int END_OF_STREAM = 256;
    const int EOF = -1; // Define a custom EOF
	
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
    public static string Usage = "infile outfile [-d]\n\nSpecifying -d will dump the modeling data\n";

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

    public void CompressFile(FileStream input, BitFile output, int argc, string[] argv)
    {

        ulong[] counts = new ulong[256];
        for (int i = 0; i < counts.Length; i++)
        {
            counts[i] = (uint)(i + 1); // Initialize with some values
        }
		//List<ulong> counts = new List<ulong>();
		
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
        int rootNode;

        InputCounts(input, nodes);
		//PrintModel(nodes, null);
        rootNode = BuildTree(nodes);
		Console.WriteLine("model 2");
	    //PrintModel(nodes, null);
        if (argc > 0 && argv[0] == "-d")
            PrintModel(nodes, null);
        ExpandData(input, output, nodes, rootNode);
    }

    public void OutputCounts(BitFile output, Node[] nodes)
    {
        int first;
        int last= 256;
        int next = 1;
        int i;

        first = 0;
        while (first < 255 && nodes[first].Count == 0) {
          first++;
		}
	    first = 10;
		Console.WriteLine(" first {0} last {1} next {2}", first.ToString(), last.ToString(), next.ToString());
        for (; first < 256; first = next)
        {
          last = first + 1;
          for (; ; )
          {
                for (; last < 256; last++)
                {
                    if (nodes[last].Count == 0) {
                     Console.WriteLine("First");
						break;
					}
                }
                last--;
                for (next = last + 1; next < 256; next++) { 
					if (nodes[next].Count != 0) {
						Console.WriteLine("Second");
						break;
					}
				}
				if (next > 255){ 
					Console.WriteLine("break Next");
					break;
				}
				if ((next - last) > 3) {
					Console.WriteLine("break next - last");         
					break;
				}
				last = next;
          }

		  try { output.fileStream.WriteByte((byte)first); }
          catch { fatal_error("first: Error writing byte counts\n"); }
		  try { output.fileStream.WriteByte((byte)last); }
          catch { fatal_error("last: Error writing byte counts\n"); }
		  
		  //Console.WriteLine(" first {0} last {1}", first.ToString(), last.ToString());
          for (i = first; i <= last; i++)
          {
			//Console.WriteLine(" node {0} ", nodes[i].Count.ToString());
            try { output.fileStream.WriteByte((byte)nodes[i].Count); }
		    catch { fatal_error("Error writing byte counts\n"); }
          }
        }
		try { output.fileStream.WriteByte((byte)0); }
		catch { fatal_error("Line 119 Error writing byte counts\n");}
    }
    public void fatal_error(string statement)
    {
        Console.WriteLine($"\n{statement}");
        Environment.Exit(1);
    }

    public void InputCounts(BitFile input, Node[] nodes)
    {
        int first;
        int last;
        int i;
        int c;

        for (i = 0; i < 256; i++)
            nodes[i].Count = 0;
		
        if ((first = input.fileStream.ReadByte()) == EOF)
            fatal_error("first Error reading byte counts\n");

        if ((last = input.fileStream.ReadByte()) == EOF)
            fatal_error("last Error reading byte counts\n");

        for (; ; )
        {
            for (i = first; i <= last; i++)
                if ((c = input.fileStream.ReadByte()) == EOF) {
                    fatal_error("Error reading byte counts\n");
				}
                else {
                    nodes[i].Count = (uint)c;
				}
            if ((first = input.fileStream.ReadByte()) == EOF)
                fatal_error("Error reading byte counts\n");
            if (first == 0)
                break;
            if ((last = input.fileStream.ReadByte()) == EOF)
                fatal_error("Error reading byte counts\n");
        }
        nodes[END_OF_STREAM].Count = 1;
    }
    uint[] numbers = new uint[256];
    public void CountBytes(Stream input, ulong[] counts)
    {
        long inputMarker;
        int c;
		int cnt=0;

        const int ulongSize = 8;
        const int arrayLength = 256;

		inputMarker = input.Position;
		List<byte> allBytes = new List<byte>();
		// ReadByte returns the next byte as an int, or -1 at EOF.
        while ((c = input.ReadByte()) != -1)
        {
            allBytes.Add((byte)c);
			counts[c]++;  //BitConverter.ToUInt64(allBytes.ToArray(), i * ulongSize);
        }

        for (int i = 0; i < ulongsToProcess; i++)
        {
            // Convert 8 bytes into a single ulong using BitConverter.
            // It reads 8 bytes starting from the calculated index in the byte list.
            //counts[i] = BitConverter.ToUInt64(allBytes.ToArray(), i * ulongSize);
        }		
       /* while ((c = input.ReadByte()) != -1) 
		{ 
			Array.Resize(ref numbers, numbers.Length + 1);
			numbers[numbers.Length - 1] = (uint)c; 
			counts[c]++;
			cnt++;
		}*/
		for (int x = 0; x < 30; x++)
        {
             Console.WriteLine($"Byte {allBytes[x]}: {counts[x]}");
        }
	    
		Console.WriteLine("marker {0} cnt {1}", inputMarker.ToString(), cnt.ToString());

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

    public void PrintModel(Node[] nodes, Code[] codes)
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

    public void CompressData(Stream input, BitFile output, Code[] codes)
    {
        int c;
        while ((c = input.ReadByte()) != EOF)
            output.OutputBits((uint)codes[c].CodeValue, codes[c].CodeBits);
        output.OutputBits((uint)codes[END_OF_STREAM].CodeValue, codes[END_OF_STREAM].CodeBits);
    }

    public void ExpandData(BitFile input, Stream output, Node[] nodes, int rootNode)
    {
        int node;

        for (; ; )
        {
            node = rootNode;
            do
            {
                if (input.InputBit() == 1) {
                    node = nodes[node].Child1;
				}
                else { 
				  node = nodes[node].Child0; 
				}
			}
            while (node > END_OF_STREAM);
			
            if (node == END_OF_STREAM)
                break;
            try { 
				output.WriteByte((byte)node); 
			}
			catch {
                fatal_error("ExpandData: Error trying to write expanded byte to output");
			}
        }
		output.Close();
    }
}