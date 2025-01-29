// Bradford Arrington 2025
using System;
using System.IO;

public struct Symbol
{
    public ushort LowCount;
    public ushort HighCount;
    public ushort Scale;
}

public partial class Compressor
{
    private const int END_OF_STREAM = 256;
    private static short[] Totals = new short[258]; // The cumulative totals

    public string CompressionName = "Fixed order 0 model with arithmetic coding arith.cs";
    public static string Usage = "in-file out-file\n\n";

    ushort code;  // The present input code value
    ushort low;   // Start of the current code range
    ushort high;  // End of the current code range
    long underflowBits; // Number of underflow bits pending

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
    public void fatal_error(string statement)
    {
        Console.WriteLine($"\n{statement}");
        Environment.Exit(1);
    }
    public void CompressFile(Stream input, BitFile output, int argc, string[] argv)
    {
        int c;
        Symbol s = new Symbol();

        BuildModel(input, output.fileStream);
        InitializeArithmeticEncoder();

        while ((c = input.ReadByte()) != -1)
        {
            ConvertIntToSymbol(c, out s);
            EncodeSymbol(output, ref s);
        }

        ConvertIntToSymbol(END_OF_STREAM, out s);
        EncodeSymbol(output, ref s);
        FlushArithmeticEncoder(output);
        output.OutputBits(0, 16);

        while (argc-- > 0)
        {
            Console.WriteLine($"Unused argument: {argv[argc]}");
        }
    }

    public void ExpandFile(BitFile input, Stream output, int argc, string[] argv)
    {
        //Node[] nodes = new Node[514];
        Symbol s = new Symbol();
        int c;
        int count;

        InputCounts(input.fileStream);
        InitializeArithmeticDecoder(input);
        while (true)
        {
            GetSymbolScale(ref s);
            count = GetCurrentCount(ref s);
            c = ConvertSymbolToInt(count, ref s);
            if (c == END_OF_STREAM)
                break;
            RemoveSymbolFromStream(input, ref s);
            output.WriteByte((byte)c);
        }

        while (argc-- > 0)
        {
            Console.WriteLine($"Unused argument: {argv[argc]}");
        }
    }

    private void BuildModel(Stream input, FileStream output)
    {
        ulong[] counts = new ulong[256];
        byte[] scaledCounts = new byte[256];

        CountBytes(input, counts);
        ScaleCounts(counts, scaledCounts);
        OutputCounts(output, scaledCounts);
        BuildTotals(scaledCounts);
    }

    private void CountBytes(Stream input, ulong[] counts)
    {
        if (!input.CanSeek)
            throw new ArgumentException("Input stream must support seeking.");

        input.Seek(0, SeekOrigin.Begin);
        int c;
        for (int i = 0; i < 256; i++)
            counts[i] = 0;

        while ((c = input.ReadByte()) != -1)
        {
            counts[c]++;
        }

        input.Seek(0, SeekOrigin.Begin);
    }

    private void ScaleCounts(ulong[] counts, byte[] scaledCounts)
    {
        // Ensure each count fits into a single byte
        ulong maxCount = 0;
        for (int i = 0; i < 256; i++)
            if (counts[i] > maxCount)
                maxCount = counts[i];

        double scale = Math.Ceiling((double)maxCount / 256);
        for (int i = 0; i < 256; i++)
        {
            scaledCounts[i] = (byte)(counts[i] / (ulong)scale);
            if (scaledCounts[i] == 0 && counts[i] != 0)
                scaledCounts[i] = 1;
        }

        // Ensure the total is less than 16384
        int total = 1; // Initialize to 1 for END_OF_STREAM
        for (int i = 0; i < 256; i++)
            total += scaledCounts[i];

        if (total > (32767 - 256))
            scale = 4;
        else if (total > 16383)
            scale = 2;
        else
            return;

        for (int i = 0; i < 256; i++)
            scaledCounts[i] /= (byte)scale;
    }

    private void BuildTotals(byte[] scaledCounts)
    {
        Totals[0] = 0;
        for (int i = 0; i < END_OF_STREAM; i++)
            Totals[i + 1] = (short)(Totals[i] + scaledCounts[i]);
        Totals[END_OF_STREAM + 1] = (short)(Totals[END_OF_STREAM] + 1);
    }

    private void OutputCounts(FileStream output, byte[] scaledCounts)
    {
        int first;
        int last;
        int next;
        int i;

        first = 0;
        while (first < 255 && scaledCounts[first] == 0)
            first++;

        for (; first < 256; first = next)
        {
            last = first + 1;
            for (; ; )
            {
                for (; last < 256; last++)
                    if (scaledCounts[last] == 0)
                        break;
                last--;
                for (next = last + 1; next < 256; next++)
                    if (scaledCounts[next] != 0)
                        break;
                if (next > 255)
                    break;
                if ((next - last) > 3)
                    break;
                last = next;
            };

            if (Putc(first, output) != first)
                fatal_error("Error writing byte counts\n");
            if (Putc(last, output) != last)
                fatal_error("Error writing byte counts\n");
            for (i = first; i <= last; i++)
            {
                if (Putc(scaledCounts[i], output) !=
                    (int)scaledCounts[i])
                    fatal_error("Error writing byte counts\n");
            }
        }
        if (Putc(0, output) != 0)
            fatal_error("Error writing byte counts\n");

    }

