// Bradford Arrington 2025
using System;
using System.IO;

    public struct Symbol
    {
        public ushort LowCount;
        public ushort HighCount;
        public ushort Scale;
    }

    public  partial class Compressor
    {
        public const int MaximumScale = 16383;  /* Maximum allowed frequency count        */
        public const int EndOfStream = 256;    /* The EOF symbol                         */

        public  long UnderflowBits;    /* The present underflow count in         */
                                             /* the arithmetic coder.                  */
        public  int[][] Totals = new int[257][]; /* Pointers to the 257 context tables     */

        public string CompressionName = "Adaptive order 1 model with arithmetic coding";
        public static string Usage = "in-file out-file\n\n";

        public void CompressFile(Stream input, BitFile output, int argc, string[] argv)
        {
            Symbol s = new Symbol();
            int c;
            int context;

            context = 0;
            InitializeModel();
            InitializeArithmeticEncoder();
            while (true)
            {
                c = input.ReadByte();
                if (c == -1)
                    c = EndOfStream;
                ConvertIntToSymbol(c, context, ref s);
                EncodeSymbol(output, ref s);
                if (c == EndOfStream)
                    break;
                UpdateModel(c, context);
                context = c;
            }
            FlushArithmeticEncoder(output);
            Console.WriteLine();
            while (argc-- > 0)
                Console.WriteLine($"Unknown argument: {argv[--argc]}");
        }

        public void ExpandFile(BitFile input, Stream output, int argc, string[] argv)
        {
            Symbol s = new Symbol();
            int count;
            int c;
            int context;

            context = 0;
            InitializeModel();
            InitializeArithmeticDecoder(input);
            while (true)
            {
                GetSymbolScale(context, ref s);
                count = GetCurrentCount(ref s);
                c = ConvertSymbolToInt(count, context, ref s);
                RemoveSymbolFromStream(input, ref s);
                if (c == EndOfStream)
                    break;
                output.WriteByte((byte)c);
                UpdateModel(c, context);
                context = c;
            }
            Console.WriteLine();
            while (argc-- > 0)
                Console.WriteLine($"Unknown argument: {argv[--argc]}");
        }

        public  void InitializeModel()
        {
            for (int context = 0; context < EndOfStream; context++)
            {
                Totals[context] = new int[EndOfStream + 2];
                for (int i = 0; i <= (EndOfStream + 1); i++)
                {
                    Totals[context][i] = i;
                }
            }
        }

        public  void UpdateModel(int symbol, int context)
        {
            for (int i = symbol + 1; i <= (EndOfStream + 1); i++)
                Totals[context][i]++;
            if (Totals[context][EndOfStream + 1] < MaximumScale)
                return;
            for (int i = 1; i <= (EndOfStream + 1); i++)
            {
                Totals[context][i] /= 2;
                if (Totals[context][i] <= Totals[context][i - 1])
                    Totals[context][i] = Totals[context][i - 1] + 1;
            }
        }

        public  void ConvertIntToSymbol(int c, int context, ref Symbol s)
        {
            s.Scale = (ushort)Totals[context][EndOfStream + 1];
            s.LowCount = (ushort)Totals[context][c];
            s.HighCount = (ushort)Totals[context][c + 1];
        }

        public  void GetSymbolScale(int context, ref Symbol s)
        {
            s.Scale = (ushort)Totals[context][EndOfStream + 1];
        }

        public  int ConvertSymbolToInt(int count, int context, ref Symbol s)
        {
            int c;

            for (c = 0; count >= Totals[context][c + 1]; c++)
                ;
            s.HighCount = (ushort)Totals[context][c + 1];
            s.LowCount = (ushort)Totals[context][c];
            return c;
        }

        private  ushort code;  /* The present input code value       */
        private  ushort low;   /* Start of the current code range    */
        private  ushort high;  /* End of the current code range      */
        public  long underflowBits;             /* Number of underflow bits pending   */

        public  void InitializeArithmeticEncoder()
        {
            low = 0;
            high = 0xFFFF;
            UnderflowBits = 0;
        }

        public void FlushArithmeticEncoder(BitFile output)
        {
            output.OutputBit((low & 0x4000));
            UnderflowBits++;
            while (UnderflowBits-- > 0)
                output.OutputBit((~low & 0x4000));
            output.OutputBits( 0, 16);
        }

        public void EncodeSymbol(BitFile output, ref Symbol s)
        {
            long range;

            range = (long)(high - low) + 1;
            high = (ushort)(low + ((range * s.HighCount) / s.Scale) - 1);
            low = (ushort)(low + ((range * s.LowCount) / s.Scale));

            while (true)
            {
                if ((high & 0x8000) == (low & 0x8000))
                {
                    output.OutputBit((high & 0x8000));
                    while (UnderflowBits > 0)
                    {
                        output.OutputBit((~high & 0x8000));
                        UnderflowBits--;
                    }
                }

                else if ((low & 0x4000) != 0 && (high & 0x4000) == 0)
                {
                    UnderflowBits += 1;
                    low &= 0x3FFF;
                    high |= 0x4000;
                }
                else
                {
                    return;
                }

                low <<= 1;
                high <<= 1;
                high |= 1;
            }
        }

        public short GetCurrentCount(ref Symbol s)
        {
            long range;
            short count;

            range = (long)(high - low) + 1;
            count = (short)(((code - low + 1) * s.Scale - 1) / range);
            return count;
        }

        public void InitializeArithmeticDecoder(BitFile input)
        {
            code = 0;
            for (int i = 0; i < 16; i++)
            {
                code <<= 1;
                code += (ushort)input.InputBit();
            }
            low = 0;
            high = 0xFFFF;
        }

        public void RemoveSymbolFromStream(BitFile input, ref Symbol s)
        {
            long range;

            range = (long)(high - low) + 1;
            high = (ushort)(low + ((range * s.HighCount) / s.Scale) - 1);
            low = (ushort)(low + ((range * s.LowCount) / s.Scale));

            while (true)
            {
                if ((high & 0x8000) == (low & 0x8000))
                {
                    // No action needed, bits are already aligned
                }

                else if (((low & 0x4000) == 0x4000) && ((high & 0x4000) == 0))
                {
                    code ^= 0x4000;
                    low &= 0x3FFF;
                    high |= 0x4000;
                }
                else
                {
                    return;
                }

                low <<= 1;
                high <<= 1;
                high |= 1;
                code <<= 1;
                code += (ushort)input.InputBit();
            }
        }
    }
//}
