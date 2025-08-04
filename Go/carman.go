package main

import (
	"bufio"
	"bytes"
	"encoding/binary"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"
	"unicode"
)

const (
	BASE_HEADER_SIZE   = 19
	CRC_MASK          = 0xFFFFFFFF
	CRC32_POLYNOMIAL  = 0xEDB88320
	FILENAME_MAX      = 260 // Increased from 128 for modern systems
)

type Header struct {
	FileName          string
	CompressionMethod byte
	OriginalSize      uint32
	CompressedSize    uint32
	OriginalCRC       uint32
	HeaderCRC         uint32
}

type CarManager struct {
	TempFileName    string
	InputCarFile    *os.File
	CarFileName     string
	OutputCarFile   *os.File
	CurrentHeader   Header
	FileList        []string
	Ccitt32Table    [256]uint32
}

func main() {
	if len(os.Args) < 3 {
		usageExit()
		return
	}

	carMan := CarManager{}
	carMan.Run(os.Args[1:])
}

func (cm *CarManager) Run(args []string) {
	fmt.Fprintf(os.Stderr, "CARMAN 1.0 : ")
	cm.BuildCRCTable()

	command := cm.ParseArguments(args)
	fmt.Fprintln(os.Stderr)

	cm.OpenArchiveFiles(args[1], command)
	cm.BuildFileList(args[2:], command)

	count := 0
	if command == 'A' {
		count = cm.AddFileListToArchive()
	}

	if command == 'L' {
		cm.PrintListTitles()
	}

	count = cm.ProcessAllFilesInInputCar(command, count)

	if cm.OutputCarFile != nil && count != 0 {
		cm.WriteEndOfCarHeader()
		cm.OutputCarFile.Close()

		if err := os.Rename(cm.TempFileName, cm.CarFileName); err != nil {
			// Try removing the original file first if rename fails
			os.Remove(cm.CarFileName)
			if err := os.Rename(cm.TempFileName, cm.CarFileName); err != nil {
				cm.FatalError("Can't rename temporary file: %v", err)
			}
		}
	}

	if command != 'P' {
		fmt.Printf("\n%d file%s\n", count, pluralSuffix(count))
	} else {
		fmt.Fprintf(os.Stderr, "\n%d file%s\n", count, pluralSuffix(count))
	}
}

func pluralSuffix(count int) string {
	if count == 1 {
		return ""
	}
	return "s"
}

func (cm *CarManager) FatalError(format string, args ...interface{}) {
	fmt.Fprintln(os.Stderr)
	fmt.Fprintf(os.Stderr, format+"\n", args...)
	fmt.Fprintln(os.Stderr)

	if cm.OutputCarFile != nil {
		cm.OutputCarFile.Close()
		os.Remove(cm.TempFileName)
	}

	os.Exit(1)
}

func (cm *CarManager) BuildCRCTable() {
	for i := 0; i <= 255; i++ {
		value := uint32(i)
		for j := 8; j > 0; j-- {
			if (value & 1) != 0 {
				value = (value >> 1) ^ CRC32_POLYNOMIAL
			} else {
				value >>= 1
			}
		}
		cm.Ccitt32Table[i] = value
	}
}

func (cm *CarManager) CalculateBlockCRC32(count uint32, crc uint32, buffer []byte) uint32 {
	for i := uint32(0); i < count; i++ {
		temp1 := (crc >> 8) & 0x00FFFFFF
		temp2 := cm.Ccitt32Table[(crc^uint32(buffer[i]))&0xff]
		crc = temp1 ^ temp2
	}
	return crc
}

func (cm *CarManager) UpdateCharacterCRC32(crc uint32, c byte) uint32 {
	temp1 := (crc >> 8) & 0x00FFFFFF
	temp2 := cm.Ccitt32Table[(crc^uint32(c))&0xff]
	return temp1 ^ temp2
}

func (cm *CarManager) ParseArguments(args []string) byte {
	if len(args) < 2 || len(args[0]) > 1 {
		usageExit()
		return 0
	}

	command := byte(unicode.ToUpper(rune(args[0][0])))
	switch command {
	case 'X':
		fmt.Fprint(os.Stderr, "Extracting files")
	case 'R':
		fmt.Fprint(os.Stderr, "Replacing files")
	case 'P':
		fmt.Fprint(os.Stderr, "Print files to stdout")
	case 'T':
		fmt.Fprint(os.Stderr, "Testing integrity of files")
	case 'L':
		fmt.Fprint(os.Stderr, "Listing archive contents")
	case 'A':
		if len(args) <= 2 {
			usageExit()
		}
		fmt.Fprint(os.Stderr, "Adding/replacing files to archive")
	case 'D':
		if len(args) <= 2 {
			usageExit()
		}
		fmt.Fprint(os.Stderr, "Deleting files from archive")
	default:
		usageExit()
	}
	return command
}

