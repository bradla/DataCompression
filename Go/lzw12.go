// Bradford Arrington 2025
package main

import (
	"bufio"
	"fmt"
	"io"
	"os"
)

const (
	BITS          = 12
	MAX_CODE      = (1 << BITS) - 1
	TABLE_SIZE    = 5021
	END_OF_STREAM = 256
	FIRST_CODE    = 257
	UNUSED        = -1
)

var CompressionName = "LZW 12 Bit Encoder"
var Usage = "in-file out-file\n\n"

type DictionaryEntry struct {
	CodeValue  int
	ParentCode int
	Character  byte
}

var dict [TABLE_SIZE]DictionaryEntry
var decodeStack [TABLE_SIZE]byte

// FindChildNode finds the index of a child node in the dictionary
func FindChildNode(parentCode int, childCharacter int) uint {
	index := (int(childCharacter) << (BITS - 8)) ^ parentCode
	offset := 1
	if index != 0 {
		offset = TABLE_SIZE - index
	}

	for {
		if dict[index].CodeValue == UNUSED {
			return uint(index)
		}

		if dict[index].ParentCode == parentCode && dict[index].Character == byte(childCharacter) {
			return uint(index)
		}

		index -= offset
		if index < 0 {
			index += TABLE_SIZE
		}
	}
}

func DecodeString(count uint32, code uint32) uint32 {
	currentCode := int(code)
	for currentCode > 255 {
		decodeStack[count] = dict[currentCode].Character
		count++
		currentCode = dict[currentCode].ParentCode
	}
	decodeStack[count] = byte(currentCode)
	count++
	return count
}

// readByte returns the next byte from the file as int,
// or -1 if EOF is reached.
func readByte(reader *bufio.Reader) int {
	b, err := reader.ReadByte()
	if err == io.EOF {
		return -1
	}
	if err != nil {
		fmt.Println("Error:", err)
		return -1
	}
	return int(b)
}

func CompressFile(input *os.File, output *BIT_FILE, argc int, argv []string) error {
	nextCode := FIRST_CODE
	var character int
	var stringCode int
	var index uint
	var i int

	for i = 0; i < TABLE_SIZE; i++ {
		dict[i].CodeValue = UNUSED
	}

	reader := bufio.NewReader(input)
	stringCode = readByte(reader)
	if stringCode == -1 {
		stringCode = END_OF_STREAM
	}

	for {
		character = readByte(reader)
		if character == -1 {
			break
		}

		index = FindChildNode(stringCode, character)

		if dict[index].CodeValue != -1 {
			stringCode = dict[index].CodeValue
		} else {
			if nextCode <= MAX_CODE {
				dict[index].CodeValue = nextCode
				dict[index].ParentCode = stringCode
				dict[index].Character = byte(character)
				nextCode++
			}
			OutputBits(output, uint32(stringCode), BITS)
			stringCode = character
		}
	}
	OutputBits(output, uint32(stringCode), BITS)
	OutputBits(output, uint32(END_OF_STREAM), BITS)

	for i = 0; i < argc; i++ {
		fmt.Printf("Unknown argument: %s\n", argv[i])
	}
	return nil
}

func ExpandFile(input *BIT_FILE, output *os.File, argc int, argv []string) error {
	nextCode := FIRST_CODE
	var newCode uint32
	var oldCode uint32
	var character int
	var count uint32

	// Read the first code
	firstCode, err := InputBits(input, BITS)
	if err != nil {
		if err != io.EOF {
			return fmt.Errorf("error reading first code: %w", err)
		}
		return nil // Empty input
	}
	oldCode = firstCode
	if oldCode == END_OF_STREAM {
		return nil
	}

	character = int(oldCode)
	_, err = output.Write([]byte{byte(oldCode)})
	if err != nil {
		return fmt.Errorf("error writing first byte: %w", err)
	}

	// Process input
	for {
		nextCodeInput, err := InputBits(input, BITS)
		if err != nil {
			if err != io.EOF {
				return fmt.Errorf("error reading code: %w", err)
			}
			break
		}
		newCode = nextCodeInput
		if newCode == END_OF_STREAM {
			break
		}

		if newCode >= uint32(nextCode) {
			decodeStack[0] = byte(character)
			count = DecodeString(1, oldCode)
		} else {
			count = DecodeString(0, newCode)
		}

		character = int(decodeStack[count-1])

		// Write decoded string to output
		for count > 0 {
			count--
			_, err := output.Write([]byte{decodeStack[count]})
			if err != nil {
				return fmt.Errorf("error writing decoded byte: %w", err)
			}
		}

		if nextCode <= MAX_CODE {
			dict[nextCode].ParentCode = int(oldCode)
			dict[nextCode].Character = byte(character)
			nextCode++
		}

		oldCode = newCode
	}

	for i := 0; i < argc; i++ {
		fmt.Printf("Unknown argument: %s\n", argv[len(argv)-argc+i])
	}

	return nil
}
