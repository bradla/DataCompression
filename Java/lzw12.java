import java.io.*;

public class lzw12 {
    private static final int BITS = 12;
    private static final int MAX_CODE = (1 << BITS) - 1;
    private static final int TABLE_SIZE = 5021;
    private static final int END_OF_STREAM = 256;
    private static final int FIRST_CODE = 257;
    private static final int UNUSED = -1;
    
    static class DictionaryEntry {
        int codeValue = UNUSED;
        int parentCode = UNUSED;
        char character = 0;
    }
    
    private DictionaryEntry[] dict;
    private char[] decodeStack;
    
    public lzw12() {
        dict = new DictionaryEntry[TABLE_SIZE];
        decodeStack = new char[TABLE_SIZE];
        for (int i = 0; i < TABLE_SIZE; i++) {
            dict[i] = new DictionaryEntry();
        }
    }
    
    public void compressFile(InputStream input, BitOutputStream output, String[] args) throws IOException {
        int nextCode = FIRST_CODE;
        for (int i = 0; i < TABLE_SIZE; i++) {
            dict[i].codeValue = UNUSED;
        }
        
        int stringCode;
        int character;
        
        if ((stringCode = input.read()) == -1) {
            stringCode = END_OF_STREAM;
        } else {
            while ((character = input.read()) != -1) {
                int index = findChildNode(stringCode, character);
                if (dict[index].codeValue != UNUSED) {
                    stringCode = dict[index].codeValue;
                } else {
                    if (nextCode <= MAX_CODE) {
                        dict[index].codeValue = nextCode++;
                        dict[index].parentCode = stringCode;
                        dict[index].character = (char) character;
                    }
                    output.writeBits(stringCode, BITS);
                    stringCode = character;
                }
            }
        }
        
        output.writeBits(stringCode, BITS);
        output.writeBits(END_OF_STREAM, BITS);
        
        for (String arg : args) {
            System.out.println("Unknown argument: " + arg);
        }
    }
    
    public void expandFile(BitInputStream input, OutputStream output, String[] args) throws IOException {
        int nextCode = FIRST_CODE;
        
        for (int i = 0; i < TABLE_SIZE; i++) {
            dict[i].codeValue = UNUSED;
        }
        
        int oldCode = input.readBits(BITS);
        if (oldCode == END_OF_STREAM) {
            return;
        }
        
        int character = oldCode;
        output.write(oldCode);
        
        while (true) {
            int newCode = input.readBits(BITS);
            if (newCode == END_OF_STREAM) {
                break;
            }
            
            int count;
            if (newCode >= nextCode) {
                decodeStack[0] = (char) character;
                count = decodeString(1, oldCode);
            } else {
                count = decodeString(0, newCode);
            }
            
            character = decodeStack[count - 1];
            while (count > 0) {
                output.write(decodeStack[--count]);
            }
            
            if (nextCode <= MAX_CODE) {
                dict[nextCode].parentCode = oldCode;
                dict[nextCode].character = (char) character;
                nextCode++;
            }
            
            oldCode = newCode;
        }
        
        for (String arg : args) {
            System.out.println("Unknown argument: " + arg);
        }
    }
    
    private int findChildNode(int parentCode, int childCharacter) {
        int index = (childCharacter << (BITS - 8)) ^ parentCode;
        int offset = (index == 0) ? 1 : TABLE_SIZE - index;
        
        while (true) {
            if (dict[index].codeValue == UNUSED) {
                return index;
            }
            if (dict[index].parentCode == parentCode && 
                dict[index].character == (char) childCharacter) {
                return index;
            }
            index -= offset;
            if (index < 0) {
                index += TABLE_SIZE;
            }
        }
    }
    
    private int decodeString(int count, int code) {
        while (code > 255) {
            decodeStack[count++] = dict[code].character;
            code = dict[code].parentCode;
        }
        decodeStack[count++] = (char) code;
        return count;
    }
}
