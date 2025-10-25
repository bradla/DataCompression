using System;
using System.IO;


partial class Compressor
{
    // Constants
    public const int ROWS = 200;
    public const int COLS = 320;
    public const int N = 8;

    // Global data
    private byte[,] PixelStrip = new byte[N, COLS];
    private double[,] C = new double[N, N];
    private double[,] Ct = new double[N, N];
    private int InputRunLength = 0;
    private int OutputRunLength = 0;
    private int[,] Quantum = new int[N, N];

    // Zigzag pattern
    private readonly (int row, int col)[] ZigZag = new (int, int)[N * N]
    {
            (0, 0), (0, 1), (1, 0), (2, 0), (1, 1), (0, 2), (0, 3), (1, 2),
            (2, 1), (3, 0), (4, 0), (3, 1), (2, 2), (1, 3), (0, 4), (0, 5),
            (1, 4), (2, 3), (3, 2), (4, 1), (5, 0), (6, 0), (5, 1), (4, 2),
            (3, 3), (2, 4), (1, 5), (0, 6), (0, 7), (1, 6), (2, 5), (3, 4),
            (4, 3), (5, 2), (6, 1), (7, 0), (7, 1), (6, 2), (5, 3), (4, 4),
            (3, 5), (2, 6), (1, 7), (2, 7), (3, 6), (4, 5), (5, 4), (6, 3),
            (7, 2), (7, 3), (6, 4), (5, 5), (4, 6), (3, 7), (4, 7), (5, 6),
            (6, 5), (7, 4), (7, 5), (6, 6), (5, 7), (6, 7), (7, 6), (7, 7)
    };

    public string CompressionName => "DCT compression";
    public static string Usage => "infile outfile [quality]\nQuality from 0-25";

    private int ROUND(double a)
    {
        return (a < 0) ? (int)(a - 0.5) : (int)(a + 0.5);
    }

