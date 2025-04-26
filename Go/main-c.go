// Bradford Arrington 2025
package main

import (
	"fmt"
	"os"
	"path/filepath"
	"runtime"
	"time"
)

func main() {
	start := time.Now() // Start wall-clock timer

	// Start memory stats
	var memStart runtime.MemStats
	runtime.ReadMemStats(&memStart)

	args := os.Args
	if len(args) < 3 {
		progName := filepath.Base(args[0])
		extension := filepath.Ext(progName)
		shortName := progName[:len(progName)-len(extension)]
		fmt.Printf("\nUsage: %s %s\n", shortName, "in-file out-file [options...]")
		os.Exit(0)
	}

	inputFilePath := args[1]
	outputFilePath := args[2]
	remainingArgs := args[3:]

	fmt.Printf("\nCompressing %s to %s\n", inputFilePath, outputFilePath)
	fmt.Printf("Using %s\n\n", CompressionName)

	//output, err := OpenOutputBitFile(outputFilePath)
	output, err := TrackWithResult2[*BIT_FILE, error]("OpenBitFile", func() (*BIT_FILE, error) { return OpenOutputBitFile(outputFilePath) })
	if err != nil {
		fmt.Println("Error opening output file:", err)
		os.Exit(1)
	}
	// Ensure output file is closed
	defer CloseOutputBitFile(output)
	//Track("CloseOutputBitFile", func() { _ = CloseOutputBitFile(output) })

	input, err := os.Open(inputFilePath)
	if err != nil {
		fmt.Println("Error opening input file:", err)
		os.Exit(1)
	}
	defer input.Close()

	//err = CompressFile(input, output, len(remainingArgs), remainingArgs)
	err = TrackWithResult("CompressFile", func() error { return CompressFile(input, output, len(remainingArgs), remainingArgs) })
	if err != nil {
		fmt.Println("Compression error:", err)
		os.Exit(1)
	}

	printRatios(inputFilePath, outputFilePath)

	// Stop timing
	elapsed := time.Since(start)

	// Read end memory
	var memEnd runtime.MemStats
	runtime.ReadMemStats(&memEnd)

	// --- Output ---
	fmt.Println("\n--- Performance Stats ---")
	fmt.Printf("Wall time: %s\n", elapsed)
	fmt.Printf("Allocated: %d KB\n", (memEnd.Alloc-memStart.Alloc)/1024)
	fmt.Printf("Total Allocated: %d KB\n", (memEnd.TotalAlloc-memStart.TotalAlloc)/1024)
	fmt.Printf("System Memory Used: %d KB\n", memEnd.Sys/1024)
	fmt.Printf("GC Cycles: %d\n", memEnd.NumGC)
}

func fileSize(fileName string) int64 {
	fileInfo, err := os.Stat(fileName)
	if err != nil {
		return 0
	}
	return fileInfo.Size()
}

func printRatios(inputFilePath, outputFilePath string) {
	inputSize := fileSize(inputFilePath)
	if inputSize == 0 {
		inputSize = 1
	}

	outputSize := fileSize(outputFilePath)
	ratio := 100 - int(outputSize*100/inputSize)

	fmt.Printf("\nInput bytes:         %d\n", inputSize)
	fmt.Printf("Output bytes:        %d\n", outputSize)
	fmt.Printf("Compression ratio: %d%%\n", ratio)
}
