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
        private const int MAXIMUM_SCALE = 16383; // Maximum allowed frequency count
        private const int END_OF_STREAM = 257;   // The EOF symbol
        private const int ESCAPE = 256;          // The ESCAPE symbol

        private static int[][] totals = new int[END_OF_STREAM][];

        /*private  void InitializeArithmeticDecoder(BitFile stream);
        private static void RemoveSymbolFromStream(BitFile stream, ref Symbol s);
        private static void InitializeArithmeticEncoder();
        private static void EncodeSymbol(BitFile stream, ref Symbol s);
        private static void FlushArithmeticEncoder(BitFile stream);
        private static short GetCurrentCount(ref Symbol s);
        private static void InitializeModel();
        private static void UpdateModel(int symbol, int context);
        private static int ConvertIntToSymbol(int symbol, int context, ref Symbol s);
        private static void GetSymbolScale(int context, ref Symbol s);
        private static int ConvertSymbolToInt(int count, int context, ref Symbol s); */

        public string CompressionName = "Adaptive order 1e model with arithmetic coding";
        public static string Usage = "in-file out-file\n\n";

        public void CompressFile(Stream input, BitFile output, int argc, string[] argv)
        {
            Symbol s = new Symbol();
            int c;
            int context;
            int escaped;

            context = 0;
            InitializeModel();
            InitializeArithmeticEncoder();
            while (true)
            {
                c = input.ReadByte();
                if (c == -1)
                    c = END_OF_STREAM;
                escaped = ConvertIntToSymbol(c, context, ref s);
                EncodeSymbol(output, ref s);
                if (escaped != 0)
                {
                    ConvertIntToSymbol(c, ESCAPE, ref s);
                    EncodeSymbol(output, ref s);
                }
                if (c == END_OF_STREAM)
                    break;
                UpdateModel(c, context);
                context = c;
            }
            FlushArithmeticEncoder(output);
            Console.WriteLine();
            while (argc-- > 0)
                Console.WriteLine($"Unused argument: {argv[argc]}");
        }

        public void ExpandFile(BitFile input, Stream output, int argc, string[] argv)
        {
            Symbol s = new Symbol();
            int count;
            int c;
            int context;
            int lastContext;

            context = 0;
            InitializeModel();
            InitializeArithmeticDecoder(input);
            while (true)
            {
                lastContext = context;
                do
                {
                    GetSymbolScale(context, ref s);
                    count = GetCurrentCount(ref s);
                    c = ConvertSymbolToInt(count, context, ref s);
                    RemoveSymbolFromStream(input, ref s);
                    context = c;
                }
                while (c == ESCAPE);
                if (c == END_OF_STREAM)
                    break;
                output.WriteByte((byte)c);
                UpdateModel(c, lastContext);
                context = c;
            }
            Console.WriteLine();
            while (argc-- > 0)
                Console.WriteLine($"Unused argument: {argv[argc]}");
        }

        private void InitializeModel()
        {
            for (int context = 0; context < END_OF_STREAM; context++)
            {
                totals[context] = new int[END_OF_STREAM + 2];
                if (totals[context] == null) {
                    Console.WriteLine($"Failure allocating context {context}");
                    Environment.Exit(1);
                }
                UpdateModel(ESCAPE, context);
            }
            for (int i = 0; i <= (END_OF_STREAM + 1); i++)
                totals[ESCAPE][i] = i;
        }

        private void UpdateModel(int symbol, int context)
        {
            for (int i = symbol + 1; i <= (END_OF_STREAM + 1); i++)
                totals[context][i]++;
            if (totals[context][END_OF_STREAM + 1] < MAXIMUM_SCALE)
                return;
            for (int i = 1; i <= (END_OF_STREAM + 1); i++)
                totals[context][i] /= 2;
            totals[context][END_OF_STREAM] = totals[context][ESCAPE] + 1;
            totals[context][END_OF_STREAM + 1] = totals[context][END_OF_STREAM] + 1;
        }

        private int ConvertIntToSymbol(int c, int context, ref Symbol s)
        {
            s.Scale = (ushort)totals[context][END_OF_STREAM + 1];
            s.LowCount = (ushort)totals[context][c];
            s.HighCount = (ushort)totals[context][c + 1];
            if (s.LowCount < s.HighCount)
                return 0;
            s.LowCount = (ushort)totals[context][ESCAPE];
            s.HighCount = (ushort)totals[context][ESCAPE + 1];
            return 1;
        }

        private void GetSymbolScale(int context, ref Symbol s)
        {
            s.Scale = (ushort)totals[context][END_OF_STREAM + 1];
        }

        private int ConvertSymbolToInt(int count, int context, ref Symbol s)
        {
            int c = 0;
            while (count >= totals[context][c + 1])
                c++;
            s.HighCount = (ushort)totals[context][c + 1];
            s.LowCount = (ushort)totals[context][c];
            return c;
        }

        // Define the specific properties as static fields
        private static ushort code;  // The present input code value
        private static ushort lowReg;   // Start of the current code range
        private static ushort highReg;  // End of the current code range
        private static long underflowBits;  // Number of underflow bits pending

        private void InitializeArithmeticEncoder()
        {
            lowReg = 0;
            highReg = 0xffff;
            underflowBits = 0;
        }

        private void FlushArithmeticEncoder(BitFile output)
        {
            output.OutputBit( (highReg & 0x4000));
            underflowBits++;
            while (underflowBits-- > 0)
                output.OutputBit( (~highReg & 0x4000));
            output.OutputBits(0, 16);
        }

        private void EncodeSymbol(BitFile output, ref Symbol s)
        {
            long range = (long)(highReg - lowReg) + 1;
            highReg = (ushort)(lowReg + ((range * s.HighCount) / s.Scale - 1));
            lowReg = (ushort)(lowReg + ((range * s.LowCount) / s.Scale));

            while (true)
            {
                if ((highReg & 0x8000) == (lowReg & 0x8000))
                {
                    output.OutputBit((highReg & 0x8000));
                    while (underflowBits > 0)
                    {
                        output.OutputBit( (~highReg & 0x8000));
                        underflowBits--;
                    }
                }
                else if ((lowReg & 0x4000) != 0 && (highReg & 0x4000) == 0)
                {
                    underflowBits += 1;
                    lowReg &= 0x3fff;
                    highReg |= 0x4000;
                }
                else
                {
                    return;
                }
                lowReg <<= 1;
                highReg = (ushort)((highReg << 1) | 1);
            }
        }

        private short GetCurrentCount(ref Symbol s)
        {
            long range = (long)(highReg - lowReg) + 1;
            short count = (short)((((long)(code - lowReg) + 1) * s.Scale - 1) / range);
            return count;
        }

        private void InitializeArithmeticDecoder(BitFile input)
        {
            code = 0;
            for (int i = 0; i < 16; i++)
            {
                code <<= 1;
                code += (ushort)input.InputBit();
            }
            lowReg = 0;
            highReg = 0xffff;
        }

        private void RemoveSymbolFromStream(BitFile input, ref Symbol s)
        {
            long range = (long)(highReg - lowReg) + 1;
            highReg = (ushort)(lowReg + ((range * s.HighCount) / s.Scale - 1));
            lowReg = (ushort)(lowReg + ((range * s.LowCount) / s.Scale));

            while (true)
            {
                if ((highReg & 0x8000) == (lowReg & 0x8000))
                {
                    // Nothing to do here, as per the original C code
                }
                else if (((lowReg & 0x4000) == 0x4000) && ((highReg & 0x4000) == 0))
                {
                    code ^= 0x4000;
                    lowReg &= 0x3fff;
                    highReg |= 0x4000;
                }
                else
                {
                    return;
                }
                lowReg <<= 1;
                highReg = (ushort)((highReg << 1) | 1);
                code = (ushort)((code << 1) + input.InputBit());
            }
        }
    }