func usageExit() {
	fmt.Fprintln(os.Stderr, "CARMAN -- Compressed ARchive MANager")
	fmt.Fprintln(os.Stderr, "Usage: carman command car-file [file ...]")
	fmt.Fprintln(os.Stderr, "Commands:")
	fmt.Fprintln(os.Stderr, "  a: Add files to a CAR archive (replace if present)")
	fmt.Fprintln(os.Stderr, "  x: Extract files from a CAR archive")
	fmt.Fprintln(os.Stderr, "  r: Replace files in a CAR archive")
	fmt.Fprintln(os.Stderr, "  d: Delete files from a CAR archive")
	fmt.Fprintln(os.Stderr, "  p: Print files on standard output")
	fmt.Fprintln(os.Stderr, "  l: List contents of a CAR archive")
	fmt.Fprintln(os.Stderr, "  t: Test files in a CAR archive")
	fmt.Fprintln(os.Stderr)
	os.Exit(1)
}

func (cm *CarManager) OpenArchiveFiles(name string, command byte) {
	cm.CarFileName = name

	// Try to open the input file
	var err error
	cm.InputCarFile, err = os.Open(cm.CarFileName)
	if err != nil {
		// If not found and no extension, try adding .car
		if filepath.Ext(cm.CarFileName) == "" {
			cm.CarFileName += ".car"
			cm.InputCarFile, err = os.Open(cm.CarFileName)
			if err != nil {
				cm.InputCarFile = nil
			}
		} else {
			cm.InputCarFile = nil
		}
	}

	if cm.InputCarFile == nil && command != 'A' {
		cm.FatalError("Can't open archive '%s'", cm.CarFileName)
	}

	if command == 'A' || command == 'R' || command == 'D' {
		tempDir := os.TempDir()
		baseName := filepath.Base(cm.CarFileName)
		ext := filepath.Ext(baseName)
		baseName = strings.TrimSuffix(baseName, ext)

		var tempName string
		var i int
		for i = 0; i < 10; i++ {
			tempName = filepath.Join(tempDir, fmt.Sprintf("%s.$$%d", baseName, i))
			if _, err := os.Stat(tempName); os.IsNotExist(err) {
				break
			}
		}

		if i == 10 {
			cm.FatalError("Can't open temporary file")
		}

		cm.TempFileName = tempName
		cm.OutputCarFile, err = os.Create(cm.TempFileName)
		if err != nil {
			cm.FatalError("Can't open temporary file %s: %v", cm.TempFileName, err)
		}
	}
}

func (cm *CarManager) BuildFileList(files []string, command byte) {
	if len(files) == 0 {
		cm.FileList = append(cm.FileList, "*")
	} else {
		for _, file := range files {
			if command == 'A' {
				// Handle wildcard expansion for Add command
				matches, err := filepath.Glob(file)
				if err == nil && len(matches) > 0 {
					for _, match := range matches {
						cm.FileList = append(cm.FileList, strings.ToLower(match))
					}
				} else {
					cm.FileList = append(cm.FileList, strings.ToLower(file))
				}
			} else {
				cm.FileList = append(cm.FileList, strings.ToLower(file))
			}

			if len(cm.FileList) > 99 {
				cm.FatalError("Too many file names")
			}
		}
	}
}

func (cm *CarManager) AddFileListToArchive() int {
	count := 0
	for _, file := range cm.FileList {
		inputTextFile, err := os.Open(file)
		if err != nil {
			cm.FatalError("Could not open %s to add to CAR file: %v", file, err)
		}

		fileNameOnly := filepath.Base(file)

		// Check for duplicates
		skip := false
		for i := 0; i < count; i++ {
			if strings.EqualFold(fileNameOnly, cm.FileList[i]) {
				fmt.Fprintf(os.Stderr, "Duplicate file name: %s   Skipping this file...", file)
				skip = true
				break
			}
		}

		if !skip {
			cm.CurrentHeader = Header{}
			cm.CurrentHeader.FileName = fileNameOnly
			cm.Insert(inputTextFile, "Adding")
			count++
		}

		inputTextFile.Close()
	}
	return count
}

