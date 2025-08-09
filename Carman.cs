// Bradford Arrington III 2025

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Text;

public class CarProcessor
{
    public const int UNUSED = 0;
    public const int INDEX_BIT_COUNT = 12;
    public const int LENGTH_BIT_COUNT = 4;
    public const int WINDOW_SIZE = (1 << INDEX_BIT_COUNT);
    public const int TREE_ROOT = (1 << INDEX_BIT_COUNT);
    public const int RAW_LOOK_AHEAD_SIZE = (1 << LENGTH_BIT_COUNT);
    public const int END_OF_STREAM = 0;
    public const int BREAK_EVEN = ((1 + INDEX_BIT_COUNT + LENGTH_BIT_COUNT) / 9);
    public const int LOOK_AHEAD_SIZE = (RAW_LOOK_AHEAD_SIZE + BREAK_EVEN);
    // Constants
    public const int BaseHeaderSize = 19;
                               
    public const ulong CrcMask = 0xFFFFFFFFL;
    public const uint Crc32Polynomial = 0xEDB88320;
    public const int FilenameMax = 128;
    public const int MaxFileList = 100;
    public const int Ccitt32TableSize = 256;

    // Header Structure
    public struct HeaderStruct
    {
        public string FileName;// { get; set; } = string.Empty;
        public char CompressionMethod; //{ get; set; }
        public ulong OriginalSize; //{ get; set; }
        public ulong CompressedSize; //{ get; set; }
        public ulong OriginalCrc; //{ get; set; }
        public ulong HeaderCrc; // { get; set; }
    }
    public struct TreeNode
    {
        public int Parent;
        public int SmallerChild;
        public int LargerChild;
    }
    public static byte[] Window = new byte[WINDOW_SIZE];
    public static TreeNode[] Tree = new TreeNode[WINDOW_SIZE + 1];

    // Global Variables
    public static string TempFileName = new string('\0', FilenameMax);
    public static FileStream InputCarFile;
    public static string CarFileName = string.Empty;
    public static FileStream OutputCarFile;
    //private FileStream OutputCarFile;
    private List<string> FileList = new List<string>();
    public static ulong[] Ccitt32Table = new ulong[Ccitt32TableSize];
    public static HeaderStruct Header = new HeaderStruct();

    public static int ModWindow(int a)
    {
        return a & (WINDOW_SIZE - 1);
    }
    static string StrRChr(string stringToSearch, char charToFind)
    {
        int index = stringToSearch.LastIndexOf(charToFind);
        if (index > -1)
            return stringToSearch.Substring(index);
        return stringToSearch;
    }
    public  void UsageExit()
    {
        Console.WriteLine("CARMAN -- Compressed Archive MANager");
        Console.WriteLine("Usage: carman command car-file [file ...]");
        Console.WriteLine("Commands:");
        Console.WriteLine("  a: Add files to a CAR archive (replace if present)");
        Console.WriteLine("  x: Extract files from a CAR archive");
        Console.WriteLine("  r: Replace files in a CAR archive");
        Console.WriteLine("  d: Delete files from a CAR archive");
        Console.WriteLine("  p: Print files on standard output");
        Console.WriteLine("  l: List contents of a CAR archive");
        Console.WriteLine("  t: Test files in a CAR archive");
        Console.WriteLine("");
        //Console.ReadLine();
    }
    public  void PrintListTitles()
    {
        Console.Write("\n");
        Console.Write("                       Original  Compressed\n");
        Console.Write("     Filename            Size       Size     Ratio   CRC-32   Method\n");
        Console.Write("------------------     --------  ----------  -----  --------  ------\n");
    }


    private void CopyFileFromInputCar()
    {
        byte[] buffer = new byte[256];
        ulong count;

        WriteFileHeader();
        while (Header.CompressedSize != 0)
        {
            if (Header.CompressedSize < 256)
            {
                count = (Header.CompressedSize);
            }
            else
            {
                count = 256;
            }
            if (InputCarFile.Read(buffer, 1, (int)count) != (int)count)
            {
                Console.WriteLine("Error reading input file {0}", Header.FileName);
            }
            Header.CompressedSize -= (uint)count;
            try
            {
                OutputCarFile.Write(buffer, 1, (int)count);
            }
            catch
            {
                Console.WriteLine("Error writing to output CAR file");
            }
        }
    }
    public int ProcessAllFilesInInputCar(char command, int count)
    {
        Stream outputDestination = null;
        try
        {
            if (command == 'P')
            {
                outputDestination = Console.OpenStandardOutput();
            }
            else if (command == 'T')
            {
                outputDestination = new FileStream(Path.GetTempFileName(), FileMode.Create);
            }

            while (InputCarFile != null && ReadFileHeader() != 0)
            {
                bool matched = SearchFileList(Header.FileName);

                switch (command)
                {
                    case 'D':
                        if (matched)
                        {
                            SkipOverFileFromInputCar();
                            count++;
                        }
                        else
                        {
                            CopyFileFromInputCar();
                        }
                        break;
                    case 'A':
                        if (matched)
                            SkipOverFileFromInputCar();
                        else
                            CopyFileFromInputCar();
                        break;
                    case 'L':
                        if (matched)
                        {
                            ListCarFileEntry();
                            count++;
                        }
                        SkipOverFileFromInputCar();
                        break;
                    case 'P':
                    case 'X':
                    case 'T':
                        if (matched)
                        {
                            Extract(outputDestination);
                            count++;
                        }
                        else
                        {
                            SkipOverFileFromInputCar();
                        }
                        break;
                    case 'R':
                        if (matched)
                        {
                            try
                            {
                                using (FileStream inputTextFile = new FileStream(Header.FileName, FileMode.Open, FileAccess.Read))
                                {
                                    SkipOverFileFromInputCar();
                                    Insert(inputTextFile, "Replacing");
                                    count++;
                                }
                            }
                            catch
                            {
                                Console.Error.Write($"Could not find {Header.FileName} for replacement, skipping");
                                CopyFileFromInputCar();
                            }
                        }
                        else
                        {
                            CopyFileFromInputCar();
                        }
                        break;
                }
            }
        }
        finally
        {
            outputDestination?.Dispose();
        }
        return count;
    }