    public void Initialize(int quality)
    {
        double pi = Math.Atan(1.0) * 4.0;

        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < N; j++)
            {
                Quantum[i, j] = 1 + ((1 + i + j) * quality);
            }
        }

        OutputRunLength = 0;
        InputRunLength = 0;

        for (int j = 0; j < N; j++)
        {
            C[0, j] = 1.0 / Math.Sqrt(N);
            Ct[j, 0] = C[0, j];
        }

        for (int i = 1; i < N; i++)
        {
            for (int j = 0; j < N; j++)
            {
                C[i, j] = Math.Sqrt(2.0 / N) *
                          Math.Cos(pi * (2 * j + 1) * i / (2.0 * N));
                Ct[j, i] = C[i, j];
            }
        }
    }

    public void ReadPixelStrip(Stream input, byte[,] strip)
    {
        for (int row = 0; row < N; row++)
        {
            for (int col = 0; col < COLS; col++)
            {
                int b = input.ReadByte();
                if (b == -1)
                    throw new EndOfStreamException("Error reading input grey scale file");
                strip[row, col] = (byte)b;
            }
        }
    }

    public int InputCode(BitFile inputFile)
    {
        if (InputRunLength > 0)
        {
            InputRunLength--;
            return 0;
        }

        int bitCount = (int)inputFile.InputBits(2);
        if (bitCount == 0)
        {
            InputRunLength = (int)inputFile.InputBits(4);
            return 0;
        }

        if (bitCount == 1)
            bitCount = (int)inputFile.InputBits(1) + 1;
        else
            bitCount = (int)inputFile.InputBits(2) + (bitCount << 2) - 5;

        int result = (int)inputFile.InputBits(bitCount);
        if ((result & (1 << (bitCount - 1))) != 0)
            return result;
        return result - (1 << bitCount) + 1;
    }

    public void ReadDCTData(BitFile inputFile, int[,] inputData)
    {
        for (int i = 0; i < N * N; i++)
        {
            var (row, col) = ZigZag[i];
            inputData[row, col] = InputCode(inputFile) * Quantum[row, col];
        }
    }

    public void OutputCode(BitFile outputFile, int code)
    {
        if (code == 0)
        {
            OutputRunLength++;
            return;
        }

        if (OutputRunLength != 0)
        {
            while (OutputRunLength > 0)
            {
                outputFile.OutputBits(0, 2);
                if (OutputRunLength <= 16)
                {
                    outputFile.OutputBits((uint)(OutputRunLength - 1), 4);
                    OutputRunLength = 0;
                }
                else
                {
                    outputFile.OutputBits(15, 4);
                    OutputRunLength -= 16;
                }
            }
        }

        int absCode = Math.Abs(code);
        int topOfRange = 1;
        int bitCount = 1;

        while (absCode > topOfRange)
        {
            bitCount++;
            topOfRange = ((topOfRange + 1) * 2) - 1;
        }

        if (bitCount < 3)
            outputFile.OutputBits((uint)(bitCount + 1), 3);
        else
            outputFile.OutputBits((uint)(bitCount + 5), 4);

        if (code > 0)
            outputFile.OutputBits((uint)code, bitCount);
        else
            outputFile.OutputBits((uint)(code + topOfRange), bitCount);
    }

    public void WriteDCTData(BitFile outputFile, int[,] outputData)
    {
        for (int i = 0; i < N * N; i++)
        {
            var (row, col) = ZigZag[i];
            double result = outputData[row, col] / (double)Quantum[row, col];
            OutputCode(outputFile, ROUND(result));
        }
    }

    public void WritePixelStrip(Stream output, byte[,] strip)
    {
        for (int row = 0; row < N; row++)
        {
            for (int col = 0; col < COLS; col++)
            {
                output.WriteByte(strip[row, col]);
            }
        }
    }

    public void ForwardDCT(byte[,] input, int[,] output)
    {
        double[,] temp = new double[N, N];
        double temp1;

        // MatrixMultiply(temp, input, Ct);
        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < N; j++)
            {
                temp[i, j] = 0.0;
                for (int k = 0; k < N; k++)
                {
                    temp[i, j] += (input[i, k] - 128) * Ct[k, j];
                }
            }
        }

        // MatrixMultiply(output, C, temp);
        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < N; j++)
            {
                temp1 = 0.0;
                for (int k = 0; k < N; k++)
                {
                    temp1 += C[i, k] * temp[k, j];
                }
                output[i, j] = ROUND(temp1);
            }
        }
    }

    public void InverseDCT(int[,] input, byte[,] output)
    {
        double[,] temp = new double[N, N];
        double temp1;

        // MatrixMultiply(temp, input, C);
        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < N; j++)
            {
                temp[i, j] = 0.0;
                for (int k = 0; k < N; k++)
                {
                    temp[i, j] += input[i, k] * C[k, j];
                }
            }
        }

        // MatrixMultiply(output, Ct, temp);
        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < N; j++)
            {
                temp1 = 0.0;
                for (int k = 0; k < N; k++)
                {
                    temp1 += Ct[i, k] * temp[k, j];
                }
                temp1 += 128.0;

                if (temp1 < 0)
                    output[i, j] = 0;
                else if (temp1 > 255)
                    output[i, j] = 255;
                else
                    output[i, j] = (byte)ROUND(temp1);
            }
        }
    }

    public void CompressFile(Stream input, BitFile output, int argc, string[] argv, int quality = 3) //string inputFilename, string outputFilename, int quality = 3)
    {
        if (quality < 0 || quality > 50)
            throw new ArgumentException($"Illegal quality factor of {quality}");

        Console.WriteLine($"Using quality factor of {quality}");
        Initialize(quality);

        //var in1 = new FileStream(input, FileMode.Open)
        output.OutputBits((uint)quality, 8);

        for (int row = 0; row < ROWS; row += N)
        {
            ReadPixelStrip(input, PixelStrip);

            for (int col = 0; col < COLS; col += N)
            {
                byte[,] inputArray = new byte[N, N];
                for (int i = 0; i < N; i++)
                {
                    for (int j = 0; j < N; j++)
                    {
                        inputArray[i, j] = PixelStrip[i, col + j];
                    }
                }

                int[,] outputArray = new int[N, N];
                ForwardDCT(inputArray, outputArray);
                WriteDCTData(output, outputArray);
            }
        }

        OutputCode(output, 1);
    }

    public void ExpandFile(BitFile input, Stream output, int argc, string[] argv)
    {
        //using (var input = new BitFile(inputFilename))
        //using (var output = new FileStream(outputFilename, FileMode.Create))

        int quality = (int)input.InputBits(8);
        Console.WriteLine($"Using quality factor of {quality}");
        Initialize(quality);

        for (int row = 0; row < ROWS; row += N)
        {
            for (int col = 0; col < COLS; col += N)
            {
                int[,] inputArray = new int[N, N];
                ReadDCTData(input, inputArray);

                byte[,] outputArray = new byte[N, N];
                InverseDCT(inputArray, outputArray);

                // Place the block back into the strip
                for (int i = 0; i < N; i++)
                {
                    for (int j = 0; j < N; j++)
                    {
                        PixelStrip[i, col + j] = outputArray[i, j];
                    }
                }
            }
            WritePixelStrip(output, PixelStrip);
        }
		output.Close();
    }
}