func (cm *CarManager) ProcessAllFilesInInputCar(command byte, count int) int {
	var outputDestination *os.File
	var err error

	if command == 'P' {
		outputDestination = os.Stdout
	} else if command == 'T' {
		outputDestination, err = os.OpenFile(os.DevNull, os.O_WRONLY, 0)
		if err != nil {
			cm.FatalError("Can't open null device: %v", err)
		}
		defer outputDestination.Close()
	}

	for cm.InputCarFile != nil {
		hasMore, err := cm.ReadFileHeader()
		if err != nil || !hasMore {
			break
		}

		matched := cm.SearchFileList(cm.CurrentHeader.FileName)

		switch command {
		case 'D':
			if matched {
				cm.SkipOverFileFromInputCar()
				count++
			} else {
				cm.CopyFileFromInputCar()
			}
		case 'A':
			if matched {
				cm.SkipOverFileFromInputCar()
			} else {
				cm.CopyFileFromInputCar()
			}
		case 'L':
			if matched {
				cm.ListCarFileEntry()
				count++
			}
			cm.SkipOverFileFromInputCar()
		case 'P', 'X', 'T':
			if matched {
				cm.Extract(outputDestination)
				count++
			} else {
				cm.SkipOverFileFromInputCar()
			}
		case 'R':
			if matched {
				inputTextFile, err := os.Open(cm.CurrentHeader.FileName)
				if err != nil {
					fmt.Fprintf(os.Stderr, "Could not find %s for replacement, skipping\n", cm.CurrentHeader.FileName)
					cm.CopyFileFromInputCar()
				} else {
					cm.SkipOverFileFromInputCar()
					cm.Insert(inputTextFile, "Replacing")
					count++
					inputTextFile.Close()
				}
			} else {
				cm.CopyFileFromInputCar()
			}
		}
	}

	return count
}

func (cm *CarManager) SearchFileList(fileName string) bool {
	for _, file := range cm.FileList {
		if cm.WildCardMatch(fileName, file) {
			return true
		}
	}
	return false
}

func (cm *CarManager) WildCardMatch(str, pattern string) bool {
	strIndex := 0
	patternIndex := 0
	matchPos := 0
	starPos := -1

	for strIndex < len(str) {
		if patternIndex < len(pattern) && (pattern[patternIndex] == '?' || pattern[patternIndex] == str[strIndex]) {
			strIndex++
			patternIndex++
		} else if patternIndex < len(pattern) && pattern[patternIndex] == '*' {
			starPos = patternIndex
			matchPos = strIndex
			patternIndex++
		} else if starPos != -1 {
			patternIndex = starPos + 1
			matchPos++
			strIndex = matchPos
		} else {
			return false
		}
	}

	for patternIndex < len(pattern) && pattern[patternIndex] == '*' {
		patternIndex++
	}

	return patternIndex == len(pattern)
}

func (cm *CarManager) SkipOverFileFromInputCar() {
	_, err := cm.InputCarFile.Seek(int64(cm.CurrentHeader.CompressedSize), io.SeekCurrent)
	if err != nil {
		cm.FatalError("Error seeking in input file: %v", err)
	}
}

func (cm *CarManager) CopyFileFromInputCar() {
	cm.WriteFileHeader()
	buffer := make([]byte, 256)
	remaining := cm.CurrentHeader.CompressedSize

	for remaining > 0 {
		count := int(remaining)
		if count > 256 {
			count = 256
		}

		n, err := io.ReadFull(cm.InputCarFile, buffer[:count])
		if err != nil {
			cm.FatalError("Error reading input file %s: %v", cm.CurrentHeader.FileName, err)
		}

		_, err = cm.OutputCarFile.Write(buffer[:n])
		if err != nil {
			cm.FatalError("Error writing to output CAR file: %v", err)
		}

		remaining -= uint32(n)
	}
}

func (cm *CarManager) PrintListTitles() {
	fmt.Println()
	fmt.Println("                       Original  Compressed")
	fmt.Println("     Filename            Size       Size     Ratio   CRC-32   Method")
	fmt.Println("------------------     --------  ----------  -----  --------  ------")
}

