import os
import sys
import struct
import io
import tempfile
from typing import List, Tuple, Optional, BinaryIO

class CarProcessor:
    UNUSED = 0
    INDEX_BIT_COUNT = 12
    LENGTH_BIT_COUNT = 4
    WINDOW_SIZE = 1 << INDEX_BIT_COUNT
    TREE_ROOT = 1 << INDEX_BIT_COUNT
    RAW_LOOK_AHEAD_SIZE = 1 << LENGTH_BIT_COUNT
    END_OF_STREAM = 0
    BREAK_EVEN = (1 + INDEX_BIT_COUNT + LENGTH_BIT_COUNT) // 9
    LOOK_AHEAD_SIZE = RAW_LOOK_AHEAD_SIZE + BREAK_EVEN
    BaseHeaderSize = 19
    CrcMask = 0xFFFFFFFF
    Crc32Polynomial = 0xEDB88320
    FilenameMax = 128
    MaxFileList = 100
    Ccitt32TableSize = 256

    class HeaderStruct:
        def __init__(self):
            self.FileName = ""
            self.CompressionMethod = 0
            self.OriginalSize = 0
            self.CompressedSize = 0
            self.OriginalCrc = 0
            self.HeaderCrc = 0

    class TreeNode:
        def __init__(self):
            self.Parent = CarProcessor.UNUSED
            self.SmallerChild = CarProcessor.UNUSED
            self.LargerChild = CarProcessor.UNUSED

    Window = bytearray(WINDOW_SIZE)
        # Initialize Tree as None first, we'll initialize it properly in __init__
    Tree = None
    #Tree = [TreeNode() for _ in range(WINDOW_SIZE + 1)]

    def __init__(self):
        # Initialize the Tree array with TreeNode objects
        if CarProcessor.Tree is None:
            CarProcessor.Tree = [self.TreeNode() for _ in range(self.WINDOW_SIZE + 1)]
        self.TempFileName = ""
        self.InputCarFile: Optional[BinaryIO] = None
        self.CarFileName = ""
        self.OutputCarFile: Optional[BinaryIO] = None
        self.FileList: List[str] = []
        self.Ccitt32Table = [0] * 256
        self.Header = self.HeaderStruct()
        self.BufferOffset = 0
        self.DataBuffer = bytearray(17)
        self.FlagBitMask = 0

    @staticmethod
    def ModWindow(a: int) -> int:
        return a & (CarProcessor.WINDOW_SIZE - 1)

    @staticmethod
    def StrRChr(string_to_search: str, char_to_find: str) -> str:
        index = string_to_search.rfind(char_to_find)
        if index > -1:
            return string_to_search[index:]
        return string_to_search

    def UsageExit(self) -> None:
        print("CARMAN -- Compressed Archive MANager")
        print("Usage: carman command car-file [file ...]")
        print("Commands:")
        print("  a: Add files to a CAR archive (replace if present)")
        print("  x: Extract files from a CAR archive")
        print("  r: Replace files in a CAR archive")
        print("  d: Delete files from a CAR archive")
        print("  p: Print files on standard output")
        print("  l: List contents of a CAR archive")
        print("  t: Test files in a CAR archive")
        print("")

    def PrintListTitles(self) -> None:
        print("\n                       Original  Compressed")
        print("     Filename            Size       Size     Ratio   CRC-32   Method")
        print("------------------     --------  ----------  -----  --------  ------")

    def CopyFileFromInputCar(self) -> None:
        print("CopyFileFromInputCar writeheader ")
        self.WriteFileHeader()
        buffer = bytearray(256)
        remaining = self.Header.CompressedSize

        while remaining > 0:
            count = min(remaining, 256)
            data = self.InputCarFile.read(count)
            if len(data) != count:
                self.FatalError(f"Error reading input file {self.Header.FileName}")
            self.OutputCarFile.write(data)
            remaining -= count

    def ProcessAllFilesInInputCar(self, command: str, count: int) -> int:
        output_destination = None
        try:
            if command == 'P':
                output_destination = sys.stdout.buffer
            elif command == 'T':
                output_destination = tempfile.NamedTemporaryFile(mode='wb+')

            while self.InputCarFile and self.ReadFileHeader() != 0:
                matched = self.SearchFileList(self.Header.FileName)

                if command == 'D':
                    if matched:
                        self.SkipOverFileFromInputCar()
                        count += 1
                    else:
                        self.CopyFileFromInputCar()
                elif command == 'A':
                    if matched:
                        self.SkipOverFileFromInputCar()
                    else:
                        self.CopyFileFromInputCar()
                elif command == 'L':
                    if matched:
                        self.ListCarFileEntry()
                        count += 1
                    self.SkipOverFileFromInputCar()
                elif command in ('P', 'X', 'T'):
                    if matched:
                        self.Extract(output_destination)
                        count += 1
                    else:
                        self.SkipOverFileFromInputCar()
                elif command == 'R':
                    if matched:
                        try:
                            with open(self.Header.FileName, 'rb') as input_text_file:
                                self.SkipOverFileFromInputCar()
                                self.Insert(input_text_file, "Replacing")
                                count += 1
                        except:
                            print(f"Could not find {self.Header.FileName} for replacement, skipping", file=sys.stderr)
                            self.CopyFileFromInputCar()
                    else:
                        self.CopyFileFromInputCar()
        finally:
            if output_destination and command in ('P', 'T'):
                output_destination.close()
        return count

    def BuildFileList(self, argc: int, args: List[str], command: str) -> None:
        if not args:
            self.FileList.append("*")
            return

        for file in args:
            if command == 'A':
                try:
                    dir = os.path.dirname(file) or "."
                    pattern = os.path.basename(file)
                    for found_file in [f.lower() for f in os.listdir(dir) if os.path.isfile(os.path.join(dir, f))]:
                        if fnmatch.fnmatch(found_file, pattern):
                            self.FileList.append(os.path.join(dir, found_file).lower())
                except:
                    self.FileList.append(file.lower())
            else:
                self.FileList.append(file.lower())

    def AddFileListToArchive(self) -> int:
        count = 0
        for file in self.FileList:
            try:
                with open(file, 'rb') as input_text_file:
                    file_name_only = os.path.basename(file)
                    skip = False
                    for i in range(count):
                        if file_name_only.lower() == os.path.basename(self.FileList[i]).lower():
                            print(f"Duplicate file name: {file}   Skipping this file...", file=sys.stderr)
                            skip = True
                            break
                    if not skip:
                        self.Header = self.HeaderStruct()
                        self.Header.FileName = file_name_only
                        self.Insert(input_text_file, "Adding")
                        count += 1
            except:
                self.FatalError(f"Could not open {file} to add to CAR file")
        return count

    def ParseArguments(self, argc: int, argv: List[str]) -> str:
        if len(argv) < 2:
            self.UsageExit()
            sys.exit(0)

        command = argv[0].upper()
        if command == 'A' and len(argv) <= 3:
            self.UsageExit()
        elif command == 'D' and len(argv) <= 3:
            self.UsageExit()
        
        print({
            'X': "Extracting files",
            'R': "Replacing files",
            'P': "Print files to stdout",
            'T': "Testing integrity of files",
            'L': "Listing archive contents",
            'A': "Adding/replacing files to archive",
            'D': "Deleting files from archive"
        }.get(command, ""))
        return command

    def OpenArchiveFiles(self, name: str, command: str) -> None:
        s = ""
        FILENAME_MAX = 255

        self.CarFileName = name
        try:
            self.InputCarFile = open(self.CarFileName, 'rb+')
        except:
            self.InputCarFile = None

        if self.InputCarFile:
            s = self.StrRChr(self.CarFileName, '\\')
            if not s:
                s = self.CarFileName

            if not self.StrRChr(s, '.'):
                if len(self.CarFileName) < (FILENAME_MAX - 4):
                    self.CarFileName += ".car"
                    with open(self.CarFileName, 'wb') as fsout:
                        self.InputCarFile.seek(0)
                        fsout.write(self.InputCarFile.read())

        if not os.path.exists(self.CarFileName) and command != 'A':
            self.FatalError(f"Can't open archive {self.CarFileName}")

        if command in ('A', 'R', 'D'):
            self.TempFileName = os.path.join(tempfile.gettempdir(), os.urandom(16).hex() + os.path.splitext(self.CarFileName)[1])
            try:
                self.OutputCarFile = open(self.TempFileName, 'wb+')
            except:
                self.FatalError(f"Can't open temporary file {self.TempFileName}")

        if self.InputCarFile:
            self.InputCarFile.flush()
        if self.OutputCarFile:
            self.OutputCarFile.flush()

    @staticmethod
    def WildCardMatch(input_str: str, pattern: str) -> bool:
        cleaned = "".join(pattern.split(".\\"))
        pattern = cleaned
        input_idx = 0
        pattern_idx = 0
        input_backtrack = -1
        pattern_backtrack = -1

        while input_idx < len(input_str):
            if pattern_idx < len(pattern) and pattern[pattern_idx] == '*':
                input_backtrack = input_idx
                pattern_backtrack = pattern_idx + 1
                pattern_idx += 1
            elif (pattern_idx < len(pattern) and 
                 (pattern[pattern_idx] == '?' or pattern[pattern_idx] == input_str[input_idx])):
                input_idx += 1
                pattern_idx += 1
            elif pattern_backtrack != -1:
                input_idx = input_backtrack + 1
                input_backtrack += 1
                pattern_idx = pattern_backtrack
            else:
                return False

        while pattern_idx < len(pattern) and pattern[pattern_idx] == '*':
            pattern_idx += 1

        return pattern_idx == len(pattern)

    def SearchFileList(self, file_name: str) -> bool:
        for file in self.FileList:
            if self.WildCardMatch(file_name, file):
                return True
        return False

    @staticmethod
    def RatioInPercent(compressed: int, original: int) -> int:
        if original == 0:
            return 0
        return 100 - (100 * compressed) // original

    def ReadFileHeader(self) -> int:
        header_data = bytearray(17)
        header_crc = 0
        i = 0
        file_name = ""

        while True:
            c = self.InputCarFile.read(1)
            if not c:
                return 0

            c = c[0]
            if c == 0:
                break

            file_name += chr(c)
            i += 1
            if i == self.FilenameMax:
                self.FatalError("File name exceeded maximum in header")

        if not file_name:
            return 0

        self.Header.FileName = file_name
        header_crc = self.CalculateBlockCRC32(i + 1, self.CrcMask, (self.Header.FileName + '\0').encode('ascii'))

        if self.InputCarFile.readinto(header_data) != 17:
            return 0

        self.Header.CompressionMethod = chr(header_data[0])
        self.Header.OriginalSize = self.UnpackUnsignedData(4, header_data, 1)
        self.Header.CompressedSize = self.UnpackUnsignedData(4, header_data, 5)
        self.Header.OriginalCrc = self.UnpackUnsignedData(4, header_data, 9)
        self.Header.HeaderCrc = self.UnpackUnsignedData(4, header_data, 13)

        header_crc = self.CalculateBlockCRC32(13, header_crc, header_data)
        self.PrintHexData(header_data, 17)
        header_crc ^= self.CrcMask

        if self.Header.HeaderCrc != header_crc:
            self.FatalError(f"Header checksum error for file {self.Header.FileName}")
        self.PrintHexData(header_data, 17)
        return 1

    @staticmethod
    def UnpackUnsignedData(num_bytes: int, buffer: bytearray, offset: int) -> int:
        result = 0
        shift_count = 0
        idx = offset

        while num_bytes > 0:
            result |= (buffer[idx] << shift_count)
            shift_count += 8
            idx += 1
            num_bytes -= 1

        return result

    def WriteFileHeader(self) -> None:
        header_data = bytearray(17)
        file_name_bytes = (self.Header.FileName + '\0').encode('ascii')

        self.OutputCarFile.write(file_name_bytes)
        self.Header.HeaderCrc = self.CalculateBlockCRC32(len(file_name_bytes), self.CrcMask, file_name_bytes)

        header_data[0] = ord(self.Header.CompressionMethod)
        self.PackUnsignedData(4, self.Header.OriginalSize, header_data, 1)
        self.PackUnsignedData(4, self.Header.CompressedSize, header_data, 5)
        self.PackUnsignedData(4, self.Header.OriginalCrc, header_data, 9)

        self.Header.HeaderCrc = self.CalculateBlockCRC32(13, self.Header.HeaderCrc, header_data)
        self.PrintHexData(header_data, 17)
        self.Header.HeaderCrc ^= self.CrcMask
        self.PrintHexData(header_data, 17)
        self.PackUnsignedData(4, self.Header.HeaderCrc, header_data, 13)

        self.OutputCarFile.write(header_data)

    @staticmethod
    def PrintHexData(data: bytearray, length: int) -> None:
        for i in range(length):
            print(f"{data[i]} ", end='')
        print()

    @staticmethod
    def PackUnsignedData(num_bytes: int, number: int, buffer: bytearray, offset: int) -> None:
        while num_bytes > 0:
            buffer[offset] = number & 0xFF
            number >>= 8
            offset += 1
            num_bytes -= 1

    def WriteEndOfCarHeader(self) -> None:
        self.OutputCarFile.write(b'\x00')
        self.InputCarFile.close()
        self.OutputCarFile.close()

    def Insert(self, input_text_file: BinaryIO, operation: str) -> None:
        print(f"{operation} {self.Header.FileName:<20}", end='', file=sys.stderr)

        saved_position_of_header = self.OutputCarFile.tell()
        self.Header.CompressionMethod = chr(2)
        self.WriteFileHeader()

        saved_position_of_file = self.OutputCarFile.tell()
        self.Header.OriginalSize = os.fstat(input_text_file.fileno()).st_size
        input_text_file.seek(0)

        if not self.LZSSCompress(input_text_file):
            self.Header.CompressionMethod = chr(1)
            self.OutputCarFile.seek(saved_position_of_file)
            input_text_file.seek(0)
            self.Store(input_text_file)

        self.OutputCarFile.seek(saved_position_of_header)
        print("writeheader 2nd time", file=sys.stderr)
        self.WriteFileHeader()
        self.OutputCarFile.seek(0, 2)  # Seek to end

        print(f" {self.RatioInPercent(self.Header.CompressedSize, self.Header.OriginalSize)}%", file=sys.stderr)

    def Extract(self, destination: Optional[BinaryIO]) -> None:
        print(f"{self.Header.FileName:<20} ", end='', file=sys.stderr)
        error = False
        output_text_file = destination
        should_close = False

        try:
            if destination is None:
                output_text_file = open(self.Header.FileName, 'wb')
                should_close = True

            crc = 0
            if self.Header.CompressionMethod == chr(1):
                crc = self.Unstore(output_text_file)
            elif self.Header.CompressionMethod == chr(2):
                crc = self.LZSSExpand(output_text_file)
            else:
                print(f"Unknown method: {self.Header.CompressionMethod}", file=sys.stderr)
                self.SkipOverFileFromInputCar()
                error = True
                crc = self.Header.OriginalCrc

            if crc != self.Header.OriginalCrc:
                print("CRC error reading data", file=sys.stderr)
                error = True

            if not error:
                print(" OK", file=sys.stderr)
        except:
            print(f"Can't open {self.Header.FileName}", file=sys.stderr)
            print("Not extracted", file=sys.stderr)
            self.SkipOverFileFromInputCar()
        finally:
            if should_close:
                output_text_file.close()
                if error:
                    try:
                        os.remove(self.Header.FileName)
                    except:
                        pass

    def Store(self, input_text_file: BinaryIO) -> bool:
        buffer = bytearray(256)
        pacifier = 0
        self.Header.OriginalCrc = self.CrcMask

        while True:
            bytes_read = input_text_file.readinto(buffer)
            if bytes_read == 0:
                break

            self.OutputCarFile.write(buffer[:bytes_read])
            self.Header.OriginalCrc = self.CalculateBlockCRC32(bytes_read, self.Header.OriginalCrc, buffer[:bytes_read])

            pacifier += 1
            if (pacifier & 15) == 0:
                print('.', end='', file=sys.stderr)

        self.Header.CompressedSize = self.Header.OriginalSize
        self.Header.OriginalCrc ^= self.CrcMask
        return True

    def Unstore(self, destination: BinaryIO) -> int:
        crc = self.CrcMask
        buffer = bytearray(256)
        pacifier = 0
        remaining = self.Header.OriginalSize

        while remaining > 0:
            count = min(remaining, 256)
            data = self.InputCarFile.read(count)
            if len(data) != count:
                self.FatalError("Can't read from input CAR file")

            destination.write(data)
            crc = self.CalculateBlockCRC32(count, crc, data)

            if destination != sys.stdout.buffer and (pacifier & 15) == 0:
                print('.', end='', file=sys.stderr)
            pacifier += 1
            remaining -= count

        return crc ^ self.CrcMask

    def ListCarFileEntry(self) -> None:
        methods = ["Stored", "LZSS"]
        method_idx = ord(self.Header.CompressionMethod) - 1
        if method_idx < 0 or method_idx >= len(methods):
            method_name = "Unknown"
        else:
            method_name = methods[method_idx]

        print(f"{self.Header.FileName:<20} "
              f"{self.Header.OriginalSize:10} "
              f"{self.Header.CompressedSize:11} "
              f"{self.RatioInPercent(self.Header.CompressedSize, self.Header.OriginalSize):5}% "
              f"  {self.Header.OriginalCrc:08X} "
              f"{method_name:5}")

    def SkipOverFileFromInputCar(self) -> None:
        self.InputCarFile.seek(self.Header.CompressedSize, 1)

    def BuildCRCTable(self) -> None:
        for i in range(256):
            value = i
            for j in range(8, 0, -1):
                if value & 1:
                    value = (value >> 1) ^ self.Crc32Polynomial
                else:
                    value >>= 1
            self.Ccitt32Table[i] = value

    def CalculateBlockCRC32(self, count: int, crc: int, buffer: bytearray) -> int:
        for i in range(count):
            temp1 = (crc >> 8) & 0x00FFFFFF
            temp2 = self.Ccitt32Table[(crc ^ buffer[i]) & 0xff]
            crc = temp1 ^ temp2
        return crc

    def UpdateCharacterCRC32(self, crc: int, c: int) -> int:
        temp1 = (crc >> 8) & 0x00FFFFFF
        temp2 = self.Ccitt32Table[(crc ^ c) & 0xff]
        return temp1 ^ temp2

    @staticmethod
    def InitTree(r: int) -> None:
        for i in range(CarProcessor.WINDOW_SIZE + 1):
            CarProcessor.Tree[i].Parent = CarProcessor.UNUSED
            CarProcessor.Tree[i].LargerChild = CarProcessor.UNUSED
            CarProcessor.Tree[i].SmallerChild = CarProcessor.UNUSED

        CarProcessor.Tree[CarProcessor.TREE_ROOT].LargerChild = r
        CarProcessor.Tree[r].Parent = CarProcessor.TREE_ROOT
        CarProcessor.Tree[r].LargerChild = CarProcessor.UNUSED
        CarProcessor.Tree[r].SmallerChild = CarProcessor.UNUSED

    @staticmethod
    def ContractNode(old_node: int, new_node: int) -> None:
        CarProcessor.Tree[new_node].Parent = CarProcessor.Tree[old_node].Parent
        parent = CarProcessor.Tree[old_node].Parent

        if CarProcessor.Tree[parent].LargerChild == old_node:
            CarProcessor.Tree[parent].LargerChild = new_node
        else:
            CarProcessor.Tree[parent].SmallerChild = new_node

        CarProcessor.Tree[old_node].Parent = CarProcessor.UNUSED

    @staticmethod
    def ReplaceNode(old_node: int, new_node: int) -> None:
        parent = CarProcessor.Tree[old_node].Parent
        if CarProcessor.Tree[parent].SmallerChild == old_node:
            CarProcessor.Tree[parent].SmallerChild = new_node
        else:
            CarProcessor.Tree[parent].LargerChild = new_node

        CarProcessor.Tree[new_node].Parent = CarProcessor.Tree[old_node].Parent
        CarProcessor.Tree[new_node].SmallerChild = CarProcessor.Tree[old_node].SmallerChild
        CarProcessor.Tree[new_node].LargerChild = CarProcessor.Tree[old_node].LargerChild

        smaller = CarProcessor.Tree[new_node].SmallerChild
        larger = CarProcessor.Tree[new_node].LargerChild
        if smaller != CarProcessor.UNUSED:
            CarProcessor.Tree[smaller].Parent = new_node
        if larger != CarProcessor.UNUSED:
            CarProcessor.Tree[larger].Parent = new_node

        CarProcessor.Tree[old_node].Parent = CarProcessor.UNUSED

    @staticmethod
    def FindNextNode(node: int) -> int:
        next_node = CarProcessor.Tree[node].SmallerChild
        while CarProcessor.Tree[next_node].LargerChild != CarProcessor.UNUSED:
            next_node = CarProcessor.Tree[next_node].LargerChild
        return next_node

    @staticmethod
    def DeleteString(p: int) -> None:
        if CarProcessor.Tree[p].Parent == CarProcessor.UNUSED:
            return

        if CarProcessor.Tree[p].LargerChild == CarProcessor.UNUSED:
            CarProcessor.ContractNode(p, CarProcessor.Tree[p].SmallerChild)
        elif CarProcessor.Tree[p].SmallerChild == CarProcessor.UNUSED:
            CarProcessor.ContractNode(p, CarProcessor.Tree[p].LargerChild)
        else:
            replacement = CarProcessor.FindNextNode(p)
            CarProcessor.DeleteString(replacement)
            CarProcessor.ReplaceNode(p, replacement)

    @staticmethod
    def AddString(new_node: int) -> Tuple[int, int]:
        if new_node == CarProcessor.END_OF_STREAM:
            return (0, 0)

        test_node = CarProcessor.Tree[CarProcessor.TREE_ROOT].LargerChild
        match_length = 0
        match_position = 0

        while True:
            delta = 0
            i = 0

            for i in range(CarProcessor.LOOK_AHEAD_SIZE):
                index_new = CarProcessor.ModWindow(new_node + i)
                index_test = CarProcessor.ModWindow(test_node + i)
                delta = CarProcessor.Window[index_new] - CarProcessor.Window[index_test]
                if delta != 0:
                    break

            if i >= match_length:
                match_length = i
                match_position = test_node
                if match_length >= CarProcessor.LOOK_AHEAD_SIZE:
                    CarProcessor.ReplaceNode(test_node, new_node)
                    return (match_length, match_position)

            if delta >= 0:
                if CarProcessor.Tree[test_node].LargerChild == CarProcessor.UNUSED:
                    CarProcessor.Tree[test_node].LargerChild = new_node
                    CarProcessor.Tree[new_node].Parent = test_node
                    CarProcessor.Tree[new_node].LargerChild = CarProcessor.UNUSED
                    CarProcessor.Tree[new_node].SmallerChild = CarProcessor.UNUSED
                    return (match_length, match_position)
                test_node = CarProcessor.Tree[test_node].LargerChild
            else:
                if CarProcessor.Tree[test_node].SmallerChild == CarProcessor.UNUSED:
                    CarProcessor.Tree[test_node].SmallerChild = new_node
                    CarProcessor.Tree[new_node].Parent = test_node
                    CarProcessor.Tree[new_node].LargerChild = CarProcessor.UNUSED
                    CarProcessor.Tree[new_node].SmallerChild = CarProcessor.UNUSED
                    return (match_length, match_position)
                test_node = CarProcessor.Tree[test_node].SmallerChild

    def InitOutputBuffer(self) -> None:
        self.DataBuffer[0] = 0
        self.FlagBitMask = 1
        self.BufferOffset = 1

    def FlushOutputBuffer(self) -> bool:
        if self.BufferOffset == 1:
            return True
        self.Header.CompressedSize += self.BufferOffset
        if self.Header.CompressedSize >= self.Header.OriginalSize:
            return False

        self.OutputCarFile.write(self.DataBuffer[:self.BufferOffset])
        self.InitOutputBuffer()
        return True

    def OutputChar(self, data: int) -> bool:
        self.DataBuffer[self.BufferOffset] = data
        self.BufferOffset += 1
        self.DataBuffer[0] |= self.FlagBitMask
        self.FlagBitMask <<= 1
        if self.FlagBitMask == 0x100:
            return self.FlushOutputBuffer()
        return True

    def OutputPair(self, position: int, length: int) -> bool:
        self.DataBuffer[self.BufferOffset] = (length << 4) | (position >> 8)
        self.BufferOffset += 1
        self.DataBuffer[self.BufferOffset] = position & 0xFF
        self.BufferOffset += 1
        self.FlagBitMask <<= 1
        if self.FlagBitMask == 0x100:
            return self.FlushOutputBuffer()
        return True

    def InitInputBuffer(self) -> None:
        self.FlagBitMask = 1
        self.DataBuffer[0] = ord(self.InputCarFile.read(1))

    def InputBit(self) -> int:
        if self.FlagBitMask == 0x100:
            self.InitInputBuffer()
        self.FlagBitMask <<= 1
        return (self.DataBuffer[0] & (self.FlagBitMask >> 1))

    @staticmethod
    def FatalError(message: str, *args) -> None:
        print(message % args, file=sys.stderr)
        sys.exit(1)

    def LZSSCompress(self, input_text_stream: BinaryIO) -> bool:
        look_ahead_bytes = 0
        current_position = 1
        match_length = 0
        match_position = 0

        self.Header.CompressedSize = 0
        self.Header.OriginalCrc = self.CrcMask
        self.InitOutputBuffer()

        for i in range(self.LOOK_AHEAD_SIZE):
            c = input_text_stream.read(1)
            if not c:
                break
            self.Window[current_position + i] = c[0]
            self.Header.OriginalCrc = self.UpdateCharacterCRC32(self.Header.OriginalCrc, c[0])
        look_ahead_bytes = i
        self.InitTree(current_position)

        tr = 0
        while look_ahead_bytes > 0:
            if match_length > look_ahead_bytes:
                match_length = look_ahead_bytes
            if match_length <= self.BREAK_EVEN:
                replace_count = 1
                if not self.OutputChar(self.Window[current_position]):
                    return False
            else:
                if not self.OutputPair(match_position, match_length - (self.BREAK_EVEN + 1)):
                    return False
                replace_count = match_length

            for i in range(replace_count):
                self.DeleteString(self.ModWindow(current_position + self.LOOK_AHEAD_SIZE))
                c = input_text_stream.read(1)
                if not c:
                    look_ahead_bytes -= 1
                else:
                    self.Header.OriginalCrc = self.UpdateCharacterCRC32(self.Header.OriginalCrc, c[0])
                    self.Window[self.ModWindow(current_position + self.LOOK_AHEAD_SIZE)] = c[0]
                current_position = self.ModWindow(current_position + 1)
                if current_position == 0:
                    print('.', end='', file=sys.stderr)
                if look_ahead_bytes > 0:
                    match_length, match_position = self.AddString(current_position)

            sys.stdout.write(f"\rProgress: {tr}%   ")
            sys.stdout.flush()
            tr += 1

        self.Header.OriginalCrc ^= self.CrcMask
        return self.FlushOutputBuffer()

    def LZSSExpand(self, output: BinaryIO) -> int:
        current_position = 1
        output_count = 0
        crc = self.CrcMask
        self.InitInputBuffer()

        while output_count < self.Header.OriginalSize:
            if self.InputBit():
                c = self.InputCarFile.read(1)[0]
                output.write(bytes([c]))
                output_count += 1
                crc = self.UpdateCharacterCRC32(crc, c)
                self.Window[current_position] = c
                current_position = self.ModWindow(current_position + 1)
                if current_position == 0 and output != sys.stdout.buffer:
                    print('.', end='', file=sys.stderr)
            else:
                match_length = self.InputCarFile.read(1)[0]
                match_position = self.InputCarFile.read(1)[0]
                match_position |= (match_length & 0xF) << 8
                match_length >>= 4
                match_length += self.BREAK_EVEN
                output_count += match_length + 1

                for i in range(match_length + 1):
                    c = self.Window[self.ModWindow(match_position + i)]
                    output.write(bytes([c]))
                    crc = self.UpdateCharacterCRC32(crc, c)
                    self.Window[current_position] = c
                    current_position = self.ModWindow(current_position + 1)
                    if current_position == 0 and output != sys.stdout.buffer:
                        print('.', end='', file=sys.stderr)

        return crc ^ self.CrcMask

