// Bradford Arrington 2025
using System;
using System.IO;
using System.IO.Compression;

partial class Compressor
{
    public string CompressionName = "LZW 12 Bit Encoder";
    public static string Usage = "in-file out-file\n\n";
    private const int BITS = 12;
    private const int MAX_CODE = (1 << BITS) - 1;
    private const int TABLE_SIZE = 5021;
    private const int END_OF_STREAM = 256;
    private const int FIRST_CODE = 257;
    private const int UNUSED = -1;

    private struct DictionaryEntry
    {
        public int CodeValue;
        public int ParentCode;
        public char Character;
    }

    private static DictionaryEntry[] dict = new DictionaryEntry[TABLE_SIZE];
    private static char[] decodeStack = new char[TABLE_SIZE];
    public static uint FindChildNode(int parentCode, int childCharacter)
    {
        int index = (childCharacter << (BITS - 8)) ^ parentCode;
        int offset = index == 0 ? 1 : TABLE_SIZE - index;

        while (true)
        {
            if (dict[index].CodeValue == UNUSED)
            {
                return (uint)index;
            }

            if (dict[index].ParentCode == parentCode && dict[index].Character == (char)childCharacter)
            {
                return (uint)index;
            }

            index -= offset;
            if (index < 0)
            {
                index += TABLE_SIZE;
            }
        }
    }

    private static uint DecodeString(uint count, uint code)
    {
        while (code > 255)
        {
            decodeStack[count++] = dict[code].Character;
            code = (uint)dict[code].ParentCode;
        }

        decodeStack[count++] = (char)code;
        return count;
    }
    public void CompressFile(Stream input, BitFile output, int argc, string[] argv)
    {
        int nextCode = FIRST_CODE;
        int character;
        int stringCode;
        uint index;

        // Initialize dictionary
        for (int i = 0; i < TABLE_SIZE; i++)
        {
            dict[i].CodeValue = UNUSED;
        }

        // Read the first character
        stringCode = input.ReadByte();
        if (stringCode == -1) // EOF
        {
            stringCode = END_OF_STREAM;
        }

        // Process input
        while ((character = input.ReadByte()) != -1) // EOF
        {
            index = FindChildNode(stringCode, character);

            if (dict[index].CodeValue != UNUSED)
            {
                stringCode = dict[index].CodeValue;
            }
            else
            {
                if (nextCode <= MAX_CODE)
                {
                    dict[index].CodeValue = nextCode++;
                    dict[index].ParentCode = stringCode;
                    dict[index].Character = (char)character;
                }

                output.OutputBits((uint)stringCode, BITS);
                stringCode = character;
            }
        }

        // Write the last string and end-of-stream marker
        output.OutputBits((uint)stringCode, BITS);
        output.OutputBits((uint)END_OF_STREAM, BITS);

        while (argc-- > 0)
            Console.WriteLine($"Unknown argument: {argv[argv.Length - argc - 1]}");
    }

    public void ExpandFile(BitFile input, Stream output, int argc, string[] argv)
    {
        uint nextCode = FIRST_CODE;
        uint newCode;
        uint oldCode;
        int character;
        uint count;

        // Read the first code
        oldCode = (uint)input.InputBits(BITS);
        if (oldCode == END_OF_STREAM)
        {
            return;
        }

        character = (int)oldCode;
        output.WriteByte((byte)oldCode);

        // Process input
        while ((newCode = (uint)input.InputBits(BITS)) != END_OF_STREAM)
        {
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

            if (nextCode <= MAX_CODE)
            {
                dict[nextCode].ParentCode = (int)oldCode;
                dict[nextCode].Character = (char)character;
                nextCode++;
            }

            oldCode = newCode;
        }

        while (argc-- > 0)
            Console.WriteLine($"Unknown argument: {argv[argv.Length - argc - 1]}");
    }
}