func (cm *CarManager) ListCarFileEntry() {
	methods := []string{"Stored", "LZSS"}
	fmt.Printf("%-20s %10d  %10d  %4d%%  %08x  %s\n",
		cm.CurrentHeader.FileName,
		cm.CurrentHeader.OriginalSize,
		cm.CurrentHeader.CompressedSize,
		cm.RatioInPercent(cm.CurrentHeader.CompressedSize, cm.CurrentHeader.OriginalSize),
		cm.CurrentHeader.OriginalCRC,
		methods[cm.CurrentHeader.CompressionMethod-1])
}

func (cm *CarManager) RatioInPercent(compressed, original uint32) int {
	if original == 0 {
		return 0
	}
	return int(100 - (100*compressed)/original)
}

func (cm *CarManager) ReadFileHeader() (bool, error) {
	var fileNameBuilder strings.Builder
	var c byte
	var err error

	// Read file name
	for {
		buf := make([]byte, 1)
		_, err = cm.InputCarFile.Read(buf)
		if err != nil {
			return false, err
		}
		c = buf[0]

		if c == 0 {
			break
		}

		fileNameBuilder.WriteByte(c)
		if fileNameBuilder.Len() == FILENAME_MAX {
			cm.FatalError("File name exceeded maximum in header")
		}
	}

	if fileNameBuilder.Len() == 0 {
		return false, nil
	}

	cm.CurrentHeader.FileName = fileNameBuilder.String()
	fileNameBytes := []byte(cm.CurrentHeader.FileName + "\x00")
	headerCRC := cm.CalculateBlockCRC32(uint32(len(fileNameBytes)), CRC_MASK, fileNameBytes)

	headerData := make([]byte, 17)
	_, err = io.ReadFull(cm.InputCarFile, headerData)
	if err != nil {
		return false, err
	}

	cm.CurrentHeader.CompressionMethod = headerData[0]
	cm.CurrentHeader.OriginalSize = binary.LittleEndian.Uint32(headerData[1:5])
	cm.CurrentHeader.CompressedSize = binary.LittleEndian.Uint32(headerData[5:9])
	cm.CurrentHeader.OriginalCRC = binary.LittleEndian.Uint32(headerData[9:13])
	cm.CurrentHeader.HeaderCRC = binary.LittleEndian.Uint32(headerData[13:17])

	headerCRC = cm.CalculateBlockCRC32(13, headerCRC, headerData[:13])
	headerCRC ^= CRC_MASK

	if cm.CurrentHeader.HeaderCRC != headerCRC {
		cm.FatalError("Header checksum error for file %s", cm.CurrentHeader.FileName)
	}

	return true, nil
}

func (cm *CarManager) WriteFileHeader() {
	fileNameBytes := []byte(cm.CurrentHeader.FileName + "\x00")
	_, err := cm.OutputCarFile.Write(fileNameBytes)
	if err != nil {
		cm.FatalError("Error writing file header: %v", err)
	}

	cm.CurrentHeader.HeaderCRC = cm.CalculateBlockCRC32(uint32(len(fileNameBytes)), CRC_MASK, fileNameBytes)

	headerData := make([]byte, 17)
	headerData[0] = cm.CurrentHeader.CompressionMethod
	binary.LittleEndian.PutUint32(headerData[1:5], cm.CurrentHeader.OriginalSize)
	binary.LittleEndian.PutUint32(headerData[5:9], cm.CurrentHeader.CompressedSize)
	binary.LittleEndian.PutUint32(headerData[9:13], cm.CurrentHeader.OriginalCRC)

	cm.CurrentHeader.HeaderCRC = cm.CalculateBlockCRC32(13, cm.CurrentHeader.HeaderCRC, headerData[:13])
	cm.CurrentHeader.HeaderCRC ^= CRC_MASK

	binary.LittleEndian.PutUint32(headerData[13:17], cm.CurrentHeader.HeaderCRC)
	_, err = cm.OutputCarFile.Write(headerData)
	if err != nil {
		cm.FatalError("Error writing file header: %v", err)
	}
}

func (cm *CarManager) WriteEndOfCarHeader() {
	_, err := cm.OutputCarFile.Write([]byte{0})
	if err != nil {
		cm.FatalError("Error writing end of CAR header: %v", err)
	}
}

