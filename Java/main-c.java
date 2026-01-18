import java.io.*;
import java.util.*;

public class mainc {
    
    public static void main(String[] args) {
        if (args.length < 2) {
            usageExit("LZWCompressor");
        }
        
        try {
            File inputFile = new File(args[0]);
            File outputFile = new File(args[1]);
            
            // Get additional arguments for compression/decompression
            String[] additionalArgs = new String[0];
            if (args.length > 2) {
                additionalArgs = Arrays.copyOfRange(args, 2, args.length);
            }
            
            System.out.printf("\nCompressing %s to %s\n", args[0], args[1]);
            System.out.println("Using LZW 12 Bit Encoder");
            
            // Measure compression time
            long startTime = System.currentTimeMillis();
            
            // Compress the file
            try (FileInputStream input = new FileInputStream(inputFile);
                 FileOutputStream output = new FileOutputStream(outputFile)) {
                
                BitOutputStream bitOutput = new BitOutputStream(output);
                lzw12 lzw = new lzw12();
                lzw.compressFile(input, bitOutput, additionalArgs);
                bitOutput.close();
            }
            
            long endTime = System.currentTimeMillis();
            
            // Print compression statistics
            printRatios(inputFile, outputFile);
            System.out.printf("Compression time: %d ms\n", endTime - startTime);
            
        } catch (IOException e) {
            System.err.println("Error: " + e.getMessage());
            e.printStackTrace();
        }
    }
    
    private static void usageExit(String progName) {
        // Extract short name from path
        String shortName = progName;
        int lastSlash = Math.max(progName.lastIndexOf('\\'), progName.lastIndexOf('/'));
        if (lastSlash != -1) {
            shortName = progName.substring(lastSlash + 1);
        }
        
        // Remove extension if present
        int dotIndex = shortName.lastIndexOf('.');
        if (dotIndex != -1) {
            shortName = shortName.substring(0, dotIndex);
        }
        
        System.out.printf("\nUsage: %s in-file out-file [options]\n\n", shortName);
        System.out.println("Options: (none currently supported)");
        System.exit(0);
    }
    
    private static void printRatios(File inputFile, File outputFile) throws IOException {
        long inputSize = fileSize(inputFile);
        if (inputSize == 0) {
            inputSize = 1;
        }
        
        long outputSize = fileSize(outputFile);
        int ratio = 100 - (int)(outputSize * 100L / inputSize);
        
        System.out.printf("\nInput bytes:        %d\n", inputSize);
        System.out.printf("Output bytes:       %d\n", outputSize);
        if (outputSize == 0) outputSize = 1;
        System.out.printf("Compression ratio:  %d%%\n", ratio);
    }
    
    private static long fileSize(File file) throws IOException {
        return file.length();
    }
}
