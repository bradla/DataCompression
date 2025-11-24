// Bradford Arrington 2025
using System;
using System.IO;
using System.Collections.Generic;

partial class Compressor
{
    public string CompressionName = "LZW 15 Bit Variable Rate Encoder";
    public static string Usage = "in-file out-file\n\n";
    private const int Bits = 15;
    private const int MaxCode = (1 << Bits) - 1;
    private const int TableSize = 35023;
    private const int TableBanks = (TableSize >> 8) + 1;
    private const int EndOfStream = 256;
    private const int BumpCode = 257;
    private const int FlushCode = 258;
    private const int FirstCode = 259;
    private const int Unused = -1;

    private readonly Dictionary<int, (int CodeValue, int ParentCode, char Character)>[] dict;
    private readonly char[] decodeStack = new char[TableSize];

    private int nextCode;
    private int currentCodeBits;
    private int nextBumpCode;

    public Compressor()
    {
        dict = new Dictionary<int, (int CodeValue, int ParentCode, char Character)>[TableBanks];
    }

    private  void InitializeStorage()
    {
        for (int i = 0; i < TableBanks; i++)
        {
            dict[i] = new Dictionary<int, (int CodeValue, int ParentCode, char Character)>(256);
        }
    }

    public  void InitializeDictionary()
    {
        for (int i = 0; i < TableSize; i++)
        {
            int bank = i >> 8;
            int index = i & 0xff;

            if (!dict[bank].ContainsKey(index))
            {
                dict[bank][index] = (Unused, -1, '\0');
            }
        }

        nextCode = FirstCode;
        Console.Write('F'); // Represents putc('F', stdout)
        currentCodeBits = 9;
        nextBumpCode = 511;
    }
    private  int FindChildNode(int parentCode, int childCharacter)
    {
        int index = (childCharacter << (Bits - 8)) ^ parentCode;
        int offset = (index == 0) ? 1 : TableSize - index;

        while (true)
        {
            int bank = index >> 8;
            int subIndex = index & 0xff;

            if (!dict[bank].ContainsKey(subIndex) || dict[bank][subIndex].CodeValue == Unused)
            {
                return index;
            }

            var entry = dict[bank][subIndex];
            if (entry.ParentCode == parentCode && entry.Character == (char)childCharacter)
            {
                return index;
            }

            if (index >= offset)
            {
                index -= offset;
            }
            else
            {
                index += TableSize - offset;
            }
        }
    }

    private  uint DecodeString(uint count, uint code)
    {
        while (code > 255)
        {
            int bank = (int)(code >> 8);
            int subIndex = (int)(code & 0xff);

            if (!dict[bank].TryGetValue(subIndex, out var entry))
            {
                throw new Exception("Invalid code during decoding.");
            }

            decodeStack[count++] = entry.Character;
            code = (uint)entry.ParentCode;
        }

        decodeStack[count++] = (char)code;
        return count;
    }
    public void CompressFile(Stream input, BitFile output, int argc, string[] argv)
    {
        int character;
        int stringCode;
        int index;

        // Initialize the dictionary and storage
        InitializeStorage();
        InitializeDictionary();

        // Read the first character
        stringCode = input.ReadByte();
        if (stringCode == -1) // Handle end-of-stream case
        {
            stringCode = EndOfStream;
        }

        // Process each character in the input stream
        while ((character = input.ReadByte()) != -1)
        {
            // Find or add the string in the dictionary
            index = FindChildNode(stringCode, character);

            if (dict[index >> 8].ContainsKey(index & 0xff) &&
                dict[index >> 8][index & 0xff].CodeValue != Unused)
            {
                stringCode = dict[index >> 8][index & 0xff].CodeValue;
            }
            else
            {
                // Add the new string to the dictionary
                int bank = index >> 8;
                int subIndex = index & 0xff;
                dict[bank][subIndex] = (nextCode++, stringCode, (char)character);

                // Output the code for the string
                output.OutputBits((uint)stringCode, currentCodeBits);

                // Update the current string to the current character
                stringCode = character;

                // Handle dictionary size limits
                if (nextCode > MaxCode)
                {
                    // Output the flush code and reinitialize the dictionary
                    output.OutputBits((uint)FlushCode, currentCodeBits);
                    InitializeDictionary();
                }
                else if (nextCode > nextBumpCode)
                {
                    // Output the bump code and increase the code size
                    output.OutputBits((uint)BumpCode, currentCodeBits);
                    currentCodeBits++;
                    nextBumpCode = (nextBumpCode << 1) | 1;
                    Console.Write('B'); // Pacifier character
                }
            }
        }

        // Output the final string and the end-of-stream code
        output.OutputBits((uint)stringCode, currentCodeBits);
        output.OutputBits((uint)EndOfStream, currentCodeBits);

        while (argc-- > 0)
            Console.WriteLine($"Unknown argument: {argv[argv.Length - argc - 1]}");
    }
    public void ExpandFile(BitFile input, Stream output, int argc, string[] argv)
    {
        uint newCode;
        uint oldCode;
        int character;
        uint count;

        InitializeStorage();

        while (argc-- > 0)
            Console.WriteLine($"Unknown argument: {argv[argv.Length - argc - 1]}");

        while (true)
        {
            InitializeDictionary();

            // Read the first code
            oldCode = (uint)input.InputBits(currentCodeBits);
            if (oldCode == EndOfStream)
            {
                return;
            }

            character = (int)oldCode;
            output.WriteByte((byte)oldCode);

            while (true)
            {
                // Read the next code
                newCode = (uint)input.InputBits(currentCodeBits);
                if (newCode == EndOfStream)
                {
                    return;
                }

                if (newCode == FlushCode)
                {
                    break;
                }

                if (newCode == BumpCode)
                {
                    currentCodeBits++;
                    Console.Write('B');
                    continue;
                }

                if (newCode >= nextCode)
                {
                    decodeStack[0] = (char)character;
                    count = DecodeString(1, oldCode);
                }
                else
                {
                    count = DecodeString(0, newCode);
                }

                character = decodeStack[count - 1];

                // Write decoded string to output
                while (count > 0)
                {
                    output.WriteByte((byte)decodeStack[--count]);
                }

                // Add the new entry to the dictionary
                int bank = (int)(nextCode >> 8);
                int subIndex = (int)(nextCode & 0xff);
                dict[bank][subIndex] = (nextCode++, (int)oldCode, (char)character);

                // Update old code
                oldCode = newCode;
            }
        }
    }
}
