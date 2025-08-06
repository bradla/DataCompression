import os
import sys
import struct
import io
from typing import List, Tuple, Optional, BinaryIO

class CarMan:
    BASE_HEADER_SIZE = 19
    CRC_MASK = 0xFFFFFFFF
    CRC32_POLYNOMIAL = 0xEDB88320
    FILENAME_MAX = 260  # Increased from 128 for modern systems

    class Header:
        def __init__(self):
            self.file_name = ""
            self.compression_method = 0
            self.original_size = 0
            self.compressed_size = 0
            self.original_crc = 0
            self.header_crc = 0

    class TreeNode:
        def __init__(self):
            self.parent = 0
            self.smaller_child = 0
            self.larger_child = 0

    def __init__(self):
        self.TempFileName = ""
        self.InputCarFile: Optional[BinaryIO] = None
        self.CarFileName = ""
        self.OutputCarFile: Optional[BinaryIO] = None
        self.CurrentHeader = self.Header()
        self.FileList: List[str] = []
        self.Ccitt32Table: List[int] = [0] * 256

        # LZSS Constants
        self.INDEX_BIT_COUNT = 12
        self.LENGTH_BIT_COUNT = 4
        self.WINDOW_SIZE = 1 << self.INDEX_BIT_COUNT
        self.RAW_LOOK_AHEAD_SIZE = 1 << self.LENGTH_BIT_COUNT
        self.BREAK_EVEN = (1 + self.INDEX_BIT_COUNT + self.LENGTH_BIT_COUNT) // 9
        self.LOOK_AHEAD_SIZE = self.RAW_LOOK_AHEAD_SIZE + self.BREAK_EVEN
        self.TREE_ROOT = self.WINDOW_SIZE
        self.END_OF_STREAM = 0
        self.UNUSED = 0

        self.window = bytearray(self.WINDOW_SIZE)


        self.tree = [self.TreeNode() for _ in range(self.WINDOW_SIZE + 1)]

        # Blocked I/O for LZSS
        self.dataBuffer = bytearray(17)
        self.flagBitMask = 0
        self.bufferOffset = 0

    def main(self):
        if len(sys.argv) < 2:
            self.UsageExit()
            return

        print("CARMAN 1.0 : ", end="", file=sys.stderr)
        self.BuildCRCTable()

        command = self.ParseArguments(sys.argv)
        print(file=sys.stderr)

        self.OpenArchiveFiles(sys.argv[2], command)
        self.BuildFileList(sys.argv[3:], command)

        count = 0
        if command == 'A':
            count = self.AddFileListToArchive()

        if command == 'L':
            self.PrintListTitles()

        count = self.ProcessAllFilesInInputCar(command, count)

        if self.OutputCarFile is not None and count != 0:
            self.WriteEndOfCarHeader()
            self.OutputCarFile.close()

            try:
                if os.path.exists(self.CarFileName):
                    os.remove(self.CarFileName)
                os.rename(self.TempFileName, self.CarFileName)
            except Exception as ex:
                self.FatalError(f"Can't rename temporary file: {ex}")

        if command != 'P':
            print(f"\n{count} file{'s' if count != 1 else ''}")
        else:
            print(f"\n{count} file{'s' if count != 1 else ''}", file=sys.stderr)

    def FatalError(self, message, *args):
        print(file=sys.stderr)
        print(message.format(*args), file=sys.stderr)
        print(file=sys.stderr)

        if self.OutputCarFile is not None:
            self.OutputCarFile.close()
            try:
                os.remove(self.TempFileName)
            except:
                pass

        sys.exit(1)

    def BuildCRCTable(self):
        for i in range(256):
            value = i
            for _ in range(8):
                if value & 1:
                    value = (value >> 1) ^ self.CRC32_POLYNOMIAL
                else:
                    value >>= 1
            self.Ccitt32Table[i] = value

    def CalculateBlockCRC32(self, count: int, crc: int, buffer: bytes) -> int:
        for i in range(count):
            temp1 = (crc >> 8) & 0x00FFFFFF
            temp2 = self.Ccitt32Table[(crc ^ buffer[i]) & 0xFF]
            crc = temp1 ^ temp2
        return crc

    def UpdateCharacterCRC32(self, crc: int, c: int) -> int:
        temp1 = (crc >> 8) & 0x00FFFFFF
        temp2 = self.Ccitt32Table[(crc ^ c) & 0xFF]
        return temp1 ^ temp2

    def ParseArguments(self, args: List[str]) -> str:
        if len(args) < 2 or len(args[1]) > 1:
            self.UsageExit()
            return '\0'

        command = args[1].upper()
        if command == 'X':
            print("Extracting files", end="", file=sys.stderr)
        elif command == 'R':
            print("Replacing files", end="", file=sys.stderr)
        elif command == 'P':
            print("Print files to stdout", end="", file=sys.stderr)
        elif command == 'T':
            print("Testing integrity of files", end="", file=sys.stderr)
        elif command == 'L':
            print("Listing archive contents", end="", file=sys.stderr)
        elif command == 'A':
            if len(args) <= 2:
                self.UsageExit()
            print("Adding/replacing files to archive", end="", file=sys.stderr)
        elif command == 'D':
            if len(args) <= 2:
                self.UsageExit()
            print("Deleting files from archive", end="", file=sys.stderr)
        else:
            self.UsageExit()
        return command

    @staticmethod
    def UsageExit():
        print("CARMAN -- Compressed ARchive MANager", file=sys.stderr)
        print("Usage: carman command car-file [file ...]", file=sys.stderr)
        print("Commands:", file=sys.stderr)
        print("  a: Add files to a CAR archive (replace if present)", file=sys.stderr)
        print("  x: Extract files from a CAR archive", file=sys.stderr)
        print("  r: Replace files in a CAR archive", file=sys.stderr)
        print("  d: Delete files from a CAR archive", file=sys.stderr)
        print("  p: Print files on standard output", file=sys.stderr)
        print("  l: List contents of a CAR archive", file=sys.stderr)
        print("  t: Test files in a CAR archive", file=sys.stderr)
        print(file=sys.stderr)
        sys.exit(0)

    def OpenArchiveFiles(self, name: str, command: str):
        self.CarFileName = name

        # Try to open the input file
        try:
            self.InputCarFile = open(self.CarFileName, "rb")
        except:
            # If not found and no extension, try adding .car
            if not os.path.splitext(self.CarFileName)[1]:
                self.CarFileName += ".car"
                try:
                    self.InputCarFile = open(self.CarFileName, "rb")
                except:
                    self.InputCarFile = None
            else:
                self.InputCarFile = None

        if self.InputCarFile is None and command != 'A':
            self.FatalError("Can't open archive '{}'", self.CarFileName)

        if command in ('A', 'R', 'D'):
            temp_dir = os.path.expanduser("~")
            temp_name = ""
            for i in range(10):
                temp_name = os.path.join(temp_dir, f"{os.path.splitext(os.path.basename(self.CarFileName))[0]}.$${i}")
                if not os.path.exists(temp_name):
                    break

            if i == 9:
                self.FatalError("Can't open temporary file")

            self.TempFileName = temp_name
            try:
                self.OutputCarFile = open(self.TempFileName, "wb")
            except:
                self.FatalError("Can't open temporary file {}", self.TempFileName)

    def BuildFileList(self, files: List[str], command: str):
        if not files:
            self.FileList.append("*")
        else:
            for file in files:
                if command == 'A':
                    # Handle wildcard expansion for Add command
                    try:
                        dir_path = os.path.dirname(file) or "."
                        pattern = os.path.basename(file)
                        for found_file in os.listdir(dir_path):
                            if os.path.isfile(os.path.join(dir_path, found_file)) and fnmatch.fnmatch(found_file, pattern):
                                self.FileList.append(found_file.lower())
                    except:
                        self.FileList.append(file.lower())
                else:
                    self.FileList.append(file.lower())

                if len(self.FileList) > 99:
                    self.FatalError("Too many file names")

    def AddFileListToArchive(self) -> int:
        for i, filename in enumerate(self.FileList):
            if not filename:
                continue
            
            try:
                with open(filename, 'rb') as input_text_file:
                                        # Get just the filename without path
                    base_name = os.path.basename(filename)
                    
                    # Check for duplicates
                    skip = False
                    for j in range(i):
                        if self.FileList[j] == base_name:
                            print(f"Duplicate file name: {filename}   Skipping this file...")
                            skip = True
                            break
                    
                    if not skip:
                        self.CurrentHeader = self.Header()
                        self.CurrentHeader.file_name = filename
                        self.Insert(input_text_file, "Adding")
            except IOError:
                print(f"Could not open {filename} to add to CAR file")
        
        return len(self.FileList)

    def AddFileListToArchive2(self) -> int:
        count = 0
        for file in self.FileList:
            try:
                with open(file, "rb") as input_text_file:
                    file_name_only = os.path.basename(file)

                    # Check for duplicates
                    skip = False
                    for i in range(count):
                        if file_name_only.lower() == self.FileList[i].lower():
                            print(f"Duplicate file name: {file}   Skipping this file...", end="", file=sys.stderr)
                            skip = True
                            break

                    if not skip:
                        self.CurrentHeader = self.Header()
                        self.CurrentHeader.file_name = file_name_only
                        self.Insert(input_text_file, "Adding")
                        count += 1
            except:
                self.FatalError("Could not open {} to add to CAR file", file)
        return count

    def ProcessAllFilesInInputCar(self, command: str, count: int) -> int:
        output_destination = None
        try:
            if command == 'P':
                output_destination = sys.stdout.buffer
            elif command == 'T':
                import tempfile
                output_destination = tempfile.NamedTemporaryFile(delete=True)

            while self.InputCarFile is not None and self.ReadFileHeader() != 0:
                matched = self.SearchFileList(self.CurrentHeader.file_name)

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
                            with open(self.CurrentHeader.file_name, "rb") as input_text_file:
                                self.SkipOverFileFromInputCar()
                                self.Insert(input_text_file, "Replacing")
                                count += 1
                        except:
                            print(f"Could not find {self.CurrentHeader.file_name} for replacement, skipping", end="", file=sys.stderr)
                            self.CopyFileFromInputCar()
                    else:
                        self.CopyFileFromInputCar()
        finally:
            if output_destination is not None and command == 'T':
                output_destination.close()
        return count

    def SearchFileList(self, file_name: str) -> bool:
        for file in self.FileList:
            if self.WildCardMatch(file_name, file):
                return True
        return False

    @staticmethod
    def WildCardMatch(s: str, pattern: str) -> bool:
        import fnmatch
        return fnmatch.fnmatch(s.lower(), pattern.lower())

    def SkipOverFileFromInputCar(self):
        self.InputCarFile.seek(self.CurrentHeader.compressed_size, os.SEEK_CUR)

    def CopyFileFromInputCar(self):
        if self.OutputCarFile is None:
            return

        self.WriteFileHeader()
        remaining = self.CurrentHeader.compressed_size
        buffer_size = 256

        while remaining > 0:
            count = min(remaining, buffer_size)
            buffer = self.InputCarFile.read(count)
            if len(buffer) != count:
                self.FatalError("Error reading input file {}", self.CurrentHeader.file_name)
            self.OutputCarFile.write(buffer)
            remaining -= count

    def PrintListTitles(self):
        print()
        print("                       Original  Compressed")
        print("     Filename            Size       Size     Ratio   CRC-32   Method")
        print("------------------     --------  ----------  -----  --------  ------")

    def ListCarFileEntry(self):
        methods = ["Stored", "LZSS"]
        print("{:<20} {:10}  {:10}  {:3}%  {:08X}  {}".format(
            self.CurrentHeader.file_name,
            self.CurrentHeader.original_size,
            self.CurrentHeader.compressed_size,
            self.RatioInPercent(self.CurrentHeader.compressed_size, self.CurrentHeader.original_size),
            self.CurrentHeader.original_crc,
            methods[self.CurrentHeader.compression_method - 1]))

    @staticmethod
    def RatioInPercent(compressed: int, original: int) -> int:
        if original == 0:
            return 0
        return int(100 - (100 * compressed) / original)

    def ReadFileHeader(self) -> int:
        i = 0
        file_name = bytearray()

        # Read file name
        while True:
            c = self.InputCarFile.read(1)
            if not c:
                return 0

            if c == b'\x00':
                break

            file_name += c
            i += 1
            if i == self.FILENAME_MAX:
                self.FatalError("File name exceeded maximum in header")

        if not file_name:
            return 0

        self.CurrentHeader.file_name = file_name.decode('ascii')
        header_crc = self.CalculateBlockCRC32(len(file_name) + 1, self.CRC_MASK, file_name + b'\x00')

        header_data = self.InputCarFile.read(17)
        if len(header_data) != 17:
            return 0

        self.CurrentHeader.compression_method = header_data[0]
        self.CurrentHeader.original_size = struct.unpack("<I", header_data[1:5])[0]
        self.CurrentHeader.compressed_size = struct.unpack("<I", header_data[5:9])[0]
        self.CurrentHeader.original_crc = struct.unpack("<I", header_data[9:13])[0]
        self.CurrentHeader.header_crc = struct.unpack("<I", header_data[13:17])[0]

        header_crc = self.CalculateBlockCRC32(13, header_crc, header_data[:13])
        header_crc ^= self.CRC_MASK

        if self.CurrentHeader.header_crc != header_crc:
            self.FatalError("Header checksum error for file {}", self.CurrentHeader.file_name)

        return 1

    def WriteFileHeader(self):
        if self.OutputCarFile is None:
            return

        file_name_bytes = self.CurrentHeader.file_name.encode('ascii') + b'\x00'
        self.OutputCarFile.write(file_name_bytes)
        self.CurrentHeader.header_crc = self.CalculateBlockCRC32(len(file_name_bytes), self.CRC_MASK, file_name_bytes)

        header_data = bytearray(17)
        header_data[0] = self.CurrentHeader.compression_method
        header_data[1:5] = struct.pack("<I", self.CurrentHeader.original_size)
        header_data[5:9] = struct.pack("<I", self.CurrentHeader.compressed_size)
        header_data[9:13] = struct.pack("<I", self.CurrentHeader.original_crc)

        self.CurrentHeader.header_crc = self.CalculateBlockCRC32(13, self.CurrentHeader.header_crc, header_data[:13])
        self.CurrentHeader.header_crc ^= self.CRC_MASK

        header_data[13:17] = struct.pack("<I", self.CurrentHeader.header_crc)
        self.OutputCarFile.write(header_data)

    def WriteEndOfCarHeader(self):
        if self.OutputCarFile is not None:
            self.OutputCarFile.write(b'\x00')

    def Insert(self, input_text_file: BinaryIO, operation: str):
        print(f"{operation} {self.CurrentHeader.file_name:<20}", end="", file=sys.stderr)

        saved_position_of_header = self.OutputCarFile.tell()
        self.CurrentHeader.compression_method = 2
        self.WriteFileHeader()

        saved_position_of_file = self.OutputCarFile.tell()
        self.CurrentHeader.original_size = os.fstat(input_text_file.fileno()).st_size
        input_text_file.seek(0)

        if not self.LZSSCompress(input_text_file):
            self.CurrentHeader.compression_method = 1
            self.OutputCarFile.seek(saved_position_of_file)
            input_text_file.seek(0)
            self.Store(input_text_file)

        self.OutputCarFile.seek(saved_position_of_header)
        self.WriteFileHeader()
        self.OutputCarFile.seek(0, os.SEEK_END)

        print(f" {self.RatioInPercent(self.CurrentHeader.compressed_size, self.CurrentHeader.original_size)}%")

    def Extract(self, destination: Optional[BinaryIO]):
        print(f"{self.CurrentHeader.file_name:<20} ", end="", file=sys.stderr)
        error = False

        output_text_file = destination
        should_close = False

        try:
            if destination is None:
                output_text_file = open(self.CurrentHeader.file_name, "wb")
                should_close = True

            crc = 0
            if self.CurrentHeader.compression_method == 1:
                crc = self.Unstore(output_text_file)
            elif self.CurrentHeader.compression_method == 2:
                crc = self.LZSSExpand(output_text_file)
            else:
                print(f"Unknown method: {self.CurrentHeader.compression_method}", end="", file=sys.stderr)
                self.SkipOverFileFromInputCar()
                error = True
                crc = self.CurrentHeader.original_crc

            if crc != self.CurrentHeader.original_crc:
                print("CRC error reading data", end="", file=sys.stderr)
                error = True

            if not error:
                print(" OK", end="", file=sys.stderr)
        except:
            print(f"Can't open {self.CurrentHeader.file_name}", end="", file=sys.stderr)
            print("Not extracted", end="", file=sys.stderr)
            self.SkipOverFileFromInputCar()
        finally:
            if should_close:
                if output_text_file is not None:
                    output_text_file.close()

                if error:
                    try:
                        os.remove(self.CurrentHeader.file_name)
                    except:
                        pass

    def Store(self, input_text_file: BinaryIO) -> bool:
        pacifier = 0
        self.CurrentHeader.original_crc = self.CRC_MASK
        buffer_size = 256

        while True:
            buffer = input_text_file.read(buffer_size)
            if not buffer:
                break

            self.OutputCarFile.write(buffer)
            self.CurrentHeader.original_crc = self.CalculateBlockCRC32(len(buffer), self.CurrentHeader.original_crc, buffer)

            pacifier += 1
            if pacifier % 15 == 0:
                print('.', end="", file=sys.stderr)

        self.CurrentHeader.compressed_size = self.CurrentHeader.original_size
        self.CurrentHeader.original_crc ^= self.CRC_MASK
        return True

    def Unstore(self, destination: BinaryIO) -> int:
        crc = self.CRC_MASK
        pacifier = 0
        remaining = self.CurrentHeader.original_size
        buffer_size = 256

        while remaining > 0:
            count = min(remaining, buffer_size)
            buffer = self.InputCarFile.read(count)
            if len(buffer) != count:
                self.FatalError("Can't read from input CAR file")

            destination.write(buffer)
            crc = self.CalculateBlockCRC32(count, crc, buffer)

            if destination != sys.stdout.buffer and pacifier % 15 == 0:
                print('.', end="", file=sys.stderr)
            pacifier += 1
            remaining -= count

        return crc ^ self.CRC_MASK

    # LZSS Methods
    def InitTree(self, r: int):
        for i in range(self.WINDOW_SIZE + 1):
            self.tree[i].parent = self.UNUSED
            self.tree[i].larger_child = self.UNUSED
            self.tree[i].smaller_child = self.UNUSED

        self.tree[self.TREE_ROOT].larger_child = r
        self.tree[r].parent = self.TREE_ROOT
        self.tree[r].larger_child = self.UNUSED
        self.tree[r].smaller_child = self.UNUSED

    def ContractNode(self, old_node: int, new_node: int):
        self.tree[new_node].parent = self.tree[old_node].parent
        if self.tree[self.tree[old_node].parent].larger_child == old_node:
            self.tree[self.tree[old_node].parent].larger_child = new_node
        else:
            self.tree[self.tree[old_node].parent].smaller_child = new_node

        self.tree[old_node].parent = self.UNUSED

    def ReplaceNode(self, old_node: int, new_node: int):
        parent = self.tree[old_node].parent
        if self.tree[parent].smaller_child == old_node:
            self.tree[parent].smaller_child = new_node
        else:
            self.tree[parent].larger_child = new_node

        self.tree[new_node] = self.tree[old_node]
        self.tree[self.tree[new_node].smaller_child].parent = new_node
        self.tree[self.tree[new_node].larger_child].parent = new_node
        self.tree[old_node].parent = self.UNUSED

    def FindNextNode(self, node: int) -> int:
        next_node = self.tree[node].smaller_child
        while self.tree[next_node].larger_child != self.UNUSED:
            next_node = self.tree[next_node].larger_child
        return next_node

    def DeleteString(self, p: int):
        if self.tree[p].parent == self.UNUSED:
            return

        if self.tree[p].larger_child == self.UNUSED:
            self.ContractNode(p, self.tree[p].smaller_child)
        elif self.tree[p].smaller_child == self.UNUSED:
            self.ContractNode(p, self.tree[p].larger_child)
        else:
            replacement = self.FindNextNode(p)
            self.DeleteString(replacement)
            self.ReplaceNode(p, replacement)

    def AddString(self, new_node: int) -> Tuple[int, int]:
        i = 0
        test_node = self.tree[self.TREE_ROOT].larger_child
        match_length = 0
        match_position = 0
        delta = 0

        while True:
            for i in range(self.LOOK_AHEAD_SIZE):
                delta = self.window[(new_node + i) % self.WINDOW_SIZE] - self.window[(test_node + i) % self.WINDOW_SIZE]
                if delta != 0:
                    break

            if i >= match_length:
                match_length = i
                match_position = test_node
                if match_length >= self.LOOK_AHEAD_SIZE:
                    self.ReplaceNode(test_node, new_node)
                    return match_length, match_position

            child = self.tree[test_node].larger_child if delta >= 0 else self.tree[test_node].smaller_child

            if child == self.UNUSED:
                if delta >= 0:
                    self.tree[test_node].larger_child = new_node
                else:
                    self.tree[test_node].smaller_child = new_node
                self.tree[new_node].parent = test_node
                self.tree[new_node].larger_child = self.UNUSED
                self.tree[new_node].smaller_child = self.UNUSED
                return match_length, match_position

            test_node = child

    def InitOutputBuffer(self):
        self.dataBuffer[0] = 0
        self.flagBitMask = 1
        self.bufferOffset = 1

    def FlushOutputBuffer(self) -> int:
        if self.bufferOffset == 1:
            return 1

        self.CurrentHeader.compressed_size += self.bufferOffset
        if self.CurrentHeader.compressed_size >= self.CurrentHeader.original_size:
            return 0

        self.OutputCarFile.write(self.dataBuffer[:self.bufferOffset])
        self.InitOutputBuffer()
        return 1

    def OutputChar(self, data: int) -> int:
        self.dataBuffer[self.bufferOffset] = data
        self.bufferOffset += 1
        self.dataBuffer[0] |= self.flagBitMask
        self.flagBitMask <<= 1
        return self.FlushOutputBuffer() if self.flagBitMask == 0x100 else 1

    def OutputPair(self, position: int, length: int) -> int:
        self.dataBuffer[self.bufferOffset] = (length << 4) | (position >> 8)
        self.dataBuffer[self.bufferOffset + 1] = position & 0xFF
        self.bufferOffset += 2
        self.flagBitMask <<= 1
        if self.flagBitMask == 0x100:
            return self.FlushOutputBuffer()
        return 1
       # return self.FlushOutputBuffer() if self.flagBitMask == 0x100 else True

    def InitInputBuffer(self):
        self.flagBitMask = 1
        self.dataBuffer[0] = ord(self.InputCarFile.read(1))

    def InputBit(self) -> bool:
        if self.flagBitMask == 0x100:
            self.InitInputBuffer()

        result = (self.dataBuffer[0] & self.flagBitMask) != 0
        self.flagBitMask <<= 1
        return result

    def LZSSCompress(self, input_text_file: BinaryIO) -> int:
        look_ahead_bytes = 0
        current_position = 1
        replace_count = 0
        match_length = 0
        match_position = 0

        self.CurrentHeader.compressed_size = 0
        self.CurrentHeader.original_crc = self.CRC_MASK
        self.InitOutputBuffer()

        # Initialize window
        for i in range(self.LOOK_AHEAD_SIZE):
            c = input_text_file.read(1)
            if not c:
                break

            self.window[current_position + i] = c[0]
            self.CurrentHeader.original_crc = self.UpdateCharacterCRC32(self.CurrentHeader.original_crc, c[0])

        look_ahead_bytes = i
        self.InitTree(current_position)

        while look_ahead_bytes > 0:
            if match_length > look_ahead_bytes:
                match_length = look_ahead_bytes

            if match_length <= self.BREAK_EVEN:
                replace_count = 1
                if not self.OutputChar(self.window[current_position]):
                    return 0
            else:
                if not self.OutputPair(match_position, match_length - (self.BREAK_EVEN + 1)):
                    return 0
                replace_count = match_length

            for _ in range(replace_count):
                self.DeleteString((current_position + self.LOOK_AHEAD_SIZE) % self.WINDOW_SIZE)

                c = input_text_file.read(1)
                if not c:
                    look_ahead_bytes -= 1
                else:
                    self.CurrentHeader.original_crc = self.UpdateCharacterCRC32(self.CurrentHeader.original_crc, c[0])
                    self.window[(current_position + self.LOOK_AHEAD_SIZE) % self.WINDOW_SIZE] = c[0]

                current_position = (current_position + 1) % self.WINDOW_SIZE
                if current_position == 0:
                    print('.', end="", file=sys.stderr)

                if look_ahead_bytes > 0:
                    match_length, match_position = self.AddString(current_position)

        self.CurrentHeader.original_crc ^= self.CRC_MASK
        return self.FlushOutputBuffer()

    def LZSSExpand(self, output: BinaryIO) -> int:
        current_position = 1
        crc = self.CRC_MASK
        output_count = 0

        self.InitInputBuffer()

        while output_count < self.CurrentHeader.original_size:
            if self.InputBit():
                c = ord(self.InputCarFile.read(1))
                output.write(bytes([c]))
                output_count += 1
                crc = self.UpdateCharacterCRC32(crc, c)
                self.window[current_position] = c
                current_position = (current_position + 1) % self.WINDOW_SIZE

                if current_position == 0 and output != sys.stdout.buffer:
                    print('.', end="", file=sys.stderr)
            else:
                match_byte1 = ord(self.InputCarFile.read(1))
                match_byte2 = ord(self.InputCarFile.read(1))
                match_length = (match_byte1 >> 4) + self.BREAK_EVEN
                match_position = ((match_byte1 & 0xF) << 8) | match_byte2
                output_count += match_length + 1

                for i in range(match_length + 1):
                    c = self.window[(match_position + i) % self.WINDOW_SIZE]
                    output.write(bytes([c]))
                    crc = self.UpdateCharacterCRC32(crc, c)
                    self.window[current_position] = c
                    current_position = (current_position + 1) % self.WINDOW_SIZE

                    if current_position == 0 and output != sys.stdout.buffer:
                        print('.', end="", file=sys.stderr)

        return crc ^ self.CRC_MASK

if __name__ == "__main__":
    import fnmatch
    car_man = CarMan()
    car_man.main()