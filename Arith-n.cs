using System;
using System.IO;

public partial class Compressor
{
    // Constants
    private const int MAXIMUM_SCALE = 16383;  // Maximum allowed frequency count
    private const int ESCAPE = 256;           // The escape symbol
    private const int DONE = -1;              // The output stream empty symbol
    private const int FLUSH = -2;             // The symbol to flush the model

    // Structures
    public struct SYMBOL
    {
        public ushort low_count;
        public ushort high_count;
        public ushort scale;
    }

    // Model structures
    private struct STATS
    {
        public byte symbol;
        public byte counts;
    }

    private struct LINKS
    {
        public CONTEXT next;
    }

    private class CONTEXT
    {
        public int max_index;
        public LINKS[] links;
        public STATS[] stats;
        public CONTEXT lesser_context;
    }

    // Global variables
    public string CompressionName = "Adaptive order n model with arithmetic coding";
    public static string Usage = "in-file out-file [ -o order ]\n\n";
    private static int max_order = 3;
    private static CONTEXT[] contexts;
    private int current_order;
    private short[] totals = new short[258];
    private byte[] scoreboard = new byte[256];

    // Arithmetic coding variables
    private ushort code;      // The present input code value
    private ushort low;       // Start of the current code range
    private ushort high;      // End of the current code range
    private long underflowBits; // Number of underflow bits pending

    // Error handling
    private void FatalError(string message)
    {
        Console.WriteLine($"Fatal Error: {message}");
        Environment.Exit(1);
    }

    // Compression
    public void CompressFile(Stream input, BitFile output, int argc, string[] argv)
    {
        SYMBOL s = new SYMBOL();
        int c;
        int escaped;
        int flush = 0;
        long text_count = 0;

        InitializeOptions(argc,argv);
        InitializeModel();
        InitializeArithmeticEncoder();
        
        for (; ; )
        {
                if ((++text_count & 0x0ff) == 0)
                    flush = CheckCompression(input, output);
                
                if (flush == 0)
                    c = input.ReadByte();
                else
                    c = FLUSH;
                
                if (c == -1)  // EOF
                    c = DONE;
                
                do
                {
                    escaped = ConvertIntToSymbol(c, ref s);
                    EncodeSymbol(output, ref s);
                } while (escaped != 0);
                
                if (c == DONE)
                    break;
                
                if (c == FLUSH)
                {
                    FlushModel();
                    flush = 0;
                }
                
                UpdateModel(c);
                AddCharacterToModel(c);
         }
         FlushArithmeticEncoder(output);
    }

    // Expansion
    public void ExpandFile(BitFile input, Stream output, int argc, string[] argv)
    {
        SYMBOL s = new SYMBOL();
        int c;
        short count;

        InitializeOptions(argc, argv);
        InitializeModel();
        InitializeArithmeticDecoder(input);
            
        for (; ; )
        {
                do
                {
                    GetSymbolScale(ref s);
                    count = GetCurrentCount(ref s);
                    c = ConvertSymbolToInt(count, ref s);
                    RemoveSymbolFromStream(input, ref s);
                } while (c == ESCAPE);
                
                if (c == DONE)
                    break;
                
                if (c != FLUSH)
                    output.WriteByte((byte)c);
                else
                    FlushModel();
                
                UpdateModel(c);
                AddCharacterToModel(c);
        }
    }