func (cm *CarManager) Insert(inputTextFile *os.File, operation string) {
	fmt.Fprintf(os.Stderr, "%s %-20s", operation, cm.CurrentHeader.FileName)

	savedPositionOfHeader, err := cm.OutputCarFile.Seek(0, io.SeekCurrent)
	if err != nil {
		cm.FatalError("Error getting file position: %v", err)
	}

	cm.CurrentHeader.CompressionMethod = 2
	cm.WriteFileHeader()

	savedPositionOfFile, err := cm.OutputCarFile.Seek(0, io.SeekCurrent)
	if err != nil {
		cm.FatalError("Error getting file position: %v", err)
	}

	fileInfo, err := inputTextFile.Stat()
	if err != nil {
		cm.FatalError("Error getting file info: %v", err)
	}
	cm.CurrentHeader.OriginalSize = uint32(fileInfo.Size())

	_, err = inputTextFile.Seek(0, io.SeekStart)
	if err != nil {
		cm.FatalError("Error seeking in input file: %v", err)
	}

	if !cm.LZSSCompress(inputTextFile) {
		cm.CurrentHeader.CompressionMethod = 1
		_, err = cm.OutputCarFile.Seek(savedPositionOfFile, io.SeekStart)
		if err != nil {
			cm.FatalError("Error seeking in output file: %v", err)
		}

		_, err = inputTextFile.Seek(0, io.SeekStart)
		if err != nil {
			cm.FatalError("Error seeking in input file: %v", err)
		}

		cm.Store(inputTextFile)
	}

	_, err = cm.OutputCarFile.Seek(savedPositionOfHeader, io.SeekStart)
	if err != nil {
		cm.FatalError("Error seeking in output file: %v", err)
	}

	cm.WriteFileHeader()

	_, err = cm.OutputCarFile.Seek(0, io.SeekEnd)
	if err != nil {
		cm.FatalError("Error seeking in output file: %v", err)
	}

	fmt.Printf(" %d%%\n", cm.RatioInPercent(cm.CurrentHeader.CompressedSize, cm.CurrentHeader.OriginalSize))
}

func (cm *CarManager) Extract(destination *os.File) {
	fmt.Fprintf(os.Stderr, "%-20s ", cm.CurrentHeader.FileName)
	var outputFile *os.File
	var err error
	shouldClose := false

	if destination == nil {
		outputFile, err = os.Create(cm.CurrentHeader.FileName)
		if err != nil {
			fmt.Fprintf(os.Stderr, "Can't open %s\nNot extracted\n", cm.CurrentHeader.FileName)
			cm.SkipOverFileFromInputCar()
			return
		}
		shouldClose = true
	} else {
		outputFile = destination
	}

	var crc uint32
	switch cm.CurrentHeader.CompressionMethod {
	case 1:
		crc = cm.Unstore(outputFile)
	case 2:
		crc = cm.LZSSExpand(outputFile)
	default:
		fmt.Fprintf(os.Stderr, "Unknown method: %c\n", cm.CurrentHeader.CompressionMethod)
		cm.SkipOverFileFromInputCar()
		if shouldClose {
			outputFile.Close()
			os.Remove(cm.CurrentHeader.FileName)
		}
		return
	}

	if crc != cm.CurrentHeader.OriginalCRC {
		fmt.Fprint(os.Stderr, "CRC error reading data\n")
		if shouldClose {
			outputFile.Close()
			os.Remove(cm.CurrentHeader.FileName)
		}
		return
	}

	if shouldClose {
		outputFile.Close()
	}

	fmt.Fprint(os.Stderr, " OK\n")
}

func (cm *CarManager) Store(inputTextFile *os.File) bool {
	buffer := make([]byte, 256)
	pacifier := 0
	cm.CurrentHeader.OriginalCRC = CRC_MASK
	cm.CurrentHeader.CompressedSize = 0

	for {
		n, err := inputTextFile.Read(buffer)
		if err != nil && err != io.EOF {
			cm.FatalError("Error reading input file: %v", err)
		}
		if n == 0 {
			break
		}

		_, err = cm.OutputCarFile.Write(buffer[:n])
		if err != nil {
			cm.FatalError("Error writing to output CAR file: %v", err)
		}

		cm.CurrentHeader.OriginalCRC = cm.CalculateBlockCRC32(uint32(n), cm.CurrentHeader.OriginalCRC, buffer[:n])
		cm.CurrentHeader.CompressedSize += uint32(n)

		if (pacifier & 15) == 0 {
			fmt.Fprint(os.Stderr, ".")
		}
		pacifier++
	}

	cm.CurrentHeader.OriginalCRC ^= CRC_MASK
	return true
}