    private void BuildFileList(int argc, string[] args, char command)
    {
        if (args.Length == 2)
        {
            FileList.Add("*");
        }
        else
        {
            foreach (string file in args)
            {
                if (command == 'A')
                {
                    // Handle wildcard expansion for Add command
                    try
                    {
                        string dir = Path.GetDirectoryName(file);
                        if (string.IsNullOrEmpty(dir)) dir = ".";
                        string pattern = Path.GetFileName(file);

                        foreach (string foundFile in Directory.GetFiles(dir, pattern))
                        {
                            FileList.Add(foundFile.ToLower());
                        }
                    }
                    catch
                    {
                        FileList.Add(file.ToLower());
                    }
                }
                else
                {
                    FileList.Add(file.ToLower());
                }
            }
        }
    }
    public void BuildFileList2(int argc, string[] args, int command)
    {
        if (args.Length > 2)
        {
            bool exists = false;
            exists = Array.Exists(args, element => element == "*");
            if (exists)
            {
                DirectoryInfo place = new DirectoryInfo(Directory.GetCurrentDirectory());
                FileInfo[] Files = place.GetFiles();
                int a = 0;
                foreach (FileInfo i in Files)
                {
                    Console.WriteLine("File Name - {0}", i.Name);
                    FileList[a] =  i.Name;
                    a++;
                }
            }
            else
            {
                int a = 0;
                for (int i = 2; i < args.Length; i++)
                {
                    FileList[a] = args[i];
                    a++;
                    //Array.Resize(ref FileList, FileList.Length + 1);
                    //FileList[FileList.Length - 1] = args[i];
                    //FileList = FileList.Concat(new[] { args[i] }).ToArray();
                    //FileList.Add(args[i]);
                }
            }
        }
    }
    public int AddFileListToArchive()
    {
        int count = 0;
        foreach (string file in FileList)
        {
            try
            {
                using (FileStream inputTextFile = new FileStream(file, FileMode.Open, FileAccess.Read))
                {
                    string fileNameOnly = Path.GetFileName(file);

                    // Check for duplicates
                    bool skip = false;
                    for (int i = 0; i < count; i++)
                    {
                        if (string.Equals(fileNameOnly, FileList[i], StringComparison.OrdinalIgnoreCase))
                        {
                            Console.Error.Write($"Duplicate file name: {file}   Skipping this file...");
                            skip = true;
                            break;
                        }
                    }

                    if (!skip)
                    {
                        //Header = new Header();
                        Header.FileName = fileNameOnly;
                        Insert(inputTextFile, "Adding");
                        count++;
                    }
                }
            }
            catch
            {
                FatalError("Could not open {0} to add to CAR file", file);
            }
        }
        return count;
    }

    public int AddFileListToArchive2()
    {
        int i;
        int skip = 0;
        FileStream input_text_file;
        byte[] input_text_file1;

        for (i = 0; FileList[i] != null; i++)
        {
            input_text_file1 = File.ReadAllBytes(FileList[i]);
            if (input_text_file1 == null)
                Console.WriteLine("Could not open %s to add to CAR file", FileList[i]);

            if (skip == 0)  // XXX Fix me
            {
                Header.FileName = FileList[i];
                input_text_file = new FileStream(Header.FileName, FileMode.Open, FileAccess.Read);
                Insert(input_text_file, "Adding");
            }
        }
        return (i);
    }

