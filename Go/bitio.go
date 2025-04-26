// Bradford Arrington 2025

package main

import (
	"fmt"
	"os"
)

const PACIFIER_COUNT = 2047

// BIT_FILE struct
type BIT_FILE struct {
	file            *os.File
	mask            byte
	rack            int
	pacifierCounter int
}

// OpenOutputBitFile opens a BIT_FILE for writing
func OpenOutputBitFile(name string) (*BIT_FILE, error) {
	file, err := os.Create(name)
	if err != nil {
		return nil, err
	}
	return &BIT_FILE{
		file:            file,
		mask:            0x80,
		pacifierCounter: 0,
	}, nil
}

// OpenInputBitFile opens a BIT_FILE for reading
func OpenInputBitFile(name string) (*BIT_FILE, error) {
	file, err := os.Open(name)
	if err != nil {
		return nil, err
	}
	return &BIT_FILE{
		file:            file,
		mask:            0x80,
		pacifierCounter: 0,
	}, nil
}

// CloseOutputBitFile closes a BIT_FILE opened for writing
func CloseOutputBitFile(bf *BIT_FILE) error {
	if bf.mask != 0x80 {
		_, err := bf.file.Write([]byte{byte(bf.rack)})
		if err != nil {
			return fmt.Errorf("fatal error in CloseOutputBitFile: %w", err)
		}
	}
	return bf.file.Close()
}

// CloseInputBitFile closes a BIT_FILE opened for reading
func CloseInputBitFile(bf *BIT_FILE) error {
	return bf.file.Close()
}

// OutputBit writes a single bit to the BIT_FILE
func OutputBit(bf *BIT_FILE, bit int) error {
	if bit != 0 {
		bf.rack |= int(bf.mask)
	}
	bf.mask >>= 1
	if bf.mask == 0 {
		_, err := bf.file.Write([]byte{byte(bf.rack)})
		if err != nil {
			return fmt.Errorf("fatal error in OutputBit: %w", err)
		}
		if (bf.pacifierCounter & PACIFIER_COUNT) == 0 {
			fmt.Print(".")
		}
		bf.pacifierCounter++
		bf.rack = 0
		bf.mask = 0x80
	}
	return nil
}

// OutputBits writes a sequence of bits to the BIT_FILE
func OutputBits(bf *BIT_FILE, code uint32, count int) error {
	mask := uint32(1) << (count - 1)
	for mask != 0 {
		if (code & mask) != 0 {
			bf.rack |= int(bf.mask)
		}
		bf.mask >>= 1
		if bf.mask == 0 {
			//err := bf.file.Write([]byte{byte(bf.rack)})
			_, err := bf.file.Write([]byte{byte(bf.rack)})
			if err != nil {
				return fmt.Errorf("fatal error in OutputBits: %w", err)
			}
			if (bf.pacifierCounter & PACIFIER_COUNT) == 0 {
				fmt.Print(".")
			}
			bf.pacifierCounter++
			bf.rack = 0
			bf.mask = 0x80
		}
		mask >>= 1
	}
	return nil
}

func InputBit(bf *BIT_FILE) (int, error) {
	if bf.mask == 0x80 {
		var readByte [1]byte
		_, err := bf.file.Read(readByte[:])
		if err != nil {
			return 0, fmt.Errorf("fatal error in InputBit: %w", err)
		}
		bf.rack = int(readByte[0])
		if (bf.pacifierCounter & PACIFIER_COUNT) == 0 {
			fmt.Print(".")
		}
		bf.pacifierCounter++
	}
	value := (bf.rack & int(bf.mask))
	bf.mask >>= 1
	if bf.mask == 0 {
		bf.mask = 0x80
	}
	if value != 0 {
		return 1, nil
	}
	return 0, nil
}

func InputBits(bf *BIT_FILE, bitCount int) (uint32, error) {
	var returnValue uint32
	mask := uint32(1) << (bitCount - 1)
	for mask != 0 {
		if bf.mask == 0x80 {
			var readByte [1]byte
			_, err := bf.file.Read(readByte[:])
			if err != nil {
				return 0, fmt.Errorf("fatal error in InputBits: %w", err)
			}
			bf.rack = int(readByte[0])
			if (bf.pacifierCounter & PACIFIER_COUNT) == 0 {
				fmt.Print(".")
			}
			bf.pacifierCounter++
		}
		if (bf.rack & int(bf.mask)) != 0 {
			returnValue |= mask
		}
		mask >>= 1
		bf.mask >>= 1
		if bf.mask == 0 {
			bf.mask = 0x80
		}
	}
	return returnValue, nil
}

func FilePrintBinary(code uint32, bits int) {
	mask := uint32(1) << (bits - 1)
	for mask != 0 {
		if (code & mask) != 0 {
			fmt.Print("1")
		} else {
			fmt.Print("0")
		}
		mask >>= 1
	}
}

func fatal_error(message string) {
	fmt.Println(message)
	os.Exit(1)
}