func (cm *CarManager) Unstore(destination *os.File) uint32 {
	crc := CRC_MASK
	buffer := make([]byte, 256)
	pacifier := 0
	remaining := cm.CurrentHeader.OriginalSize

	for remaining > 0 {
		count := int(remaining)
		if count > 256 {
			count = 256
		}

		n, err := io.ReadFull(cm.InputCarFile, buffer[:count])
		if err != nil {
			cm.FatalError("Can't read from input CAR file: %v", err)
		}

		_, err = destination.Write(buffer[:n])
		if err != nil {
			fmt.Fprint(os.Stderr, "Error writing to output file")
			return ^cm.CurrentHeader.OriginalCRC
		}
        //n = uint32(n) // Convert to uint32
        crc = cm.CalculateBlockCRC32(n, crc, buffer[:n])

		//crc = cm.CalculateBlockCRC32(uint64(n), crc, buffer[:n])
		if destination != os.Stdout && (pacifier&15) == 0 {
			fmt.Fprint(os.Stderr, ".")
		}
		pacifier++

		remaining -= uint32(n)
	}

	return crc ^ CRC_MASK
}

// LZSS Constants
const (
	INDEX_BIT_COUNT      = 12
	LENGTH_BIT_COUNT     = 4
	WINDOW_SIZE          = 1 << INDEX_BIT_COUNT
	RAW_LOOK_AHEAD_SIZE  = 1 << LENGTH_BIT_COUNT
	BREAK_EVEN           = (1 + INDEX_BIT_COUNT + LENGTH_BIT_COUNT) / 9
	LOOK_AHEAD_SIZE      = RAW_LOOK_AHEAD_SIZE + BREAK_EVEN
	TREE_ROOT            = WINDOW_SIZE
	END_OF_STREAM        = 0
	UNUSED               = 0
)

type TreeNode struct {
	Parent        int
	SmallerChild int
	LargerChild  int
}

type LZSSEncoder struct {
	Window       [WINDOW_SIZE]byte
	tree         [WINDOW_SIZE + 1]TreeNode
	DataBuffer   [17]byte
	FlagBitMask  int
	BufferOffset int
	CarManager   *CarManager
}

func (cm *CarManager) modWindow(a int) int {
	return a & (WINDOW_SIZE - 1)
}

func (cm *CarManager) initTree(r int) {
	for i := 0; i < WINDOW_SIZE+1; i++ {
		cm.tree[i].Parent = UNUSED
		cm.tree[i].LargerChild = UNUSED
		cm.tree[i].SmallerChild = UNUSED
	}

	cm.tree[TREE_ROOT].LargerChild = r
	cm.tree[r].Parent = TREE_ROOT
	cm.tree[r].LargerChild = UNUSED
	cm.tree[r].SmallerChild = UNUSED
}

func (cm *CarManager) contractNode(oldNode, newNode int) {
	cm.tree[newNode].Parent = cm.tree[oldNode].Parent
	if cm.tree[cm.tree[oldNode].Parent].LargerChild == oldNode {
		cm.tree[cm.tree[oldNode].Parent].LargerChild = newNode
	} else {
		cm.tree[cm.tree[oldNode].Parent].SmallerChild = newNode
	}

	cm.tree[oldNode].Parent = UNUSED
}

func (cm *CarManager) replaceNode(oldNode, newNode int) {
	parent := cm.tree[oldNode].Parent
	if cm.tree[parent].SmallerChild == oldNode {
		cm.tree[parent].SmallerChild = newNode
	} else {
		cm.tree[parent].LargerChild = newNode
	}

	cm.tree[newNode] = cm.tree[oldNode]
	cm.tree[cm.tree[newNode].SmallerChild].Parent = newNode
	cm.tree[cm.tree[newNode].LargerChild].Parent = newNode
	cm.tree[oldNode].Parent = UNUSED
}

func (cm *CarManager) findNextNode(node int) int {
	next := cm.tree[node].SmallerChild
	for cm.tree[next].LargerChild != UNUSED {
		next = cm.tree[next].LargerChild
	}
	return next
}