    public char ParseArguments(int argc, string[] argv)
    {
        //char command;
        //string[] arguments = Environment.GetCommandLineArgs();

        if (argv.Length < 2)
        {
            UsageExit();
            Environment.Exit(0);
        }

        char lowerCase = char.Parse(argv[0]); // argv[0][0]);
        char command = char.ToUpper(lowerCase, CultureInfo.InvariantCulture);
        switch (command) // = char.Parse(arguments[1]))
        {
            case 'X':
                Console.WriteLine("Extracting files");
                break;
            case 'R':
                Console.WriteLine("Replacing files");
                break;
            case 'P':
                Console.WriteLine("Print files to stdout");
                break;
            case 'T':
                Console.WriteLine("Testing integrity of files");
                break;
            case 'L':
                Console.WriteLine("Listing archive contents");
                break;
            case 'A':
                if (argv.Length <= 3)
                    UsageExit();
                Console.WriteLine("Adding/replacing files to archive");
                break;
            case 'D':
                if (argv.Length <= 3)
                    UsageExit();
                Console.WriteLine("Deleting files from archive");
                break;
            default:
                UsageExit();
                break;
        };
        return command;
    }
    public void OpenArchiveFiles(string name, char command)
    {
        string s;
        //int i;
        int FILENAME_MAX = 255;
        char x = command;

        CarFileName = name;
        InputCarFile = new FileStream(CarFileName, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        if (InputCarFile != null)
        {
            s = StrRChr(CarFileName, '\\');
            if (s == null)
                s = CarFileName;

            if (StrRChr(s, '.') == null)
                if (CarFileName.Length < (FILENAME_MAX - 4))
                {
                    CarFileName += ".car";
                    FileStream fsout = new FileStream(CarFileName, FileMode.Create, FileAccess.Write);
                    InputCarFile.CopyTo(fsout);
                }
        }

        if (!File.Exists(CarFileName) && (char)command != 'A')
            FatalError("Can't open archive {0}", CarFileName);

        if ((char)command == 'A' || (char)command == 'R' || (char)command == 'D')
        {
            TempFileName = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + Path.GetExtension(CarFileName));
            OutputCarFile = new FileStream(TempFileName, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            if (OutputCarFile == null)
                FatalError("Can't open temporary file {0}", TempFileName);
        }
        if (InputCarFile != null)
            InputCarFile.Flush();

        if (OutputCarFile != null)
            OutputCarFile.Flush();
    }

    private bool WildCardMatch(string str, string pattern)
    {
        int strIndex = 0;
        int patternIndex = 0;
        int matchPos = 0;
        int starPos = -1;

        while (strIndex < str.Length)
        {
            if (patternIndex < pattern.Length && (pattern[patternIndex] == '?' || pattern[patternIndex] == str[strIndex]))
            {
                strIndex++;
                patternIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starPos = patternIndex;
                matchPos = strIndex;
                patternIndex++;
            }
            else if (starPos != -1)
            {
                patternIndex = starPos + 1;
                matchPos++;
                strIndex = matchPos;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }
    /*
    private static bool Match(string str, string pattern, int si, int pi)
    {
        while (true)
        {
            if (pi < pattern.Length && pattern[pi] == '*')
            {
                pi++;
                for (int skip = 0; si + skip <= str.Length; skip++)
                {
                    if (Match(str, pattern, si + skip, pi))
                        return true;
                }
                return false;
            }
            else if (pi < pattern.Length && pattern[pi] == '?')
            {
                if (si >= str.Length)
                    return false;
                si++;
                pi++;
            }
            else
            {
                if (si >= str.Length || pi >= pattern.Length)
                    return si == str.Length && pi == pattern.Length;

                if (str[si] != pattern[pi])
                    return false;

                si++;
                pi++;
            }
        }
    } */

    private bool SearchFileList(string fileName)
    {
        foreach (string file in FileList)
        {
            if (WildCardMatch(fileName, file))
                return true;
        }
        return false;
    }

    public static int RatioInPercent(ulong compressed, ulong original)
    {
        if (original == 0)
            return 0;
        int result = (int)((100L * compressed) / original);
        return 100 - result;
    }

    public static int ReadFileHeader()
    {
        byte[] headerData = new byte[17];
        ulong headerCrc;
        uint i = 0;

        StringBuilder fileName = new StringBuilder();

        while (true)
        {
            int c = InputCarFile.ReadByte();
            if (c == -1) return 0;

            if (c == 0)
                break;

            fileName.Append((char)c);

            if (++i == FilenameMax)
                FatalError($"File name exceeded maximum in header");
        }
        ///string result = fileName.ToString().Split('\0')[0]; // Gets part before `\0` m_ChunkChars
        //Header.FileName = result;

        // if (i == 0)
        //     return 0;

        //byte[] byteArray = Encoding.ASCII.GetBytes(fileName.ToString());
        // headerCrc = CalculateBlockCRC32((i + 1), CrcMask, byteArray);
        if (fileName.Length == 0)
            return 0;

        Header.FileName = fileName.ToString();
        headerCrc = CalculateBlockCRC32((uint)(i + 1), CrcMask, Encoding.ASCII.GetBytes(Header.FileName + '\0'));   

        if (InputCarFile.Read(headerData, 0, 17) != 17)
            return 0;

        Header.CompressionMethod = (char)headerData[0];
        Header.OriginalSize = BitConverter.ToUInt32(headerData, 1);
        Header.CompressedSize = BitConverter.ToUInt32(headerData, 5);
        Header.OriginalCrc = BitConverter.ToUInt32(headerData, 9);
        Header.HeaderCrc = BitConverter.ToUInt32(headerData, 13);
        //Header.OriginalSize = UnpackUnsignedData(4, headerData, 1);
        //Header.CompressedSize = UnpackUnsignedData(4, headerData, 5);
        //Header.OriginalCrc = UnpackUnsignedData(4, headerData, 9);
        //Header.HeaderCrc = UnpackUnsignedData(4, headerData, 13);

        headerCrc = CalculateBlockCRC32(13, headerCrc, headerData);

        headerCrc ^= CrcMask;

        if (Header.HeaderCrc != headerCrc)
            FatalError($"Header checksum error for file {Header.FileName}");

        return 1;
    }

    public static ulong UnpackUnsignedData(int numberOfBytes, byte[] buffer, int offset)
    {
        ulong result = 0;
        int shiftCount = 0;
        int index = offset;

        while (numberOfBytes-- > 0)
        {
            result |= ((ulong)buffer[index++]) << shiftCount;
            shiftCount += 8;
        }

        return result;
    }

    private void WriteFileHeader()
    {
        byte[] headerData = new byte[17];
        uint i = 0;

        byte[] bytes = Encoding.ASCII.GetBytes(Header.FileName);
        byte[] withNull = bytes.Concat(new byte[] { 0 }).ToArray();
        OutputCarFile.Write(withNull, 0, withNull.Length);
        i = (uint)withNull.Length;

        Header.HeaderCrc = CalculateBlockCRC32(i, CrcMask, withNull);

        // PackUnsignedData needs to be implemented
        PackUnsignedData(1, (ulong)Header.CompressionMethod, headerData, 0);
        PackUnsignedData(4, Header.OriginalSize, headerData, 1);
        PackUnsignedData(4, Header.CompressedSize, headerData, 5);
        PackUnsignedData(4, Header.OriginalCrc, headerData, 9);
        Header.HeaderCrc = CalculateBlockCRC32(13, Header.HeaderCrc, headerData);
        Header.HeaderCrc ^= CrcMask;
        PackUnsignedData(4, Header.HeaderCrc, headerData, 13);

        OutputCarFile?.Write(headerData, 0, 17);
    }

    public static void PackUnsignedData(int numberOfBytes, ulong number, byte[] buffer, int offset)
    {
        for (int i = 0; i < numberOfBytes; i++)
        {
            buffer[offset + i] = (byte)(number & 0xFF);
            number >>= 8;
        }
    }

    public void WriteEndOfCarHeader()
    {
        OutputCarFile.WriteByte(0);
        //InputCarFile.Close();
        //OutputCarFile.Close();
    }
    /*
    public static void Insert2(FileStream inputTextFile, string operation)
    {
        long savedPositionOfHeader;
        long savedPositionOfFile;

        Console.Error.Write($"{operation} {Header.FileName,-20}");

        savedPositionOfHeader = OutputCarFile?.Position ?? 0;
        Header.CompressionMethod = (char)2;
        WriteFileHeader();

        savedPositionOfFile = OutputCarFile?.Position ?? 0;
        inputTextFile.Seek(0, SeekOrigin.End);
        Header.OriginalSize = (ulong)inputTextFile.Length;
        inputTextFile.Seek(0, SeekOrigin.Begin);
        int rc = 0;
        rc = LZSSCompress(inputTextFile);
        if (rc != 1)
        {
            Header.CompressionMethod = (char)1;
            OutputCarFile?.Seek(savedPositionOfFile, SeekOrigin.Begin);
            inputTextFile.Seek(0, SeekOrigin.Begin);
            Store(inputTextFile);
        }

        inputTextFile.Close();

        OutputCarFile?.Seek(savedPositionOfHeader, SeekOrigin.Begin);
        WriteFileHeader();
        OutputCarFile?.Seek(0L, SeekOrigin.End);
        Console.WriteLine($" {RatioInPercent(Header.CompressedSize, Header.OriginalSize)}%");
    } */

    private void Insert(Stream inputTextFile, string operation)
    {
        Console.Error.Write("{0} {1,-20}", operation, Header.FileName);

        long savedPositionOfHeader = OutputCarFile.Position;
        Header.CompressionMethod = (char)2;
        WriteFileHeader();

        long savedPositionOfFile = OutputCarFile.Position;
        Header.OriginalSize = (uint)inputTextFile.Length;
        inputTextFile.Seek(0, SeekOrigin.Begin);

        if (!LZSSCompress(inputTextFile))
        {
            Header.CompressionMethod = (char)1;
            OutputCarFile.Seek(savedPositionOfFile, SeekOrigin.Begin);
            inputTextFile.Seek(0, SeekOrigin.Begin);
            Store(inputTextFile);
        }

        OutputCarFile.Seek(savedPositionOfHeader, SeekOrigin.Begin);
        WriteFileHeader();
        OutputCarFile.Seek(0, SeekOrigin.End);

        Console.WriteLine(" {0}%", RatioInPercent(Header.CompressedSize, Header.OriginalSize));
    }

    private void Extract(Stream destination)
    {
        Console.Error.Write("{0,-20} ", Header.FileName);
        bool error = false;

        Stream outputTextFile = destination;
        bool shouldClose = false;

        try
        {
            if (destination == null)
            {
                outputTextFile = new FileStream(Header.FileName, FileMode.Create, FileAccess.Write);
                shouldClose = true;
            }

            ulong crc;
            switch (Header.CompressionMethod)
            {
                case (char)1:
                    crc = Unstore(outputTextFile);
                    break;
                case (char)2:
                    crc = LZSSExpand(outputTextFile);
                    break;
                default:
                    Console.Error.Write("Unknown method: {0}", Header.CompressionMethod);
                    SkipOverFileFromInputCar();
                    error = true;
                    crc = Header.OriginalCrc;
                    break;
            }

            if (crc != Header.OriginalCrc)
            {
                Console.Error.Write("CRC error reading data");
                error = true;
            }

            if (!error)
                Console.Error.Write(" OK");
        }
        catch
        {
            Console.Error.Write("Can't open {0}", Header.FileName);
            Console.Error.Write("Not extracted");
            SkipOverFileFromInputCar();
        }
        finally
        {
            if (shouldClose)
            {
                outputTextFile?.Close();

                if (error)
                {
                    try { File.Delete(Header.FileName); } catch { }
                }
            }
        }
    }
    
    private bool Store(Stream inputTextFile)
    {
        byte[] buffer = new byte[256];
        int pacifier = 0;
        Header.OriginalCrc = CrcMask;

        int bytesRead;
        while ((bytesRead = inputTextFile.Read(buffer, 0, 256)) > 0)
        {
            OutputCarFile.Write(buffer, 0, bytesRead);
            Header.OriginalCrc = CalculateBlockCRC32((uint)bytesRead, Header.OriginalCrc, buffer);

            if ((++pacifier & 15) == 0)
                Console.Error.Write('.');
        }

        Header.CompressedSize = Header.OriginalSize;
        Header.OriginalCrc ^= CrcMask;
        return true;
    }

    private ulong Unstore(Stream destination)
    {
        ulong crc = CrcMask;
        byte[] buffer = new byte[256];
        int pacifier = 0;
        ulong remaining = Header.OriginalSize;

        while (remaining > 0)
        {
            int count = (int)Math.Min(remaining, 256);
            if (InputCarFile.Read(buffer, 0, count) != count)
                FatalError("Can't read from input CAR file");

            destination.Write(buffer, 0, count);
            crc = CalculateBlockCRC32((uint)count, crc, buffer);

            if (destination != Console.OpenStandardOutput() && (++pacifier & 15) == 0)
                Console.Error.Write('.');

            remaining -= (uint)count;
        }

        return crc ^ CrcMask;
    }
    /*
    public static bool Store(FileStream inputTextFile)
    {
        byte[] buffer = new byte[256];
        uint n;
        int pacifier = 0;

        Header.OriginalCrc = CrcMask;

        while ((n = (uint)inputTextFile.Read(buffer, 0, 256)) != 0)
        {
            OutputCarFile?.Write(buffer, 0, (int)n);
            Header.OriginalCrc = CalculateBlockCRC32(n, Header.OriginalCrc, buffer);
            if (((++pacifier) & 15) == 0)
                Console.Error.Write('.');
        }

        Header.CompressedSize = Header.OriginalSize;
        Header.OriginalCrc ^= CrcMask;
        return true;
    }

    // Unstore Method
    public static ulong Unstore(FileStream destination)
    {
        ulong crc = CrcMask;
        uint count;
        byte[] buffer = new byte[256];
        int pacifier = 0;

        while (Header.OriginalSize != 0)
        {
            //count = (Header.OriginalSize > 256 ? 256 : Header.OriginalSize);
            if (Header.OriginalSize > 256)
                count = 256;
            else
                count = (uint)Header.OriginalSize;

            int bytesRead = InputCarFile?.Read(buffer, 0, (int)count) ?? 0;
            if (bytesRead != count)
                FatalError("Can't read from input CAR file");

            destination.Write(buffer, 0, (int)count);
            crc = CalculateBlockCRC32(count, (uint)crc, buffer);

            if (destination != Stream.Null && ((pacifier++) & 15) == 0)
                Console.Error.Write('.');

            Header.OriginalSize -= (ulong)count;
        }

        return crc ^ CrcMask;
    } */

    public static void ListCarFileEntry()
    {
        string[] methods = { "Stored", "LZSS" };

        Console.WriteLine(
            $"{Header.FileName,-20} " +
            $"{Header.OriginalSize,10} " +
            $"{Header.CompressedSize,11} " +
            $"{RatioInPercent(Header.CompressedSize, Header.OriginalSize),5}% " +
            $"  {Header.OriginalCrc:X8} " +
            $"{methods[Header.CompressionMethod - 1],5}");
    }

    public static void SkipOverFileFromInputCar()
    {
        long compsize = (long)Header.CompressedSize;
        InputCarFile.Seek((long)Header.CompressedSize, SeekOrigin.Current);
    }
    public  void BuildCRCTable()
    {
        int i;
        int j;
        ulong value;

        for (i = 0; i <= 255; i++)
        {
            value = (uint)i; //Convert.ToUInt64(i);
            for (j = 8; j > 0; j--)
            {
                if ((value & 1) != 0)
                    value = (value >> 1) ^ Crc32Polynomial;
                else
                    value >>= 1;
            }
            Ccitt32Table[i] = value;
        }
    }
    public static ulong CalculateBlockCRC32(uint count, ulong crc, byte[] buffer)
    {
        for (int i = 0; i < count; i++)
        {
            ulong temp1 = (crc >> 8) & 0x00FFFFFF;
            ulong temp2 = Ccitt32Table[(crc ^ buffer[i]) & 0xFF];
            crc = temp1 ^ temp2;
        }
        return crc;
    }

    public static ulong UpdateCharacterCRC32(ulong crc, int c)
    {
        ulong temp1;
        ulong temp2;

        temp1 = ((crc >> 8) & 0x00FFFFFF);
        temp2 = Ccitt32Table[((int)crc ^ c) & 0xff];
        crc = temp1 ^ temp2;
        return (crc);
    }
    public static void InitTree(int r)
    {
        // Since the tree is static data, every node is already 0 (UNUSED).
        // However, we explicitly initialize all nodes for clarity.
        for (int i = 0; i < WINDOW_SIZE + 1; i++)
        {
            Tree[i].Parent = UNUSED;
            Tree[i].LargerChild = UNUSED;
            Tree[i].SmallerChild = UNUSED;
        }

        // Insert the initial phrase to establish the root.
        Tree[TREE_ROOT].LargerChild = r;
        Tree[r].Parent = TREE_ROOT;
        Tree[r].LargerChild = UNUSED;
        Tree[r].SmallerChild = UNUSED;
    }

    /// Contracts the node by replacing the old node with a new node.
    /// </summary>
    public static void ContractNode(int oldNode, int newNode)
    {
        // Set the new node's parent to the old node's parent.
        Tree[newNode].Parent = Tree[oldNode].Parent;

        int parent = Tree[oldNode].Parent;
        // Replace the link from the parent to the old node with a link to the new node.
        if (Tree[parent].LargerChild == oldNode)
        {
            Tree[parent].LargerChild = newNode;
        }
        else
        {
            Tree[parent].SmallerChild = newNode;
        }
        // Mark the old node as unused.
        Tree[oldNode].Parent = UNUSED;
    }

    /// <summary>
    /// Replaces the old node with the new node when the new node was not previously in the tree.
    /// </summary>
    public static void ReplaceNode(int oldNode, int newNode)
    {
        int parent = Tree[oldNode].Parent;
        if (Tree[parent].SmallerChild == oldNode)
        {
            Tree[parent].SmallerChild = newNode;
        }
        else
        {
            Tree[parent].LargerChild = newNode;
        }
        // Copy the entire node data.
        Tree[newNode] = Tree[oldNode];
        // Update the children of the new node so that their parent is set to newNode.
        int smaller = Tree[newNode].SmallerChild;
        int larger = Tree[newNode].LargerChild;
        if (smaller != UNUSED)
        {
            Tree[smaller].Parent = newNode;
        }
        if (larger != UNUSED)
        {
            Tree[larger].Parent = newNode;
        }
        // Mark the old node as unused.
        Tree[oldNode].Parent = UNUSED;
    }

    /// <summary>
    /// Finds the next smallest node after the given node.
    /// Assumes that the node has a smaller child.
    /// </summary>
    public static int FindNextNode(int node)
    {
        int next = Tree[node].SmallerChild;
        while (Tree[next].LargerChild != UNUSED)
        {
            next = Tree[next].LargerChild;
        }
        return next;
    }

    /// <summary>
    /// Deletes the node from the binary tree using the classic deletion algorithm.
    /// </summary>
    public static void DeleteString(int p)
    {
        if (Tree[p].Parent == UNUSED)
            return;

        if (Tree[p].LargerChild == UNUSED)
        {
            ContractNode(p, Tree[p].SmallerChild);
        }
        else if (Tree[p].SmallerChild == UNUSED)
        {
            ContractNode(p, Tree[p].LargerChild);
        }
        else
        {
            int replacement = FindNextNode(p);
            DeleteString(replacement);
            ReplaceNode(p, replacement);
        }
    }

    /// Adds the new node to the binary tree. While doing so, it searches for the best match among all
    /// existing nodes in the tree and returns the match length. If a duplicate is found, the old node is replaced.
    public static int AddString(int newNode, out int matchPosition)
    {
        // If we've reached the end-of-stream, return 0.
        if (newNode == END_OF_STREAM)
        {
            matchPosition = 0;
            return 0;
        }

        int testNode = Tree[TREE_ROOT].LargerChild;
        int matchLength = 0;
        matchPosition = 0;

        // Infinite loop; exit occurs when the appropriate child pointer is unused.
        while (true)
        {
            int i;
            int delta = 0;

            // Compare the strings in the window.
            for (i = 0; i < LOOK_AHEAD_SIZE; i++)
            {
                int indexNew = ModWindow(newNode + i);
                int indexTest = ModWindow(testNode + i);
                delta = Window[indexNew] - Window[indexTest];
                if (delta != 0)
                    break;
            }

            if (i >= matchLength)
            {
                matchLength = i;
                matchPosition = testNode;
                if (matchLength >= LOOK_AHEAD_SIZE)
                {
                    ReplaceNode(testNode, newNode);
                    return matchLength;
                }
            }

            // Decide which subtree to follow.
            if (delta >= 0)
            {
                // Check if the larger child link is unused.
                if (Tree[testNode].LargerChild == UNUSED)
                {
                    Tree[testNode].LargerChild = newNode;
                    Tree[newNode].Parent = testNode;
                    Tree[newNode].LargerChild = UNUSED;
                    Tree[newNode].SmallerChild = UNUSED;
                    return matchLength;
                }
                testNode = Tree[testNode].LargerChild;
            }
            else
            {
                // Check if the smaller child link is unused.
                if (Tree[testNode].SmallerChild == UNUSED)
                {
                    Tree[testNode].SmallerChild = newNode;
                    Tree[newNode].Parent = testNode;
                    Tree[newNode].LargerChild = UNUSED;
                    Tree[newNode].SmallerChild = UNUSED;
                    return matchLength;
                }
                testNode = Tree[testNode].SmallerChild;
            }
        }
    }
    public static int Putc(int ch, FileStream fileStream)
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
    public static int Getc(FileStream fileStream)
    {
        if (fileStream == null)
            throw new ArgumentNullException(nameof(fileStream));

        if (!fileStream.CanRead)
            throw new IOException("File stream is not readable");

        int nextChar = fileStream.ReadByte();
        return nextChar;
    }
    public static uint BufferOffset;
    //public static char[] DataBuffer = new char[17];
    public static byte[] DataBuffer = new byte[17];
    public static int FlagBitMask;

    public static void InitOutputBuffer()
    {
        DataBuffer[0] = 0;
        FlagBitMask = 1;
        BufferOffset = 1;
    }
    private bool FlushOutputBuffer()
    {
        if (BufferOffset == 1)
            return true;
        Header.CompressedSize += BufferOffset;
        if ((Header.CompressedSize) >= Header.OriginalSize)
            return false;

        OutputCarFile.Write(DataBuffer, 0, DataBuffer.Length);
        InitOutputBuffer();
        return true;
    }

    private bool OutputChar(int data)
    {
        DataBuffer[BufferOffset++] = (byte)data;
        DataBuffer[0] |= (byte)FlagBitMask;
        FlagBitMask <<= 1;
        return FlagBitMask == 0x100 ? FlushOutputBuffer() : true;
    }

    private bool OutputPair(int position, int length)
    {
        DataBuffer[BufferOffset] = (byte)(length << 4);
        DataBuffer[BufferOffset++] |= (byte)(position >> 8);
        DataBuffer[BufferOffset++] = (byte)(position & 0xff);
        FlagBitMask <<= 1;
        return FlagBitMask == 0x100 ? FlushOutputBuffer() : true;
    }
    public static void InitInputBuffer()
    {
        FlagBitMask = 1;
        DataBuffer[0] = (byte)Getc(InputCarFile);
    }

    public static int InputBit()
    {
        if (FlagBitMask == 0x100)
            InitInputBuffer();
        FlagBitMask <<= 1;
        return ((DataBuffer[0]) & (FlagBitMask >> 1));
    }

    public static void FatalError(string message, params object[] args)
    {
        Console.Error.WriteLine(message, args);
        Environment.Exit(1);
    }

    /*
    private bool LZSSCompress(Stream inputTextFile)
    {
        int i;
        int c;
        int lookAheadBytes;
        int currentPosition = 1;
        int replaceCount;
        int matchLength = 0;
        int matchPosition = 0;

        CurrentHeader.compressed_size = 0;
        CurrentHeader.original_crc = CRC_MASK;
        InitOutputBuffer();

        // Initialize window
        for (i = 0; i < LOOK_AHEAD_SIZE; i++)
        {
            c = inputTextFile.ReadByte();
            if (c == -1)
                break;

            window[currentPosition + i] = (byte)c;
            CurrentHeader.original_crc = UpdateCharacterCRC32(CurrentHeader.original_crc, c);
        }

        lookAheadBytes = i;
        InitTree(currentPosition);

        while (lookAheadBytes > 0)
        {
            if (matchLength > lookAheadBytes)
                matchLength = lookAheadBytes;

            if (matchLength <= BREAK_EVEN)
            {
                replaceCount = 1;
                if (!OutputChar(window[currentPosition]))
                    return false;
            }
            else
            {
                if (!OutputPair(matchPosition, matchLength - (BREAK_EVEN + 1)))
                    return false;
                replaceCount = matchLength;
            }

            for (i = 0; i < replaceCount; i++)
            {
                DeleteString(ModWindow(currentPosition + LOOK_AHEAD_SIZE));

                c = inputTextFile.ReadByte();
                if (c == -1)
                {
                    lookAheadBytes--;
                }
                else
                {
                    CurrentHeader.original_crc = UpdateCharacterCRC32(CurrentHeader.original_crc, c);
                    window[ModWindow(currentPosition + LOOK_AHEAD_SIZE)] = (byte)c;
                }

                currentPosition = ModWindow(currentPosition + 1);
                if (currentPosition == 0)
                    Console.Error.Write('.');

                if (lookAheadBytes > 0)
                    matchLength = AddString(currentPosition, out matchPosition);
            }
        }

        CurrentHeader.original_crc ^= CRC_MASK;
        return FlushOutputBuffer();
    } */

    // LZSS Compression and Expansion Methods
    private bool LZSSCompress(Stream inputTextStream)
    {
        int i;
        int c;
        int lookAheadBytes;
        int currentPosition = 1;
        int replaceCount;
        int matchLength = 0;
        int matchPosition = 0;

        Header.CompressedSize = 0;
        Header.OriginalCrc = CrcMask;
        InitOutputBuffer();

        // Read LOOK_AHEAD_SIZE bytes into the window.
        for (i = 0; i < LOOK_AHEAD_SIZE; i++)
        {
            c = inputTextStream.ReadByte();
            //c = Getc(inputTextStream);
            if (c == -1)
                break;
            Window[currentPosition + i] = (byte)c;
            Header.OriginalCrc = UpdateCharacterCRC32(Header.OriginalCrc, c);
        }
        lookAheadBytes = i;
        InitTree(currentPosition);

        matchLength = 0;
        matchPosition = 0;
        while (lookAheadBytes > 0)
        {
            if (matchLength > lookAheadBytes)
                matchLength = lookAheadBytes;
            if (matchLength <= BREAK_EVEN)
            {
                replaceCount = 1;
                if (OutputChar(Window[currentPosition]))
                    return false;
            }
            else
            {
                if (OutputPair(matchPosition, matchLength - (BREAK_EVEN + 1)))
                    return false;
                replaceCount = matchLength;
            }
            for (i = 0; i < replaceCount; i++)
            {
                DeleteString(ModWindow(currentPosition + LOOK_AHEAD_SIZE));
                c = inputTextStream.ReadByte();
                //c = Getc(inputTextStream);
                if (c == -1)
                {
                    lookAheadBytes--;
                }
                else
                {
                    Header.OriginalCrc = UpdateCharacterCRC32(Header.OriginalCrc, c);
                    Window[ModWindow(currentPosition + LOOK_AHEAD_SIZE)] = (byte)c;
                }
                currentPosition = ModWindow(currentPosition + 1);
                if (currentPosition == 0)
                    Console.Error.Write('.');
                if (lookAheadBytes > 0)
                    matchLength = AddString(currentPosition, out matchPosition);
            }
        }
        Header.OriginalCrc ^= CrcMask;
        return FlushOutputBuffer();
    }

    private ulong LZSSExpand(Stream output)
    {
        int current_position = 1;
        ulong output_count = 0;
        ulong crc = CrcMask;
        InitInputBuffer();
;
        while (output_count < Header.OriginalSize)
        {
            if (InputBit() != 0)
            {
                //c = Getc(InputCarFile);
                //Putc(c, output);
                int c = InputCarFile.ReadByte();
                output.WriteByte((byte)c);
                output_count++;
                crc = UpdateCharacterCRC32(crc, c);
                Window[current_position] = (byte)c;
                current_position = ModWindow(current_position + 1);
                if (current_position == 0 && output != Console.OpenStandardOutput())
                {
                    Console.Write('.');
                }
            }
            else
            {
                //match_length = Getc(InputCarFile);
                //match_position = Getc(InputCarFile);
                int match_length = InputCarFile.ReadByte();
                int match_position = InputCarFile.ReadByte();
                match_position |= (match_length & 0xf) << 8;
                match_length >>= 4;
                match_length += BREAK_EVEN;
                output_count += (ulong)(match_length + 1);
                for (int i = 0; i <= match_length; i++)
                {
                    byte c = Window[ModWindow(match_position + i)];
                    //Putc(c, output);
                    output.WriteByte(c);
                    crc = UpdateCharacterCRC32(crc, c);
                    Window[current_position] = c;
                    current_position = ModWindow(current_position + 1);
                    if (current_position == 0 && output != Console.OpenStandardOutput())
                        Console.Write('.');
                }
            }
        }
        return (crc ^ CrcMask);
    }
    public static void Main(string[] args)
    {
        CarProcessor processor = new CarProcessor();

        Console.Error.Write("CARMAN 1.0 : ");
        processor.BuildCRCTable();

        char command = processor.ParseArguments(args.Length, args); //ParseArguments(args);
        Console.Error.WriteLine();

        processor.OpenArchiveFiles(args[1], command);
        //BuildFileList(args.Skip(2).ToArray(), command);
        processor.BuildFileList(args.Length, args, command);

        int count = 0;
        if (command == 'A')
        {
            count = processor.AddFileListToArchive();
        }

        if (command == 'L')
        {
            processor.PrintListTitles();
        }

        count = processor.ProcessAllFilesInInputCar(command, count);

        if (CarProcessor.OutputCarFile != null && count != 0)
        {
            processor.WriteEndOfCarHeader();
            CarProcessor.OutputCarFile.Close();

            try
            {
                if (File.Exists(CarProcessor.CarFileName))
                {
                    File.Delete(CarProcessor.CarFileName);
                }
                File.Move(CarProcessor.TempFileName, CarProcessor.CarFileName);
            }
            catch (Exception ex)
            {
                CarProcessor.FatalError($"Can't rename temporary file: {ex.Message}");
            }
        }

        if (command != 'P')
        {
            Console.WriteLine($"\n{count} file{(count == 1 ? "" : "s")}");
        }
        else
        {
            Console.Error.WriteLine($"\n{count} file{(count == 1 ? "" : "s")}");
        }
    }
}
