using System;
using System.IO;

public partial class Compressor
{
    public const int PACIFIER_COUNT = 2047;
    public class BitFile
    {
        private FileStream fileStream;
        private int rack;
        private byte mask;
        private int pacifierCounter;
        private bool isInput;

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
}