func (cm *CarManager) deleteString(p int) {
	if cm.tree[p].Parent == UNUSED {
		return
	}

	if cm.tree[p].LargerChild == UNUSED {
		cm.contractNode(p, cm.tree[p].SmallerChild)
	} else if cm.tree[p].SmallerChild == UNUSED {
		cm.contractNode(p, cm.tree[p].LargerChild)
	} else {
		replacement := cm.findNextNode(p)
		cm.deleteString(replacement)
		cm.replaceNode(p, replacement)
	}
}

func (cm *CarManager) addString(newNode int) (int, int) {
	testNode := cm.tree[TREE_ROOT].LargerChild
	matchLength := 0
	matchPosition := 0

	for {
		i := 0
		for ; i < LOOK_AHEAD_SIZE; i++ {
			delta := int(cm.window[cm.modWindow(newNode+i)]) - int(cm.window[cm.modWindow(testNode+i)])
			if delta != 0 {
				break
			}
		}

		if i >= matchLength {
			matchLength = i
			matchPosition = testNode
			if matchLength >= LOOK_AHEAD_SIZE {
				cm.replaceNode(testNode, newNode)
				return matchLength, matchPosition
			}
		}

		var child *int
		if delta >= 0 {
			child = &cm.tree[testNode].LargerChild
		} else {
			child = &cm.tree[testNode].SmallerChild
		}

		if *child == UNUSED {
			*child = newNode
			cm.tree[newNode].Parent = testNode
			cm.tree[newNode].LargerChild = UNUSED
			cm.tree[newNode].SmallerChild = UNUSED
			return matchLength, matchPosition
		}
		testNode = *child
	}
}

func (cm *CarManager) initOutputBuffer() {
	cm.dataBuffer[0] = 0
	cm.flagBitMask = 1
	cm.bufferOffset = 1
}

func (cm *CarManager) flushOutputBuffer() bool {
	if cm.bufferOffset == 1 {
		return true
	}

	cm.CurrentHeader.CompressedSize += uint32(cm.bufferOffset)
	if cm.CurrentHeader.CompressedSize >= cm.CurrentHeader.OriginalSize {
		return false
	}

	_, err := cm.OutputCarFile.Write(cm.dataBuffer[:cm.bufferOffset])
	if err != nil {
		cm.FatalError("Error writing compressed data to CAR file: %v", err)
	}

	cm.initOutputBuffer()
	return true
}

func (cm *CarManager) outputChar(data byte) bool {
	cm.dataBuffer[cm.bufferOffset] = data
	cm.bufferOffset++
	cm.dataBuffer[0] |= byte(cm.flagBitMask)
	cm.flagBitMask <<= 1
	if cm.flagBitMask == 0x100 {
		return cm.flushOutputBuffer()
	}
	return true
}

func (cm *CarManager) outputPair(position, length int) bool {
	cm.dataBuffer[cm.bufferOffset] = byte(length << 4)
	cm.dataBuffer[cm.bufferOffset] |= byte(position >> 8)
	cm.bufferOffset++
	cm.dataBuffer[cm.bufferOffset] = byte(position & 0xff)
	cm.bufferOffset++
	cm.flagBitMask <<= 1
	if cm.flagBitMask == 0x100 {
		return cm.flushOutputBuffer()
	}
	return true
}

func (cm *CarManager) initInputBuffer() error {
	buf := make([]byte, 1)
	_, err := cm.InputCarFile.Read(buf)
	if err != nil {
		return err
	}
	cm.dataBuffer[0] = buf[0]
	cm.flagBitMask = 1
	return nil
}

func (cm *CarManager) inputBit() (bool, error) {
	if cm.flagBitMask == 0x100 {
		if err := cm.initInputBuffer(); err != nil {
			return false, err
		}
	}

	result := (cm.dataBuffer[0] & byte(cm.flagBitMask>>1)) != 0
	cm.flagBitMask <<= 1
	return result, nil
}

