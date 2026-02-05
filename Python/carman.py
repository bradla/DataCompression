#Brad Arrington 2025
from collections import defaultdict
from dataclasses import dataclass
import os
import shutil
import sys
import struct
import io
from typing import List, Optional, Tuple
from weakref import ref

#from lzss import UNUSED

class CarProcessor:
    UNUSED = 0
    INDEX_BIT_COUNT = 12
    LENGTH_BIT_COUNT = 4
    WINDOW_SIZE = (1 << INDEX_BIT_COUNT)
    TREE_ROOT = WINDOW_SIZE
    RAW_LOOK_AHEAD_SIZE = 1 << LENGTH_BIT_COUNT
    END_OF_STREAM = 0
    BREAK_EVEN = (1 + INDEX_BIT_COUNT + LENGTH_BIT_COUNT) // 9
    LOOK_AHEAD_SIZE = (RAW_LOOK_AHEAD_SIZE + BREAK_EVEN)
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
        __slots__ = ['Parent', 'SmallerChild', 'LargerChild']
        def __init__(self):
            self.Parent = 0
            self.SmallerChild = 0
            self.LargerChild = 0

    def __init__(self):
        self.Window = bytearray(self.WINDOW_SIZE)
        self.Tree = [self.TreeNode() for _ in range(self.WINDOW_SIZE + 1)]
        self.TempFileName = ""
        self.InputCarFile: Optional[io.BufferedReader] = None
        self.CarFileName = ""
        self.OutputCarFile: Optional[io.BufferedWriter] = None
        self.FileList: List[str] = []
        self.Ccitt32Table = [0] * self.Ccitt32TableSize
        self.Header = self.HeaderStruct()
        self.DataBuffer = bytearray(17)
        self.FlagBitMask = 0
        self.BufferOffset = 0

    def ModWindow(self, a: int) -> int:
        return a & (self.WINDOW_SIZE - 1)

    def UsageExit(self):
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
        print()
        sys.exit(0)

    def PrintListTitles(self):
        print("\n                       Original  Compressed")
        print("     Filename            Size       Size     Ratio   CRC-32   Method")
        print("------------------     --------  ----------  -----  --------  ------")

    def CopyFileFromInputCar(self):
        buffer = bytearray(256)
        while self.Header.CompressedSize != 0:
            count = min(256, self.Header.CompressedSize)
            data = self.InputCarFile.read(count)
            if len(data) != count:
                print(f"Error reading input file {self.Header.FileName}")
                break
            
            self.Header.CompressedSize -= count
            try:
                self.OutputCarFile.write(data)
            except:
                print("Error writing to output CAR file")

    def ProcessAllFilesInInputCar(self, command: str, count: int) -> int:
        output_destination = None
        arguments = sys.argv
        
        while self.InputCarFile and self.ReadFileHeader() != 0:
            matched = self.SearchFileList(self.Header.FileName)
            c = arguments[1].lower()
            
            if c == 'd':
                if matched == 0:
                    self.SkipOverFileFromInputCar()
                    count += 1
                else:
                    self.CopyFileFromInputCar()
            elif c == 'a':
                if matched == 0:
                    self.SkipOverFileFromInputCar()
                else:
                    self.CopyFileFromInputCar()
            elif c == 'l':
                if matched == 0:
                    self.ListCarFileEntry()
                    count += 1
                self.SkipOverFileFromInputCar()
            elif c in ('p', 'x', 't'):
                if matched == 0:
                    self.Extract(output_destination)
                    count += 1
                else:
                    self.SkipOverFileFromInputCar()
            elif c == 'r':
                if matched == 0:
                    try:
                        with open(self.Header.FileName, 'rb') as input_text_file:
                            self.SkipOverFileFromInputCar()
                            self.Insert(input_text_file, "Replacing")
                            count += 1
                    except IOError:
                        print(f"Could not find {self.Header.FileName} for replacement, skipping")
                        self.CopyFileFromInputCar()
                else:
                    self.CopyFileFromInputCar()
        
        if output_destination and output_destination != sys.stdout:
            output_destination.close()
        return count

    def BuildFileList(self, argc: int, args: List[str], command: str):
        if len(args) > 2:
            if '*' in args:
                files = [f for f in os.listdir('.') if os.path.isfile(f)]
                self.FileList = files[:self.MaxFileList]
            else:
                self.FileList = args[3:+self.MaxFileList]

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
                        self.Header.FileName = filename
                        self.Insert(input_text_file, "Adding")
            except IOError:
                print(f"Could not open {filename} to add to CAR file")
        
        return len(self.FileList)

    def ParseArguments(self, argc: int, argv: List[str]) -> str:
        if len(argv) <= 1:
            self.UsageExit()
        
        command = argv[1].lower()
        if command == 'x':
            print("Extracting files")
        elif command == 'r':
            print("Replacing files")
        elif command == 'p':
            print("Print files to stdout")
        elif command == 't':
            print("Testing integrity of files")
        elif command == 'l':
            print("Listing archive contents")
        elif command == 'a':
            if len(argv) <= 2:
                self.UsageExit()
            print("Adding/replacing files to archive")
        elif command == 'd':
            if len(argv) <= 2:
                self.UsageExit()
            print("Deleting files from archive")
        else:
            self.UsageExit()
        
        return command

    def OpenArchiveFiles(self, name: str, command: str):
        self.CarFileName = name
        
        try:
            self.InputCarFile = open(self.CarFileName, 'rb')
        except IOError:
            if not os.path.splitext(self.CarFileName)[1]:
                self.CarFileName += ".car"
                try:
                    self.InputCarFile = open(self.CarFileName, 'rb')
                except IOError:
                    pass
        
        if not self.InputCarFile and command != 'a':
            self.FatalError(f"Can't open archive '{self.CarFileName}'")

        if command in ('a', 'r', 'd'):
            import tempfile
            self.TempFileName = tempfile.mktemp(suffix='.car')
            self.OutputCarFile = open(self.TempFileName, 'wb')

    def WildCardMatch(self, text: str, pattern: str) -> bool:
        n = len(text)
        m = len(pattern)
        dp = [[False] * (m + 1) for _ in range(n + 1)]
        dp[0][0] = True
        for j in range(1, m + 1):
            if pattern[j - 1] == '*':
                dp[0][j] = dp[0][j - 1]

        for i in range(1, n + 1):
            for j in range(1, m + 1):
                if pattern[j - 1] == '*':
                    dp[i][j] = dp[i - 1][j] or dp[i][j - 1] or dp[i - 1][j - 1]
                elif pattern[j - 1] == '?' or text[i - 1] == pattern[j - 1]:
                    dp[i][j] = dp[i - 1][j - 1]

        return dp[n][m]

    def SearchFileList(self, fileName: str) -> int:
        for filePattern in self.FileList:
            if filePattern and self.WildCardMatch(fileName, filePattern):
                return 1
        return 0

    def RatioInPercent(self, compressed: int, original: int) -> int:
        if original == 0:
            return 0
        result = (100 * compressed) // original
        return 100 - result

    def ReadFileHeader(self) -> int:
        # Read filename (null-terminated)
        filename_bytes = bytearray()
        while True:
            byte = self.InputCarFile.read(1)
            if not byte or byte == b'\x00':
                break
            filename_bytes.extend(byte)
        
        if not filename_bytes:
            return 0
            
        self.Header.FileName = filename_bytes.decode('ascii', errors='replace')
        
        # Read the rest of the header
        header_data = self.InputCarFile.read(17)
        if len(header_data) != 17:
            return 0
            
        self.Header.CompressionMethod = header_data[0]
        self.Header.OriginalSize = int.from_bytes(header_data[1:5], 'little')
        self.Header.CompressedSize = int.from_bytes(header_data[5:9], 'little')
        self.Header.OriginalCrc = int.from_bytes(header_data[9:13], 'little')
        self.Header.HeaderCrc = int.from_bytes(header_data[13:17], 'little')
        
        return 1

    def WriteFileHeader(self):
        header_data = bytearray(17)

        if not self.OutputCarFile:
            return

        filename_bytes = self.Header.FileName.encode('ascii') + b'\x00'
        self.OutputCarFile.write(filename_bytes)
        self.Header.HeaderCrc = self.CalculateBlockCRC32(len(filename_bytes), self.CrcMask, filename_bytes)

        self.PackUnsignedData(1, self.Header.CompressionMethod, header_data, 0);
        self.PackUnsignedData(4, self.Header.OriginalSize, header_data, 1);
        self.PackUnsignedData(4, self.Header.CompressedSize, header_data, 5);
        self.PackUnsignedData(4, self.Header.OriginalCrc, header_data, 9);

        self.Header.HeaderCrc = self.CalculateBlockCRC32(13, self.Header.HeaderCrc, header_data)
        self.Header.HeaderCrc ^= self.CrcMask

        self.PackUnsignedData(4, self.Header.HeaderCrc, header_data, 13);

        self.OutputCarFile.write(header_data);

    def PackUnsignedData(self, number_of_bytes, number, buffer, offset):
        for i in range(number_of_bytes):
            buffer[offset + i] = number & 0xFF
            number >>= 8

    def WriteEndOfCarHeader(self):
        if self.OutputCarFile:
            self.OutputCarFile.write(b'\x00')
            self.OutputCarFile.close()
        if self.InputCarFile:
            self.InputCarFile.close()

    def Insert(self, inputTextFile, operation: str):
        print(f"{operation} {self.Header.FileName:<20}", end=' ')
        
        savedPositionOfHeader = self.OutputCarFile.tell() if self.OutputCarFile else 0
        self.Header.CompressionMethod = 2
        self.WriteFileHeader()
        
        savedPositionOfFile = self.OutputCarFile.tell() if self.OutputCarFile else 0
        inputTextFile.seek(0, 2)  # Seek to end
        self.Header.OriginalSize = inputTextFile.tell()
        inputTextFile.seek(0)
        
        if not self.LZSSCompress(inputTextFile):
            self.Header.CompressionMethod = 1
            if self.OutputCarFile:
                self.OutputCarFile.seek(savedPositionOfFile)
            inputTextFile.seek(0)
            self.Store(inputTextFile)
        
        if self.OutputCarFile:
            self.OutputCarFile.seek(savedPositionOfHeader)
            self.WriteFileHeader()
            self.OutputCarFile.seek(0, 2)  # Seek to end
        
        print(f"{self.RatioInPercent(self.Header.CompressedSize, self.Header.OriginalSize)}%")

    def Extract(self, destination):
        print(f"{self.Header.FileName:<20}", end=' ')
        
        try:
            if destination is None:
                output_file = open(self.Header.FileName, 'wb')
            else:
                output_file = destination
            
            if self.Header.CompressionMethod == 1:
                crc = self.Unstore(output_file)
            elif self.Header.CompressionMethod == 2:
                crc = self.LZSSExpand(output_file)
            else:
                print(f"Unknown method: {self.Header.CompressionMethod}")
                self.SkipOverFileFromInputCar()
                return
            
            if crc != self.Header.OriginalCrc:
                print("CRC error reading data")
            
            if destination is None:
                output_file.close()
                if crc != self.Header.OriginalCrc:
                    os.remove(self.Header.FileName)
            else:
                print("OK")
                
        except IOError:
            print(f"Can't open {self.Header.FileName}")
            print("Not extracted")
            self.SkipOverFileFromInputCar()

    def Store(self, inputTextFile) -> bool:
        buffer = bytearray(256)
        pacifier = 0
        self.Header.OriginalCrc = self.CrcMask
        
        while True:
            n = inputTextFile.readinto(buffer)
            if n == 0:
                break
                
            if self.OutputCarFile:
                self.OutputCarFile.write(buffer[:n])
            
            pacifier += 1
            if (pacifier & 15) == 0:
                print('.', end='')
        
        self.Header.CompressedSize = self.Header.OriginalSize
        self.Header.OriginalCrc ^= self.CrcMask
        return True

    def Unstore(self, destination) -> int:
        crc = self.CrcMask
        pacifier = 0
        
        while self.Header.OriginalSize != 0:
            count = min(256, self.Header.OriginalSize)
            data = self.InputCarFile.read(count)
            if len(data) != count:
                self.FatalError("Can't read from input CAR file")
            
            destination.write(data)
            pacifier += 1
            if (pacifier & 15) == 0:
                print('.', end='')
            
            self.Header.OriginalSize -= count
        
        return crc ^ self.CrcMask

    def ListCarFileEntry(self):
        methods = ["Stored", "LZSS"]
        print(f"{self.Header.FileName:<20} "
              f"{self.Header.OriginalSize:10} "
              f"{self.Header.CompressedSize:11} "
              f"{self.RatioInPercent(self.Header.CompressedSize, self.Header.OriginalSize):5}% "
              f"  {self.Header.OriginalCrc:08X} "
              f"{methods[self.Header.CompressionMethod - 1]:5}")

    def SkipOverFileFromInputCar(self):
        if self.InputCarFile:
            self.InputCarFile.seek(self.Header.CompressedSize, 1)

    def BuildCRCTable(self):
        for i in range(256):
            value = i
            for _ in range(8):
                if value & 1:
                    value = (value >> 1) ^ self.Crc32Polynomial
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

    def InitTree(self, r: int):
        #for i in range(self.WINDOW_SIZE + 1):
        #    self.Tree[i].Parent = self.UNUSED
        #    self.Tree[i].LargerChild = self.UNUSED
        #    self.Tree[i].SmallerChild = self.UNUSED
        
        self.Tree[self.TREE_ROOT].LargerChild = r
        self.Tree[r].Parent = self.TREE_ROOT
        self.Tree[r].LargerChild = self.UNUSED
        self.Tree[r].SmallerChild = self.UNUSED

    def ContractNode(self, old_node: int, new_node: int):
        self.Tree[new_node].Parent = self.Tree[old_node].Parent
        parent = self.Tree[old_node].Parent
        if self.Tree[parent].LargerChild == old_node:
            self.Tree[parent].LargerChild = new_node
        else:
            self.Tree[parent].SmallerChild = new_node
        self.Tree[old_node].Parent = self.UNUSED

    def ReplaceNode(self, old_node: int, new_node: int):
        parent = self.Tree[old_node].Parent
        if self.Tree[parent].SmallerChild == old_node:
            self.Tree[parent].SmallerChild = new_node
        else:
            self.Tree[parent].LargerChild = new_node
        
        	# Copy all attributes from old_node to new_node
        self.Tree[new_node].Parent = self.Tree[old_node].Parent
        self.Tree[new_node].SmallerChild = self.Tree[old_node].SmallerChild
        self.Tree[new_node].LargerChild = self.Tree[old_node].LargerChild
        
            # Update parent references of children
        if self.Tree[new_node].SmallerChild != self.UNUSED:
            self.Tree[self.Tree[new_node].SmallerChild].Parent = new_node
        if self.Tree[new_node].LargerChild != self.UNUSED:
            self.Tree[self.Tree[new_node].LargerChild].Parent = new_node
        
        self.Tree[old_node].Parent = self.UNUSED
        # self.Tree[new_node] = self.Tree[old_node]
        # smaller = self.Tree[new_node].SmallerChild
        # larger = self.Tree[new_node].LargerChild
        # if smaller != self.UNUSED:
        #     self.Tree[smaller].Parent = new_node
        # if larger != self.UNUSED:
        #     self.Tree[larger].Parent = new_node
        
        # self.Tree[old_node].Parent = self.UNUSED

    def FindNextNode(self, node: int) -> int:
        next_node = self.Tree[node].SmallerChild
        while self.Tree[next_node].LargerChild != self.UNUSED:
            next_node = self.Tree[next_node].LargerChild
        return next_node

    def DeleteString(self, p: int):
        if self.Tree[p].Parent == self.UNUSED:
            return
            
        if self.Tree[p].LargerChild == self.UNUSED:
            self.ContractNode(p, self.Tree[p].SmallerChild)
        elif self.Tree[p].SmallerChild == self.UNUSED:
            self.ContractNode(p, self.Tree[p].LargerChild)
        else:
            replacement = self.FindNextNode(p)
            self.DeleteString(replacement)
            self.ReplaceNode(p, replacement)

    def AddString(self, newNode: int, match_position: int) -> int:           
        num=0

        #if len(self.data1) == 524:
        #    print("Debug Break")

        if newNode == self.END_OF_STREAM:
            return
        testNode = self.Tree[self.TREE_ROOT].LargerChild
        match_length = 0

        while True:
            i = 0
            while i < self.LOOK_AHEAD_SIZE:
                indexNew  = self.ModWindow(newNode + i)
                indexTest  = self.ModWindow(testNode + i)
                delta = self.Window[indexNew] - self.Window[indexTest]
                if delta != 0:
                    break
                i += 1
            
            if i >= match_length:
                match_length = i
                match_position = testNode
                if match_length >= self.LOOK_AHEAD_SIZE:
                    self.ReplaceNode(testNode, newNode)
                    return match_length
                
            if delta >= 0: 
                child = self.Tree[testNode].LargerChild
            else:
                child = self.Tree[testNode].SmallerChild

            if child == self.UNUSED:
                child = newNode
                if delta >= 0:
                    self.Tree[testNode].LargerChild = child
                else:
                    self.Tree[testNode].SmallerChild = child
                self.Tree[newNode].Parent = testNode
                self.Tree[newNode].LargerChild = self.UNUSED
                self.Tree[newNode].SmallerChild = self.UNUSED
                return match_length
            testNode = child
            num+=1

    def InitOutputBuffer(self):
        self.DataBuffer[0] = 0
        self.FlagBitMask = 1
        self.BufferOffset = 1

    def FlushOutputBuffer(self) -> int:
        if self.BufferOffset == 1:
            return 1
            
        self.Header.CompressedSize += self.BufferOffset
        if self.Header.CompressedSize >= self.Header.OriginalSize:
            return 0
            
        if self.OutputCarFile:
            self.OutputCarFile.write(self.DataBuffer[:self.BufferOffset])
        self.InitOutputBuffer()
        return 1

    def OutputChar(self, data: int) -> int:
        self.DataBuffer[self.BufferOffset] = data
        self.BufferOffset += 1
        self.DataBuffer[0] |= self.FlagBitMask
        self.FlagBitMask <<= 1
        if self.FlagBitMask == 0x100:
            return self.FlushOutputBuffer()
        return 1

    def OutputPair(self, position: int, length: int) -> int:
        self.DataBuffer[self.BufferOffset] = (length << 4) | (position >> 8)
        self.DataBuffer[self.BufferOffset + 1] = position & 0xFF
        self.BufferOffset += 2
        self.FlagBitMask <<= 1
        if self.FlagBitMask == 0x100:
            return self.FlushOutputBuffer()
        return 1

    def InitInputBuffer(self):
        self.FlagBitMask = 1
        self.DataBuffer[0] = self.InputCarFile.read(1)[0]

    def InputBit(self) -> int:
        if self.FlagBitMask == 0x100:
            self.InitInputBuffer()
        self.FlagBitMask <<= 1;
        return ((self.DataBuffer[0]) & (self.FlagBitMask >> 1))

    def LZSSCompress(self, input_text_file) -> int:
        self.Header.CompressedSize = 0
        look_ahead_bytes: int = 0
        current_position: int = 1
        replace_count: int
        match_length: int = 0
        match_position: int
        cnt: int = 0
        self.Header.OriginalCrc = self.CrcMask
        self.InitOutputBuffer()

        # Fill initial window
        look_ahead_bytes = 0
        i = 0

        while i < self.LOOK_AHEAD_SIZE: # to get i = correct after loop
            byte = input_text_file.read(1)
            if byte == b'':
                break

            self.Window[current_position + i] = byte[0]
            self.Header.OriginalCrc = self.UpdateCharacterCRC32(self.Header.OriginalCrc, byte[0])
            i += 1
        
        look_ahead_bytes = i   
        self.InitTree(current_position)
        match_length = 0
        match_position = 0

        while look_ahead_bytes > 0:
            if match_length == None: # python sets match_length to None sometimes for some reason
                match_length = 0
            if match_length > look_ahead_bytes:
                match_length = look_ahead_bytes

            if match_length <= self.BREAK_EVEN:
                replace_count = 1
                # Output single character
                if self.OutputChar(self.Window[current_position]) == 0:
                    return 0

            else:
                # Output position/length pair
                if self.OutputPair(match_position, match_length - (self.BREAK_EVEN + 1)) == 0:
                    return 0
                replace_count = match_length
            
            # Replace replace_count bytes in the window
            i = 0
            while i < replace_count:

                # Delete old string
                self.DeleteString(self.ModWindow(current_position + self.LOOK_AHEAD_SIZE))
                
                # Get new character
                byte = input_text_file.read(1)

                if byte == b'':
                    look_ahead_bytes -= 1
                else:
                    self.Header.OriginalCrc = self.UpdateCharacterCRC32(self.Header.OriginalCrc, byte[0])
                    self.Window[self.ModWindow(current_position + self.LOOK_AHEAD_SIZE)] = byte[0]
                
                # Add new string
                current_position = self.ModWindow(current_position + 1)
                if current_position == 0:
                    print('.', end='', file=sys.stderr)
                
                if look_ahead_bytes > 0:
                    match_length = self.AddString(current_position, match_position)
                i += 1
        
        self.Header.OriginalCrc ^= self.CrcMask
        return self.FlushOutputBuffer()

    def LZSSExpand(self, output) -> int:
        crc = self.CrcMask
        output_count = 0
        self.InitInputBuffer()
        current_position = 1
        
        while output_count < self.Header.OriginalSize:
            if self.InputBit():
                # Read single character
                byte = self.input_car_file.read(1)
                if not byte:
                    break
                output.write(byte)
                output_count += 1
                crc = self.UpdateCharacterCRC32(crc, byte[0])
                self.Window[current_position] = byte[0]
                current_position = self.ModWindow(current_position + 1)
                if current_position == 0 and output != sys.stdout:
                    print('.', end='', file=sys.stderr)
            else:
                # Read position/length pair
                byte1 = self.input_car_file.read(1)
                byte2 = self.input_car_file.read(1)
                if not byte1 or not byte2:
                    break
                
                byte1 = byte1[0]
                byte2 = byte2[0]
                match_length = (byte1 >> 4) + self.BREAK_EVEN + 1
                match_position = ((byte1 & 0x0F) << 8) | byte2
                
                for i in range(match_length):
                    char = self.Window[self.ModWindow(match_position + i)]
                    output.write(bytes([char]))
                    output_count += 1
                    crc = self.UpdateCharacterCRC32(crc, char)
                    self.Window[current_position] = char
                    current_position = self.ModWindow(current_position + 1)
                    if current_position == 0 and output != sys.stdout:
                        print('.', end='', file=sys.stderr)
        
        return crc ^ self.CRC_MASK

    def FatalError(self, message: str, *args):
        print(f"\n{message % args}\n", file=sys.stderr)
        if self.output_car_file:
            self.output_car_file.close()
            try:
                os.remove(self.temp_file_name)
            except:
                pass
        sys.exit(1)