def main():
    processor = CarProcessor()
    print("CARMAN 1.0 : ", end='', file=sys.stderr)
    processor.BuildCRCTable()

    command = processor.ParseArguments(len(sys.argv), sys.argv[1:])
    print(file=sys.stderr)

    processor.OpenArchiveFiles(sys.argv[1], command)
    processor.BuildFileList(len(sys.argv), sys.argv[2:], command)

    count = 0
    if command == 'A':
        count = processor.AddFileListToArchive()

    if command == 'L':
        processor.PrintListTitles()

    count = processor.ProcessAllFilesInInputCar(command, count)

    if processor.OutputCarFile and count != 0:
        processor.WriteEndOfCarHeader()
        processor.OutputCarFile.close()

        try:
            if os.path.exists(processor.CarFileName):
                os.remove(processor.CarFileName)
            print(f"Copy temp to original {processor.TempFileName}", file=sys.stderr)
            os.rename(processor.TempFileName, processor.CarFileName)
        except Exception as ex:
            processor.FatalError(f"Can't rename temporary file: {ex}")

    if command != 'P':
        print(f"\n{count} file{'s' if count != 1 else ''}")
    else:
        print(f"\n{count} file{'s' if count != 1 else ''}", file=sys.stderr)

if __name__ == "__main__":
    import fnmatch
    main()