func (cm *CarManager) LZSSCompress(inputTextFile *os.File) bool {
	cm.initOutputBuffer()
	cm.CurrentHeader.OriginalCRC = CRC_MASK
	cm.CurrentHeader.CompressedSize = 0

	currentPosition := 1
	lookAheadBytes := 0

	// Initialize window
	for i := 0; i < LOOK_AHEAD_SIZE; i++ {
		buf := make([]byte, 1)
		_, err := inputTextFile.Read(buf)
		if err != nil {
			if err == io.EOF {
				break
			}
			cm.FatalError("Error reading input file: %v", err)
		}

		cm.window[currentPosition+i] = buf[0]
		cm.CurrentHeader.OriginalCRC = cm.UpdateCharacterCRC32(cm.CurrentHeader.OriginalCRC, buf[0])
		lookAheadBytes++
	}

	cm.initTree(currentPosition)
	matchLength := 0
	matchPosition := 0

	for lookAheadBytes > 0 {
		if matchLength > lookAheadBytes {
			matchLength = lookAheadBytes
		}

		if matchLength <= BREAK_EVEN {
			if !cm.outputChar(cm.window[currentPosition]) {
				return false
			}
		} else {
			if !cm.outputPair(matchPosition, matchLength-(BREAK_EVEN+1)) {
				return false
			}
		}

		for i := 0; i < matchLength; i++ {
			cm.deleteString(cm.modWindow(currentPosition + LOOK_AHEAD_SIZE))

			buf := make([]byte, 1)
			_, err := inputTextFile.Read(buf)
			if err != nil {
				if err == io.EOF {
					lookAheadBytes--
					continue
				}
				cm.FatalError("Error reading input file: %v", err)
			}

			cm.CurrentHeader.OriginalCRC = cm.UpdateCharacterCRC32(cm.CurrentHeader.OriginalCRC, buf[0])
			cm.window[cm.modWindow(currentPosition+LOOK_AHEAD_SIZE)] = buf[0]
			currentPosition = cm.modWindow(currentPosition + 1)

			if currentPosition == 0 {
				fmt.Fprint(os.Stderr, ".")
			}

			if lookAheadBytes > 0 {
				matchLength, matchPosition = cm.addString(currentPosition)
			}
		}
	}

	cm.CurrentHeader.OriginalCRC ^= CRC_MASK
	return cm.flushOutputBuffer()
}

func (cm *CarManager) LZSSExpand(destination *os.File) uint32 {
	currentPosition := 1
	crc := CRC_MASK
	outputCount := uint32(0)

	if err := cm.initInputBuffer(); err != nil {
		cm.FatalError("Error initializing input buffer: %v", err)
	}

	for outputCount < cm.CurrentHeader.OriginalSize {
		bit, err := cm.inputBit()
		if err != nil {
			cm.FatalError("Error reading input bit: %v", err)
		}

		if bit {
			buf := make([]byte, 1)
			_, err := cm.InputCarFile.Read(buf)
			if err != nil {
				cm.FatalError("Error reading from input CAR file: %v", err)
			}

			_, err = destination.Write(buf)
			if err != nil {
				cm.FatalError("Error writing to output file: %v", err)
			}

			outputCount++
			crc = cm.UpdateCharacterCRC32(crc, buf[0])
			cm.window[currentPosition] = buf[0]
			currentPosition = cm.modWindow(currentPosition + 1)

			if currentPosition == 0 && destination != os.Stdout {
				fmt.Fprint(os.Stderr, ".")
			}
		} else {
			matchLengthBuf := make([]byte, 1)
			_, err := cm.InputCarFile.Read(matchLengthBuf)
			if err != nil {
				cm.FatalError("Error reading from input CAR file: %v", err)
			}

			matchPositionBuf := make([]byte, 1)
			_, err = cm.InputCarFile.Read(matchPositionBuf)
			if err != nil {
				cm.FatalError("Error reading from input CAR file: %v", err)
			}

			matchLength := int(matchLengthBuf[0])
			matchPosition := int(matchPositionBuf[0])
			matchPosition |= (matchLength & 0xf) << 8
			matchLength >>= 4
			matchLength += BREAK_EVEN
			outputCount += uint32(matchLength + 1)

			for i := 0; i <= matchLength; i++ {
				c := cm.window[cm.modWindow(matchPosition+i)]
				_, err := destination.Write([]byte{c})
				if err != nil {
					cm.FatalError("Error writing to output file: %v", err)
				}

				crc = cm.UpdateCharacterCRC32(crc, c)
				cm.window[currentPosition] = c
				currentPosition = cm.modWindow(currentPosition + 1)

				if currentPosition == 0 && destination != os.Stdout {
					fmt.Fprint(os.Stderr, ".")
				}
			}
		}
	}

	return crc ^ CRC_MASK
}