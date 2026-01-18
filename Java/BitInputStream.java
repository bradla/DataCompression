import java.io.*;

public class BitInputStream {
    private static final int PACIFIER_COUNT = 2047;
    
    private InputStream in;
    private int currentByte;
    private int bitsRemaining;
    private int pacifierCounter;
    
    public BitInputStream(InputStream in) {
        this.in = in;
        this.currentByte = 0;
        this.bitsRemaining = 0;
        this.pacifierCounter = 0;
    }
    
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
}
