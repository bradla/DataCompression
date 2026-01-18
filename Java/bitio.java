import java.io.*;

public class bitio {
    private static final int PACIFIER_COUNT = 2047;
    
    private InputStream in;
    private int currentByte;
    private int bitsRemaining;
    private int pacifierCounter;
       
    private OutputStream out;
    private int bitsInCurrentByte;
    
    public int readBit() throws IOException {
        if (bitsRemaining == 0) {
            currentByte = in.read();
            if (currentByte == -1) {
                return -1;
            }
            bitsRemaining = 8;
            pacifierCounter++;
            if ((pacifierCounter & PACIFIER_COUNT) == 0) {
                System.out.print(".");
            }
        }
        
        int bit = (currentByte >> (bitsRemaining - 1)) & 1;
        bitsRemaining--;
        return bit;
    }
    
    public int readBits(int numBits) throws IOException {
        int result = 0;
        for (int i = 0; i < numBits; i++) {
            int bit = readBit();
            if (bit == -1) {
                return -1;
            }
            result = (result << 1) | bit;
        }
        return result;
    }
    
    public int readByte() throws IOException {
        return readBits(8);
    }
    
    public void close() throws IOException {
        in.close();
    }
    
    public int getPacifierCounter() {
        return pacifierCounter;
    }
      
    public void writeBit(int bit) throws IOException {
        if (bit != 0) {
            currentByte |= (1 << (7 - bitsInCurrentByte));
        }
        bitsInCurrentByte++;
        
        if (bitsInCurrentByte == 8) {
            flush();
        }
    }
    
    public void writeBits(int bits, int numBits) throws IOException {
        int mask = 1 << (numBits - 1);
        while (mask != 0) {
            writeBit((bits & mask) != 0 ? 1 : 0);
            mask >>>= 1;
        }
    }
    
    public void writeByte(int b) throws IOException {
        writeBits(b & 0xFF, 8);
    }
    
    public void flush() throws IOException {
        if (bitsInCurrentByte > 0) {
            out.write(currentByte);
            pacifierCounter++;
            if ((pacifierCounter & PACIFIER_COUNT) == 0) {
                System.out.print(".");
            }
            currentByte = 0;
            bitsInCurrentByte = 0;
        }
    }
}
