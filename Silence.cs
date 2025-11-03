using System;
using System.IO;

partial class Compressor
{
    public string CompressionName = "Silence compression";
    public static string Usage = "infile outfile\n";

    // Compression parameters
    private const int SILENCE_LIMIT = 4;
    private const int START_THRESHOLD = 5;
    private const int STOP_THRESHOLD = 2;
    private const byte SILENCE_CODE = 0xFF;
    
    private static bool IsSilence(byte c)
    {
        return (c > (0x7F - SILENCE_LIMIT) && c < (0x80 + SILENCE_LIMIT));
    }

    // Buffer parameters
    private const int BUFFER_SIZE = 8;
    private const int BUFFER_MASK = 7;

    public void CompressFile(Stream input, BitFile output, int argc, string[] argv)
    {
        byte[] lookAhead = new byte[BUFFER_SIZE];
        int index = 0;

        // Initialize look-ahead buffer
        for (int i = 0; i < BUFFER_SIZE; i++)
        {
            int b = input.ReadByte();
            lookAhead[i] = (b == -1) ? (byte)0 : (byte)b;
        }

        while (true)
        {
            if (lookAhead[index] == 0 && input.Position >= input.Length)
                break;

            if (SilenceRun(lookAhead, index))
            {
                int runLength = 0;
                do
                {
                    int nextByte = input.ReadByte();
                    lookAhead[index] = (nextByte == -1) ? (byte)0 : (byte)nextByte;
                    index = (index + 1) & BUFFER_MASK;
                        
                    if (++runLength == 255)
                    {
                        output.fileStream.WriteByte(SILENCE_CODE);
                        output.fileStream.WriteByte(255);
                        runLength = 0;
                    }
                } while (!EndOfSilence(lookAhead, index) && input.Position < input.Length);

                if (runLength > 0)
                {
                    output.fileStream.WriteByte(SILENCE_CODE);
                    output.fileStream.WriteByte((byte)runLength);
                }
            }

            // Output non-silence data
            if (lookAhead[index] == SILENCE_CODE)
            {
                // Avoid accidental match with silence code
                output.fileStream.WriteByte((byte)(SILENCE_CODE - 1));
            }
            else
            {
                output.fileStream.WriteByte(lookAhead[index]);
            }

                // Read next byte into buffer
            int newByte = input.ReadByte();
            lookAhead[index] = (newByte == -1) ? (byte)0 : (byte)newByte;
            index = (index + 1) & BUFFER_MASK;
        }
    }

    public void ExpandFile(BitFile input, Stream output, int argc, string[] argv)
    {
        int c;
        while ((c = input.fileStream.ReadByte()) != -1)
        {
            if (c == SILENCE_CODE)
            {
                int runCount = input.fileStream.ReadByte();
                if (runCount == -1) break;
                    
                for (int i = 0; i < runCount; i++)
                {
                    output.WriteByte(0x80); // Middle silence value
                }
            }
            else
            {
                output.WriteByte((byte)c);
            }
        }
    }

    private static bool SilenceRun(byte[] buffer, int index)
    {
        for (int i = 0; i < START_THRESHOLD; i++)
        {
            if (!IsSilence(buffer[(index + i) & BUFFER_MASK]))
                return false;
        }
        return true;
    }

    private static bool EndOfSilence(byte[] buffer, int index)
    {
        for (int i = 0; i < STOP_THRESHOLD; i++)
        {
            if (IsSilence(buffer[(index + i) & BUFFER_MASK]))
                return false;
        }
        return true;
    }
}