def main():
    print("CARMAN 1.0 : ")
    
    cp = CarProcessor()
    cp.BuildCRCTable()
    
    command = cp.ParseArguments(len(sys.argv), sys.argv)
    print("\n")
    
    if len(sys.argv) > 2:
        cp.OpenArchiveFiles(sys.argv[2], command)
        cp.BuildFileList(len(sys.argv), sys.argv, command)
    
    count = cp.AddFileListToArchive() if command == 'a' else 0
    
    if command == 'l':
        cp.PrintListTitles()
    
    count = cp.ProcessAllFilesInInputCar(command, count)
    
    if cp.OutputCarFile and count != 0:
        cp.WriteEndOfCarHeader()
        if os.path.exists(cp.CarFileName):
            os.remove(cp.CarFileName)

        try:
            # Copy the file to the new location
            shutil.copy2(cp.TempFileName, cp.CarFileName)  # copy2 preserves metadata like timestamps

            os.remove(cp.TempFileName)
        except FileNotFoundError:
            print(f"The file {cp.TempFileName} does not exist.")
        except PermissionError:
            print(f"You don't have permission to rename the file.")
        except Exception as e:
            print(f"An error occurred: {e}")
    
    print(f"\n{count} file{'s' if count != 1 else ''}\n")

if __name__ == "__main__":
    main()