    private void InputCounts(Stream input)
    {
        int first = input.ReadByte();
        if (first == -1)
            FatalError("Error reading byte counts");

        int last = input.ReadByte();
        if (last == -1)
            FatalError("Error reading byte counts");

        byte[] scaledCounts = new byte[256];
        for (int i = 0; i < 256; i++)
            scaledCounts[i] = 0;

        while (true)
        {
            for (int i = first; i <= last; i++)
            {
                int c = input.ReadByte();
                if (c == -1)
                    FatalError("Error reading byte counts");
                scaledCounts[i] = (byte)c;
            }

            first = input.ReadByte();
            if (first == -1)
                FatalError("Error reading byte counts");
            if (first == 0)
                break;

            last = input.ReadByte();
            if (last == -1)
                FatalError("Error reading byte counts");
        }

        BuildTotals(scaledCounts);
    }

    private void InitializeArithmeticEncoder()
    {
        low = 0;
        high = 0xFFFF;
        underflowBits = 0;
    }

    private void FlushArithmeticEncoder(BitFile output)
    {
        output.OutputBit((low & 0x4000));
        underflowBits++;
        while (underflowBits-- > 0)
            output.OutputBit((~low & 0x4000));
    }

    private void ConvertIntToSymbol(int c, out Symbol s)
    {
        s.Scale = (ushort)Totals[END_OF_STREAM + 1];
        s.LowCount = (ushort)Totals[c];
        s.HighCount = (ushort)Totals[c + 1];
    }

    private void GetSymbolScale(ref Symbol s)
    {
        s.Scale = (ushort)Totals[END_OF_STREAM + 1];
    }

    private int ConvertSymbolToInt(int count, ref Symbol s)
    {
        int c;
        for (c = END_OF_STREAM; count < Totals[c]; c--)
            ;
        s.HighCount = (ushort)Totals[c + 1];
        s.LowCount = (ushort)Totals[c];
        return c;
    }

    private void EncodeSymbol(BitFile output, ref Symbol s)
    {
        long range = (long)(high - low) + 1;
        ushort r = (ushort)range;
        ushort h = (ushort)((range * s.HighCount) / s.Scale - 1);
        high = (ushort)(low + h);
        //high = low + ((range * s.HighCount) / s.Scale - 1);
        //low = low + ((r * s.LowCount) / s.Scale);
        ushort l = (ushort)((range * s.LowCount) / s.Scale);
        low = (ushort)(low + l);

        while (true)
        {
            // If the highest bits match, output the bit
            if ((high & 0x8000) == (low & 0x8000))
            {
                output.OutputBit((high & 0x8000));
                while (underflowBits > 0)
                {
                    output.OutputBit((~high & 0x8000));
                    underflowBits--;
                }
            }
            // Check for underflow condition
            else if ((low & 0x4000) != 0 && (high & 0x4000) == 0)
            {
                underflowBits += 1;
                low &= 0x3FFF;
                high |= 0x4000;
            }
            else
            {
                break;
            }

            low <<= 1;
            high <<= 1;
            high |= 1;
        }
    }

    private void InitializeArithmeticDecoder(BitFile input)
    {
        code = 0;
        for (int i = 0; i < 16; i++)
        {
            code <<= 1;
            int bit = input.InputBit(); //? 1 : 0;
            code += (ushort)bit;
        }
        low = 0;
        high = 0xFFFF;
    }

    private short GetCurrentCount(ref Symbol s)
    {
        long range = (long)(high - low) + 1;
        short count = (short)((((long)(code - low) + 1) * s.Scale - 1) / range);
        return count;
    }

    private void RemoveSymbolFromStream(BitFile input, ref Symbol s)
    {
        long range = (long)(high - low) + 1;
        //high = low + (ushort)((range * s.HighCount) / s.Scale - 1);
        //low = low + (ushort)((range * s.LowCount) / s.Scale);
        ushort h = (ushort)((range * s.HighCount) / s.Scale - 1);
        high = (ushort)(low + h);
        ushort l = (ushort)((range * s.LowCount) / s.Scale);
        low = (ushort)(low + l);

        while (true)
        {
            // If the highest bits match, shift out the bits
            if ((high & 0x8000) == (low & 0x8000))
            {
                // No action needed in the decoder for matching bits
            }
            // Check for underflow condition
            else if (((low & 0x4000) != 0) && ((high & 0x4000) == 0))
            {
                code ^= 0x4000;
                low &= 0x3FFF;
                high |= 0x4000;
            }
            else
            {
                break;
            }

            low <<= 1;
            high <<= 1;
            high |= 1;
            code <<= 1;
            int bit = input.InputBit();// ? 1 : 0;
            code += (ushort)bit;
        }
    }

    private void FatalError(string message)
    {
        Console.Error.WriteLine(message);
        Environment.Exit(1);
    }
}