    // Options initialization
    private void InitializeOptions(int argc, string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-o" && i + 1 < args.Length)
            {
                max_order = int.Parse(args[i + 1]);
                i++;
            }
            else
            {
                Console.WriteLine($"Unknown argument on command line: {args[i]}");
            }
        }
    }

    // Compression check
    private int CheckCompression(Stream input, BitFile output)
    {
        if (!input.CanSeek)
            throw new ArgumentException("Input stream must support seeking.");
        long local_input_marker = 0L;
        long local_output_marker = 0L;
        
        long total_input_bytes = input.Seek(0,SeekOrigin.Begin ) - local_input_marker;
        long total_output_bytes = output.fileStream.Seek(0, SeekOrigin.Begin) - local_output_marker;
        
        if (total_output_bytes == 0)
            total_output_bytes = 1;
        
        int local_ratio = (int)((total_output_bytes * 100) / total_input_bytes);
        
        local_input_marker = input.Seek(0 , SeekOrigin.Begin );
        local_output_marker = output.fileStream.Seek(0 , SeekOrigin.Begin );
        
        return local_ratio > 90 ? 1 : 0;
    }

    // Model initialization
    private void InitializeModel()
    {
        CONTEXT null_table;
        CONTEXT control_table;

        current_order = max_order;
        contexts = new CONTEXT[10];
        Array.Fill(contexts, null);
        
        null_table = new CONTEXT
        {
            max_index = -1
        };
        
        contexts[-1] = null_table;
        
        for (int i = 0; i <= max_order; i++)
        {
            contexts[i] = AllocateNextOrderTable(contexts[i - 1], 0, contexts[i - 1]);
        }
        
        null_table.stats = new STATS[256];
        null_table.max_index = 255;
        
        for (int i = 0; i < 256; i++)
        {
            null_table.stats[i].symbol = (byte)i;
            null_table.stats[i].counts = 1;
        }

        control_table = new CONTEXT();
        contexts[-2] = control_table;
        control_table.max_index = 1;
        control_table.stats = new STATS[2];
        control_table.stats[0].symbol = (byte)(-FLUSH);
        control_table.stats[0].counts = 1;
        control_table.stats[1].symbol = (byte)(-DONE);
        control_table.stats[1].counts = 1;

        Array.Fill(scoreboard, (byte)0);
    }

    private CONTEXT AllocateNextOrderTable(CONTEXT table, int symbol, CONTEXT lesser_context)
    {
        int i;
        CONTEXT new_table;

        for (i = 0; i <= table.max_index; i++)
        {
            if (table.stats[i].symbol == (byte)symbol)
                break;
        }
        
        if (i > table.max_index)
        {
            table.max_index++;
            
            if (table.links == null)
                table.links = new LINKS[table.max_index + 1];
            else
                Array.Resize(ref table.links, table.max_index + 1);
            
            if (table.stats == null)
                table.stats = new STATS[table.max_index + 1];
            else
                Array.Resize(ref table.stats, table.max_index + 1);
            
            if (table.links == null)
                FatalError("Failure #6: allocating new table");
            
            if (table.stats == null)
                FatalError("Failure #7: allocating new table");
            
            table.stats[i].symbol = (byte)symbol;
            table.stats[i].counts = 0;
        }
        
        new_table = new CONTEXT
        {
            max_index = -1
        };
        
        table.links[i].next = new_table;
        new_table.lesser_context = lesser_context;
        
        return new_table;
    }

    // Model update
    private void UpdateModel(int symbol)
    {
        int local_order;

        if (current_order < 0)
            local_order = 0;
        else
            local_order = current_order;
        
        if (symbol >= 0)
        {
            while (local_order <= max_order)
            {
                if (symbol >= 0)
                    UpdateTable(contexts[local_order], symbol);
                local_order++;
            }
        }
        
        current_order = max_order;
        Array.Fill(scoreboard, (byte)0);
    }

    private void UpdateTable(CONTEXT table, int symbol)
    {
        int index = 0;
        
        while (index <= table.max_index && table.stats[index].symbol != (byte)symbol)
            index++;
        
        if (index > table.max_index)
        {
            table.max_index++;
            
            if (current_order < max_order)
            {
                if (table.links == null)
                    table.links = new LINKS[table.max_index + 1];
                else
                    Array.Resize(ref table.links, table.max_index + 1);
                
                if (table.links == null)
                    FatalError("Error #9: reallocating table space!");
            }
            
            if (table.stats == null)
                table.stats = new STATS[table.max_index + 1];
            else
                Array.Resize(ref table.stats, table.max_index + 1);
            
            if (table.stats == null)
                FatalError("Error #10: reallocating table space!");
            
            table.stats[index].symbol = (byte)symbol;
            table.stats[index].counts = 0;
        }
        
        int i = index;
        while (i > 0 && table.stats[index].counts == table.stats[i - 1].counts)
            i--;
        
        if (i != index)
        {
            byte temp_symbol = table.stats[index].symbol;
            table.stats[index].symbol = table.stats[i].symbol;
            table.stats[i].symbol = temp_symbol;
            
            if (table.links != null)
            {
                CONTEXT temp_ptr = table.links[index].next;
                table.links[index].next = table.links[i].next;
                table.links[i].next = temp_ptr;
            }
            
            index = i;
        }
        
        table.stats[index].counts++;
        
        if (table.stats[index].counts == 255)
            RescaleTable(table);
    }

    // Symbol conversion
    private int ConvertIntToSymbol(int c, ref SYMBOL s)
    {
        CONTEXT table = contexts[current_order];
        TotalizeTable(table);
        s.scale = (ushort)totals[0];
        
        if (current_order == -2)
            c = -c;
        
        for (int i = 0; i <= table.max_index; i++)
        {
            if (c == table.stats[i].symbol)
            {
                if (table.stats[i].counts == 0)
                    break;
                
                s.low_count = (ushort)totals[i + 2];
                s.high_count = (ushort)totals[i + 1];
                return 0;
            }
        }
        
        s.low_count = (ushort)totals[1];
        s.high_count = (ushort)totals[0];
        current_order--;
        return 1;
    }

    private void GetSymbolScale(ref SYMBOL s)
    {
        CONTEXT table = contexts[current_order];
        TotalizeTable(table);
        s.scale = (ushort)totals[0];
    }

    private int ConvertSymbolToInt(short count, ref SYMBOL s)
    {
        int c;
        CONTEXT table = contexts[current_order];
        
        for (c = 0; count < totals[c]; c++) ;
        
        s.high_count = (ushort)totals[c - 1];
        s.low_count = (ushort)totals[c];
        
        if (c == 1)
        {
            current_order--;
            return ESCAPE;
        }
        
        if (current_order < -1)
            return -table.stats[c - 2].symbol;
        else
            return table.stats[c - 2].symbol;
    }

    // Character addition
    private void AddCharacterToModel(int c)
    {
        if (max_order < 0 || c < 0)
            return;
        
        contexts[max_order] = ShiftToNextContext(contexts[max_order], c, max_order);
        
        for (int i = max_order - 1; i > 0; i--)
            contexts[i] = contexts[i + 1].lesser_context;
    }

    private CONTEXT ShiftToNextContext(CONTEXT table, int c, int order)
    {
        table = table.lesser_context;
        
        if (order == 0)
            return table.links[0].next;
        
        for (int i = 0; i <= table.max_index; i++)
        {
            if (table.stats[i].symbol == (byte)c)
            {
                if (table.links[i].next != null)
                    return table.links[i].next;
                else
                    break;
            }
        }
        
        CONTEXT new_lesser = ShiftToNextContext(table, c, order - 1);
        table = AllocateNextOrderTable(table, c, new_lesser);
        return table;
    }

    // Table operations
    private void RescaleTable(CONTEXT table)
    {
        if (table.max_index == -1)
            return;
        
        for (int i = 0; i <= table.max_index; i++)
            table.stats[i].counts /= 2;
        
        if (table.stats[table.max_index].counts == 0 && table.links == null)
        {
            while (table.stats[table.max_index].counts == 0 && table.max_index >= 0)
                table.max_index--;
            
            if (table.max_index == -1)
            {
                table.stats = null;
            }
            else
            {
                Array.Resize(ref table.stats, table.max_index + 1);
            }
        }
    }

    private void TotalizeTable(CONTEXT table)
    {
        byte max;
        int i;
        
        for (; ; )
        {
            max = 0;
            i = table.max_index + 2;
            totals[i] = 0;
            
            for (; i > 1; i--)
            {
                totals[i - 1] = totals[i];
                
                if (table.stats[i - 2].counts != 0)
                {
                    if (current_order == -2 || scoreboard[table.stats[i - 2].symbol] == 0)
                        totals[i - 1] += table.stats[i - 2].counts;
                }
                
                if (table.stats[i - 2].counts > max)
                    max = table.stats[i - 2].counts;
            }
            
            if (max == 0)
            {
                totals[0] = 1;
            }
            else
            {
                totals[0] = (short)(256 - table.max_index);
                totals[0] *= (short)table.max_index;
                totals[0] /= 256;
                totals[0] /= max;
                totals[0]++;
                totals[0] += totals[1];
            }
            
            if (totals[0] < MAXIMUM_SCALE)
                break;
            
            RescaleTable(table);
        }
        
        for (i = 0; i < table.max_index; i++)
        {
            if (table.stats[i].counts != 0)
                scoreboard[table.stats[i].symbol] = 1;
        }
    }

    private void RecursiveFlush(CONTEXT table)
    {
        if (table.links != null)
        {
            for (int i = 0; i <= table.max_index; i++)
            {
                if (table.links[i].next != null)
                    RecursiveFlush(table.links[i].next);
            }
        }
        
        RescaleTable(table);
    }

    private void FlushModel()
    {
        Console.Write('F');
        RecursiveFlush(contexts[0]);
    }

    // Arithmetic coding
    private void InitializeArithmeticEncoder()
    {
        low = 0;
        high = 0xffff;
        underflowBits = 0;
    }

    private void FlushArithmeticEncoder(BitFile output)
    {
        output.OutputBit((low & 0x4000));
        underflowBits++;
        while (underflowBits-- > 0)
            output.OutputBit((~low & 0x4000));
        
        output.OutputBits(0, 16);
    }

    private void EncodeSymbol(BitFile output, ref SYMBOL s)
    {
        uint range = (uint)(high - low) + 1;
        high = (ushort)(low + ((range * s.high_count) / s.scale - 1));
        low = (ushort)(low + ((range * s.low_count) / s.scale));
        
        for (; ; )
        {
            if ((high & 0x8000) == (low & 0x8000))
            {
                output.OutputBit((high & 0x8000));
                
                while (underflowBits > 0)
                {
                    output.OutputBit((~high & 0x8000));
                    underflowBits--;
                }
            }
            else if ((low & 0x4000) != 0 && (high & 0x4000) == 0)
            {
                underflowBits++;
                low &= 0x3fff;
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

    private short GetCurrentCount(ref SYMBOL s)
    {
        uint range = (uint)(high - low) + 1;
        short count = (short)((((uint)(code - low) + 1) * s.scale - 1) / range);
        return count;
    }

    private  void InitializeArithmeticDecoder(BitFile input)
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

    private void RemoveSymbolFromStream(BitFile input, ref SYMBOL s)
    {
        uint range = (uint)(high - low) + 1;
        high = (ushort)(low + ((range * s.high_count) / s.scale - 1));
        low = (ushort)(low + ((range * s.low_count) / s.scale));
        
        for (; ; )
        {
            if ((high & 0x8000) == (low & 0x8000))
            {
                // Do nothing
            }
            else if ((low & 0x4000) == 0x4000 && (high & 0x4000) == 0)
            {
                code ^= 0x4000;
                low &= 0x3fff;
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
            int bit = input.InputBit();// ? 1 : 0;
            code += (ushort)bit;
        }
    }
}
