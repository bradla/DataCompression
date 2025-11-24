using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CarManager
{
    public class CarMan
    {
        private const int BASE_HEADER_SIZE = 19;
        private const ulong CRC_MASK = 0xFFFFFFFF;
        private const uint CRC32_POLYNOMIAL = 0xEDB88320;
        private const int FILENAME_MAX = 260; // Increased from 128 for modern systems

        private struct Header
        {
            public string file_name;
            public char compression_method;
            public ulong original_size;
            public ulong compressed_size;
            public ulong original_crc;
            public ulong header_crc;
        }

        private string TempFileName;
        private FileStream InputCarFile;
        private string CarFileName;
        private FileStream OutputCarFile;
        private Header CurrentHeader;
        private List<string> FileList = new List<string>();
        private uint[] Ccitt32Table = new uint[256];

        // LZSS Constants
        private const int INDEX_BIT_COUNT = 12;
        private const int LENGTH_BIT_COUNT = 4;
        private const int WINDOW_SIZE = 1 << INDEX_BIT_COUNT;
        private const int RAW_LOOK_AHEAD_SIZE = 1 << LENGTH_BIT_COUNT;
        private const int BREAK_EVEN = (1 + INDEX_BIT_COUNT + LENGTH_BIT_COUNT) / 9;
        private const int LOOK_AHEAD_SIZE = RAW_LOOK_AHEAD_SIZE + BREAK_EVEN;
        private const int TREE_ROOT = WINDOW_SIZE;
        private const int END_OF_STREAM = 0;
        private const int UNUSED = 0;

        private byte[] window = new byte[WINDOW_SIZE];
        // Blocked I/O for LZSS
        private byte[] dataBuffer = new byte[17];
        private int flagBitMask;
        private int bufferOffset;

        private struct TreeNode
        {
            public int parent;
            public int smaller_child;
            public int larger_child;
        }

        private TreeNode[] tree = new TreeNode[WINDOW_SIZE + 1];
        public static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                UsageExit();
                return;
            }

            CarMan carMan = new CarMan();
            carMan.Run(args);
        }
        private int AddFileListToArchive()
        {
            int count = 0;
            foreach (string file in FileList)
            {
                try
                {
                    Console.WriteLine("adding: " + file);
                    using (FileStream inputTextFile = new FileStream(file, FileMode.Open, FileAccess.Read))
                    {
                        /*using (StreamReader reader = new StreamReader(inputTextFile))
			{
			    string text = reader.ReadToEnd();
			    Console.WriteLine(text);
			}*/

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
                            CurrentHeader = new Header();
                            CurrentHeader.file_name = fileNameOnly;
                            Insert(inputTextFile, "Adding");
                            count++;
                        }
                    }
                }
                catch (Exception ex)
                {
                  /*if (OutputCarFile != null)
            	  {
                    OutputCarFile.Close();
                    try { File.Delete(TempFileName); } catch { }
                  }
                  Console.WriteLine("Could not open {0} to add to CAR file {1}", file, ex.Message);
                  Environment.Exit(1);*/
                  FatalError("Could not open {0} to add to CAR file {ex.Message}", file);
                }
            }
            return count;
        }

        private int AddString(int newNode, out int matchPosition)
        {
            int i;
            int testNode = tree[TREE_ROOT].larger_child;
            int matchLength = 0;
            matchPosition = 0;
            int delta = 0;

            if (newNode == END_OF_STREAM)
                return (0);
            while (true)
            {
                for (i = 0; i < LOOK_AHEAD_SIZE; i++)
                {
                    delta = window[ModWindow(newNode + i)] - window[ModWindow(testNode + i)];
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

                ref int child = ref (delta >= 0) ? ref tree[testNode].larger_child : ref tree[testNode].smaller_child;

                if (child == UNUSED)
                {
                    child = newNode;
                    tree[newNode].parent = testNode;
                    tree[newNode].larger_child = UNUSED;
                    tree[newNode].smaller_child = UNUSED;
                    return matchLength;
                }
                testNode = child;
            }
        }
        private void BuildFileList(string[] files, char command)
        {
            if (files.Length == 0)
            {
                FileList.Add("*");
            }
            else
            {
                foreach (string file in files)
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
                                FileList.Add(foundFile); //.ToLower());
                            }
                        }
                        catch
                        {
                            FileList.Add(file); //.ToLower());
                        }
                    }
                    else
                    {
                        FileList.Add(file); //.ToLower());
                    }

                    if (FileList.Count > 99)
                    {
                        FatalError("Too many file names");
                    }
                }
            }
        }
        private void BuildCRCTable()
        {
            for (int i = 0; i <= 255; i++)
            {
                uint value = (uint)i;
                for (int j = 8; j > 0; j--)
                {
                    if ((value & 1) != 0)
                        value = (value >> 1) ^ CRC32_POLYNOMIAL;
                    else
                        value >>= 1;
                }
                Ccitt32Table[i] = value;
            }
        }
        private void ContractNode(int oldNode, int newNode)
        {
            tree[newNode].parent = tree[oldNode].parent;
            if (tree[tree[oldNode].parent].larger_child == oldNode)
                tree[tree[oldNode].parent].larger_child = newNode;
            else
                tree[tree[oldNode].parent].smaller_child = newNode;

            tree[oldNode].parent = UNUSED;
        }

        private ulong CalculateBlockCRC32(uint count, ulong crc, byte[] buffer)
        {
            ulong temp1;
            ulong temp2;

            for (int i = 0; i < count; i++)
            {
                temp1 = (crc >> 8) & 0x00FFFFFF;
                temp2 = Ccitt32Table[(crc ^ buffer[i]) & 0xff];
                crc = temp1 ^ temp2;
            }
            return crc;
        }
        private void CopyFileFromInputCar()
        {
            byte[] buffer = new byte[256];
            uint count = 0;

            WriteFileHeader();
            while (CurrentHeader.compressed_size != 0)
            {
                if (CurrentHeader.compressed_size < 256)
                    count = (uint)CurrentHeader.compressed_size;//Math.Min(CurrentHeader.compressed_size, 256);
                else
                    count = 256;

                if (InputCarFile.Read(buffer, 0, (int)count) != count)
                    FatalError("CopyFileFromInputCar Error reading input file {0}", CurrentHeader.file_name);

                CurrentHeader.compressed_size -= count;

                try
                {
                    OutputCarFile.Write(buffer, 0, (int)count);
                }
                catch (IOException)
                {
                    FatalError("CopyFileFromInputCar Error writing to output CAR file");
                }
            }
        }
        private void DeleteString(int p)
        {
            if (tree[p].parent == UNUSED)
                return;

            if (tree[p].larger_child == UNUSED)
                ContractNode(p, tree[p].smaller_child);
            else if (tree[p].smaller_child == UNUSED)
                ContractNode(p, tree[p].larger_child);
            else
            {
                int replacement = FindNextNode(p);
                DeleteString(replacement);
                ReplaceNode(p, replacement);
            }
        }
        private void Extract(Stream destination)
        {
            Console.Error.Write("{0,-20} ", CurrentHeader.file_name);
            bool error = false;

            Stream outputTextFile = destination;
            bool shouldClose = false;
            uint crc;

            try
            {
                if (destination == null)
                {
                    try
                    {
                        outputTextFile = new FileStream(CurrentHeader.file_name, FileMode.Create, FileAccess.Write);
                        shouldClose = true;
                    }
                    catch (Exception ex)
                    {
                        SkipOverFileFromInputCar();
                        Console.Error.WriteLine($"Can't open {CurrentHeader.file_name}: {ex.Message}");
                    }
                } else outputTextFile = destination;

                switch (CurrentHeader.compression_method)
                {
                    case (char)1:
                        crc = (uint)Unstore(outputTextFile);
                        break;
                    case (char)2:
                        crc = (uint)LZSSExpand(outputTextFile);
                        break;
                    default:
                        Console.Error.Write("Unknown method: {0}", CurrentHeader.compression_method);
                        SkipOverFileFromInputCar();
                        error = true;
                        crc = (uint)CurrentHeader.original_crc;
                        break;
                }

                if (crc != CurrentHeader.original_crc)
                {
                    Console.Error.Write("CRC error reading data");
                    error = true;
                }

                if (!error)
                    Console.Error.Write(" OK");
            }
            catch
            {
                Console.Error.Write("Can't open {0}", CurrentHeader.file_name);
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
                        try { File.Delete(CurrentHeader.file_name); } catch { }
                    }
                }
            }
        }
        private int FindNextNode(int node)
        {
            int next = tree[node].smaller_child;
            while (tree[next].larger_child != UNUSED)
                next = tree[next].larger_child;
            return next;
        }
        private void FatalError(string message, params object[] args)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(message); //, args);
            foreach (var a in args) {
            	Console.WriteLine(a);
            }
            Console.Error.WriteLine();

            if (OutputCarFile != null)
            {
                OutputCarFile.Close();
                try { File.Delete(TempFileName); } catch { }
            }

            Environment.Exit(1);
        }

        private int FlushOutputBuffer()
        {
            if (bufferOffset == 1)
                return 1;

            CurrentHeader.compressed_size += (uint)bufferOffset;
            if (CurrentHeader.compressed_size >= CurrentHeader.original_size)
                return 0;

            OutputCarFile.Write(dataBuffer, 0, bufferOffset); //dataBuffer.Length);  //
            InitOutputBuffer();
            return 1;
        }
        private void Insert(Stream inputTextFile, string operation)
        {
            Console.Error.Write("{0} {1,-20}", operation, CurrentHeader.file_name);

            long savedPositionOfHeader = OutputCarFile.Position;
            CurrentHeader.compression_method = (char)2;
            WriteFileHeader();

            long savedPositionOfFile = OutputCarFile.Position;
            inputTextFile.Seek(0, SeekOrigin.End);
            CurrentHeader.original_size = (uint)inputTextFile.Length;
            inputTextFile.Seek(0, SeekOrigin.Begin);
            //inputTextFile.Seek(0, SeekOrigin.Begin);

            if (LZSSCompress(inputTextFile) == 0)
            {
                CurrentHeader.compression_method = (char)1;
                OutputCarFile.Seek(savedPositionOfFile, SeekOrigin.Begin);
                inputTextFile.Seek(0, SeekOrigin.Begin);
                Store(inputTextFile);
            }
            inputTextFile.Close();
            OutputCarFile.Seek(savedPositionOfHeader, SeekOrigin.Begin);
            WriteFileHeader();
            OutputCarFile.Seek(0, SeekOrigin.End);

            Console.WriteLine(" {0}%", RatioInPercent(CurrentHeader.compressed_size, CurrentHeader.original_size));
        }
        private void InitInputBuffer()
        {
            flagBitMask = 1;
            dataBuffer[0] = (byte)InputCarFile.ReadByte();
        }

        private int InputBit()
        {
            if (flagBitMask == 0x100)
                InitInputBuffer();

            flagBitMask <<= 1;
            int result = (dataBuffer[0] & (flagBitMask >> 1));
            return result;
        }
        private void InitTree(int r)
        {
            for (int i = 0; i < WINDOW_SIZE + 1; i++)
            {
                tree[i].parent = UNUSED;
                tree[i].larger_child = UNUSED;
                tree[i].smaller_child = UNUSED;
            }

            tree[TREE_ROOT].larger_child = r;
            tree[r].parent = TREE_ROOT;
            tree[r].larger_child = UNUSED;
            tree[r].smaller_child = UNUSED;
        }
        private void InitOutputBuffer()
        {
            dataBuffer[0] = 0;
            flagBitMask = 1;
            bufferOffset = 1;
        }

        private ulong LZSSExpand(Stream output)
        {
            int c;
            int currentPosition = 1;
            uint crc = (uint)CRC_MASK;
            ulong outputCount = 0;
            int matchLength;
            int matchPosition;

            InitInputBuffer();

            while (outputCount < CurrentHeader.original_size)
            {
                if (InputBit() == 1)
                {
                    c = InputCarFile.ReadByte();
                    output.WriteByte((byte)c);
                    outputCount++;
                    crc = (uint)UpdateCharacterCRC32(crc, c);
                    window[currentPosition] = (byte)c;
                    currentPosition = ModWindow(currentPosition + 1);

                    if (currentPosition == 0 && output != Console.OpenStandardOutput())
                        Console.Error.Write('.');
                }
                else
                {
                    matchLength = InputCarFile.ReadByte();
                    matchPosition = InputCarFile.ReadByte();
                    matchPosition |= (matchLength & 0xf) << 8;
                    matchLength >>= 4;
                    matchLength += BREAK_EVEN;
                    outputCount += (ulong)(matchLength + 1);

                    for (int i = 0; i <= matchLength; i++)
                    {
                        c = window[ModWindow(matchPosition + i)];
                        output.WriteByte((byte)c);
                        crc = (uint)UpdateCharacterCRC32(crc, c);
                        window[currentPosition] = (byte)c;
                        currentPosition = ModWindow(currentPosition + 1);

                        if (currentPosition == 0 && output != Console.OpenStandardOutput())
                            Console.Error.Write('.');
                    }
                }
            }

            return crc ^ CRC_MASK;
        }

        private int LZSSCompress(Stream inputTextFile)
        {
            int i;
            int c;
            int lookAheadBytes;
            int currentPosition = 1;
            int replaceCount=0;
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
                if (matchLength > lookAheadBytes) matchLength = lookAheadBytes;

                if (matchLength <= BREAK_EVEN)
                {
                    replaceCount = 1;
                    if (OutputChar(window[currentPosition]) == 0)
                        return 0;
                }
                else
                {
                    if (OutputPair(matchPosition, matchLength - (BREAK_EVEN + 1)) == 0)
                        return 0;

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
        }
        
        private void ListCarFileEntry()
        {
            string[] methods = { "Stored", "LZSS" };
            Console.WriteLine("{0,-20} {1,10}  {2,10}  {3,4}%     {4:X8}   {5}",
                CurrentHeader.file_name,
                CurrentHeader.original_size,
                CurrentHeader.compressed_size,
                RatioInPercent(CurrentHeader.compressed_size, CurrentHeader.original_size),
                CurrentHeader.original_crc,
                methods[CurrentHeader.compression_method - 1]);
        }
        private int ModWindow(int a) => a & (WINDOW_SIZE - 1);
        private void OpenArchiveFiles(string name, char command)
        {
            CarFileName = name;

            // Try to open the input file
            try
            {
                InputCarFile = new FileStream(CarFileName, FileMode.Open, FileAccess.Read);
            }
            catch
            {
                // If not found and no extension, try adding .car
                if (Path.GetExtension(CarFileName) == "")
                {
                    CarFileName += ".car";
                    try
                    {
                        InputCarFile = new FileStream(CarFileName, FileMode.Open, FileAccess.Read);
                    }
                    catch
                    {
                        InputCarFile = null;
                    }
                }
                else
                {
                    InputCarFile = null;
                }
            }

            if (InputCarFile == null && command != 'A')
                FatalError("Can't open archive '{0}'", CarFileName);

            if (command == 'A' || command == 'R' || command == 'D')
            {
                string tempDir = Path.GetTempPath();
                string tempName;
                int i;
                for (i = 0; i < 10; i++)
                {
                    tempName = Path.Combine(tempDir, $"{Path.GetFileNameWithoutExtension(CarFileName)}.$${i}");
                    Console.WriteLine("temp: {0}", tempName);
                    if (!File.Exists(tempName))
                        break;
                }

                if (i == 10)
                    FatalError("Can't open temporary file");

                TempFileName = Path.Combine(tempDir, $"{Path.GetFileNameWithoutExtension(CarFileName)}.$${i}");
                try
                {
                    OutputCarFile = new FileStream(TempFileName, FileMode.Create, FileAccess.Write);
                }
                catch
                {
                    FatalError("Can't open temporary file {0}", TempFileName);
                }
            }
        }
        private int OutputChar(int data)
        {
            dataBuffer[bufferOffset++] = (byte)data;
            dataBuffer[0] |= (byte)flagBitMask;
            flagBitMask <<= 1;
            //Console.WriteLine($"{dataBuffer} flagbit {flagBitMask}");
            if (flagBitMask == 0x100) 
                return (FlushOutputBuffer());
            else
                return 1;
            //return flagBitMask == 0x100 ? FlushOutputBuffer() : true;
        }

        private int OutputPair(int position, int length)
        {
            dataBuffer[bufferOffset] = (byte)(length << 4);
            dataBuffer[bufferOffset++] |= (byte)(position >> 8);
            dataBuffer[bufferOffset++] = (byte)(position & 0xff);
            flagBitMask <<= 1;
            return flagBitMask == 0x100 ? FlushOutputBuffer() : 1;
        }
        private char ParseArguments(string[] args)
        {
            if (args.Length < 2 || args[0].Length > 1)
            {
                UsageExit();
                return '\0';
            }

            char command = char.ToUpper(args[0][0]);
            switch (command)
            {
                case 'X':
                    Console.Error.Write("Extracting files");
                    break;
                case 'R':
                    Console.Error.Write("Replacing files");
                    break;
                case 'P':
                    Console.Error.Write("Print files to stdout");
                    break;
                case 'T':
                    Console.Error.Write("Testing integrity of files");
                    break;
                case 'L':
                    Console.Error.Write("Listing archive contents");
                    break;
                case 'A':
                    if (args.Length <= 2)
                        UsageExit();
                    Console.Error.Write("Adding/replacing files to archive");
                    break;
                case 'D':
                    if (args.Length <= 2)
                        UsageExit();
                    Console.Error.Write("Deleting files from archive");
                    break;
                default:
                    UsageExit();
                    break;
            }
            return command;
        }

        private int ProcessAllFilesInInputCar(char command, int count)
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
                    int matched = SearchFileList(CurrentHeader.file_name);

                    switch (command)
                    {
                        case 'D':
                            if (matched == 1)
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
                            if (matched == 1)
                                SkipOverFileFromInputCar();
                            else
                                CopyFileFromInputCar();
                            break;
                        case 'L':
                            if (matched == 1)
                            {
                                ListCarFileEntry();
                                count++;
                            }
                            SkipOverFileFromInputCar();
                            break;
                        case 'P':
                        case 'X':
                        case 'T':
                            if (matched == 1)
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
                            if (matched == 1)
                            {
                                try
                                {
                                    using (FileStream inputTextFile = new FileStream(CurrentHeader.file_name, FileMode.Open, FileAccess.Read))
                                    {
                                        SkipOverFileFromInputCar();
                                        Insert(inputTextFile, "Replacing");
                                        count++;
                                    }
                                }
                                catch
                                {
                                    Console.Error.Write($"Could not find {CurrentHeader.file_name} for replacement, skipping");
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

        private void PrintListTitles()
        {
            Console.WriteLine();
            Console.WriteLine("                           Original  Compressed");
            Console.WriteLine("     Filename                Size       Size      Ratio    CRC-32    Method");
            Console.WriteLine("----------------------     --------   ----------  -----   --------   ------");
        }

        private int RatioInPercent(ulong compressed, ulong original)
        {
            if (original == 0)
                return 0;
            return (int)(100 - (100L * compressed) / original);
        }
        private void Run(string[] args)
        {
            Console.Error.Write("CARMAN 1.0 : ");
            BuildCRCTable();

            char command = ParseArguments(args);
            Console.Error.WriteLine();

            OpenArchiveFiles(args[1], command);
            BuildFileList(args.Skip(2).ToArray(), command);

            int count = 0;
            if (command == 'A')
            {
                count = AddFileListToArchive();
            }

            if (command == 'L')
            {
                PrintListTitles();
            }

            count = ProcessAllFilesInInputCar(command, count);

            if (OutputCarFile != null && count != 0)
            {
                WriteEndOfCarHeader();
                OutputCarFile.Close();
                if (InputCarFile != null)
                {
                    InputCarFile.Close();
                    InputCarFile.Dispose(); // optional, ensures all resources are released
                }

                try
                {
                    if (File.Exists(CarFileName))
                    {
                        File.Delete(CarFileName);
                    }
                    Console.WriteLine("Copy temp to original {0}", TempFileName);
                    File.Move(TempFileName, CarFileName);
                }
                catch (Exception ex)
                {
                    FatalError($"Can't rename temporary file zzz: {ex.Message}");
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
        private void ReplaceNode(int oldNode, int newNode)
        {
            int parent = tree[oldNode].parent;
            if (tree[parent].smaller_child == oldNode)
                tree[parent].smaller_child = newNode;
            else
                tree[parent].larger_child = newNode;

            tree[newNode] = tree[oldNode];
            tree[tree[newNode].smaller_child].parent = newNode;
            tree[tree[newNode].larger_child].parent = newNode;
            tree[oldNode].parent = UNUSED;
        }
        private int ReadFileHeader()
        {
            byte[] headerData = new byte[17];
            ulong headerCrc;
            int i = 0;
            StringBuilder fileName = new StringBuilder();
            while (true)
            {
                int c = InputCarFile.ReadByte();
                if (c == -1) return 0;

                if (c == 0)
                    break;

                fileName.Append((char)c);
                if (++i == FILENAME_MAX)
                    FatalError("File name exceeded maximum in header");
            }

            if (fileName.Length == 0)
                return 0;

            CurrentHeader.file_name = fileName.ToString();
            headerCrc = CalculateBlockCRC32((uint)(i + 1), CRC_MASK, Encoding.ASCII.GetBytes(CurrentHeader.file_name + '\0'));

            if (InputCarFile.Read(headerData, 0, 17) != 17)
                return 0;

            CurrentHeader.compression_method = (char)headerData[0];
            CurrentHeader.original_size = BitConverter.ToUInt32(headerData, 1);
            CurrentHeader.compressed_size = BitConverter.ToUInt32(headerData, 5);
            CurrentHeader.original_crc = BitConverter.ToUInt32(headerData, 9);
            CurrentHeader.header_crc = BitConverter.ToUInt32(headerData, 13);

            headerCrc = CalculateBlockCRC32(13, headerCrc, headerData);
            headerCrc ^= CRC_MASK;

            if (CurrentHeader.header_crc != headerCrc)
                FatalError("Header checksum error for file {0}", CurrentHeader.file_name);

            return 1;
        }
        private bool Store(Stream inputTextFile)
        {
            byte[] buffer = new byte[256];
            int pacifier = 0;
            CurrentHeader.original_crc = CRC_MASK;

            uint bytesRead;
            while ((bytesRead = (uint)inputTextFile.Read(buffer, 0, 256)) > 0)
            {
                OutputCarFile.Write(buffer, 0, (int)bytesRead);
                CurrentHeader.original_crc = CalculateBlockCRC32(bytesRead, CurrentHeader.original_crc, buffer);

                if ((++pacifier & 15) == 0)
                    Console.Error.Write('.');
            }

            CurrentHeader.compressed_size = CurrentHeader.original_size;
            CurrentHeader.original_crc ^= CRC_MASK;
            return true;
        }
        private int SearchFileList(string fileName)
        {
            foreach (string file in FileList)
            {
                if (WildCardMatch(fileName, file))
                    return 1;
            }
            return 0;
        }

        private void SkipOverFileFromInputCar()
        {
            InputCarFile.Seek((long)CurrentHeader.compressed_size, SeekOrigin.Current);
        }
        private ulong Unstore(Stream destination)
        {
            ulong crc = CRC_MASK;
            uint count;
            byte[] buffer = new byte[256];
            int pacifier = 0;
            //uint remaining = CurrentHeader.original_size;

            while (CurrentHeader.original_size != 0)
            {
                if (CurrentHeader.original_size > 256)
                    count = 256;
                else
                    count = (uint)CurrentHeader.original_size;

                count = (uint)CurrentHeader.original_size;
                if (InputCarFile.Read(buffer, 0, (int)count) != count)
                    FatalError("Can't read from input CAR file");
                try
                {
                    destination.Write(buffer, 0, (int)count);
                }
                catch (IOException)
                {
                    Console.Error.WriteLine("Error writing to output file");
                    return ~CurrentHeader.original_crc;
                }
                crc = CalculateBlockCRC32((uint)count, crc, buffer);

                if (destination != Console.OpenStandardOutput() && (++pacifier & 15) == 0)
                    Console.Error.Write('.');

                CurrentHeader.original_size -= count;
            }

            return crc ^ CRC_MASK;
        }
        private ulong UpdateCharacterCRC32(ulong crc, int c)
        {
            ulong temp1 = (crc >> 8) & 0x00FFFFFF;
            ulong temp2 = Ccitt32Table[((int)crc ^ c) & 0xff];
            return temp1 ^ temp2;
        }

        private static void UsageExit()
        {
            Console.Error.WriteLine("CARMAN -- Compressed ARchive MANager");
            Console.Error.WriteLine("Usage: carman command car-file [file ...]");
            Console.Error.WriteLine("Commands:");
            Console.Error.WriteLine("  a: Add files to a CAR archive (replace if present)");
            Console.Error.WriteLine("  x: Extract files from a CAR archive");
            Console.Error.WriteLine("  r: Replace files in a CAR archive");
            Console.Error.WriteLine("  d: Delete files from a CAR archive");
            Console.Error.WriteLine("  p: Print files on standard output");
            Console.Error.WriteLine("  l: List contents of a CAR archive");
            Console.Error.WriteLine("  t: Test files in a CAR archive");
            Console.Error.WriteLine();
            Environment.Exit(1);
        }
        private bool WildCardMatch(string str, string pattern)
        {
            string cleaned = string.Join("", pattern.Split(new[] { ".\\" }, StringSplitOptions.None));
            pattern = cleaned;
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
        void PackUnsignedData(int numberOfBytes, ulong number, byte[] buffer, int offset = 0)
        {
            for (int i = 0; i < numberOfBytes; i++)
            {
                buffer[offset + i] = (byte)(number & 0xFF);
                number >>= 8;
            }
        }

        private void WriteFileHeader()
        {
            byte[] headerData = new byte[17];
            byte[] fileNameBytes = Encoding.ASCII.GetBytes(CurrentHeader.file_name + '\0');

            OutputCarFile.Write(fileNameBytes, 0, fileNameBytes.Length);
            CurrentHeader.header_crc = CalculateBlockCRC32((uint)fileNameBytes.Length, CRC_MASK, fileNameBytes);
            PackUnsignedData(1, CurrentHeader.compression_method, headerData, 0);
            PackUnsignedData(4, CurrentHeader.original_size, headerData, 1);
            PackUnsignedData(4, CurrentHeader.compressed_size, headerData, 5);
            PackUnsignedData(4, CurrentHeader.original_crc, headerData, 9);


            CurrentHeader.header_crc = CalculateBlockCRC32(13, CurrentHeader.header_crc, headerData);
            CurrentHeader.header_crc ^= CRC_MASK;

            PackUnsignedData(4, CurrentHeader.header_crc, headerData, 13);
            OutputCarFile.Write(headerData, 0, 17);
        }

        private void WriteEndOfCarHeader()
        {
            OutputCarFile.WriteByte(0);
        }
    }
}
