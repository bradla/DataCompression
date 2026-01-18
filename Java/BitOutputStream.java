import java.io.*;

public class BitOutputStream {
    private static final int PACIFIER_COUNT = 2047;
    
    private OutputStream out;
    private int currentByte;
    private int bitsInCurrentByte;
    private int pacifierCounter;
    
    public BitOutputStream(OutputStream out) {
        this.out = out;
        this.currentByte = 0;
        this.bitsInCurrentByte = 0;
        this.pacifierCounter = 0;
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
    
    public void close() throws IOException {
        flush();
        out.close();
    }
    
    public int getPacifierCounter() {
        return pacifierCounter;
    }
}
