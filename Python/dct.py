import math
import numpy as np
from typing import List, Tuple

# Constants
ROWS = 200
COLS = 320
N = 8

# Global variables
PixelStrip = np.zeros((N, COLS), dtype=np.uint8)
C = np.zeros((N, N))
Ct = np.zeros((N, N))
InputRunLength = 0
OutputRunLength = 0
Quantum = np.zeros((N, N), dtype=int)

# Zigzag pattern for DCT coefficient ordering
ZigZag = [
    (0, 0), (0, 1), (1, 0), (2, 0), (1, 1), (0, 2), (0, 3), (1, 2),
    (2, 1), (3, 0), (4, 0), (3, 1), (2, 2), (1, 3), (0, 4), (0, 5),
    (1, 4), (2, 3), (3, 2), (4, 1), (5, 0), (6, 0), (5, 1), (4, 2),
    (3, 3), (2, 4), (1, 5), (0, 6), (0, 7), (1, 6), (2, 5), (3, 4),
    (4, 3), (5, 2), (6, 1), (7, 0), (7, 1), (6, 2), (5, 3), (4, 4),
    (3, 5), (2, 6), (1, 7), (2, 7), (3, 6), (4, 5), (5, 4), (6, 3),
    (7, 2), (7, 3), (6, 4), (5, 5), (4, 6), (3, 7), (4, 7), (5, 6),
    (6, 5), (7, 4), (7, 5), (6, 6), (5, 7), (6, 7), (7, 6), (7, 7)
]

def ROUND(a: float) -> int:
    """Round a float value to nearest integer"""
    return int(a + 0.5) if a >= 0 else int(a - 0.5)

def initialize(quality: int):
    """Initialize DCT matrices and quantization table"""
    global C, Ct, Quantum, InputRunLength, OutputRunLength
    
    # Setup quantization matrix
    for i in range(N):
        for j in range(N):
            Quantum[i][j] = 1 + ((1 + i + j) * quality)
    
    OutputRunLength = 0
    InputRunLength = 0
    
    # Setup DCT matrices
    for j in range(N):
        C[0][j] = 1.0 / math.sqrt(N)
        Ct[j][0] = C[0][j]
    
    for i in range(1, N):
        for j in range(N):
            C[i][j] = math.sqrt(2.0 / N) * math.cos(
                math.pi * (2 * j + 1) * i / (2.0 * N)
            )
            Ct[j][i] = C[i][j]

def read_pixel_strip(input_file) -> np.ndarray:
    """Read a strip of N rows from input file"""
    strip = np.zeros((N, COLS), dtype=np.uint8)
    
    for row in range(N):
        for col in range(COLS):
            byte = input_file.read(1)
            if not byte:
                raise EOFError("Error reading input grey scale file")
            strip[row][col] = ord(byte)
    
    return strip

def input_code(bit_reader) -> int:
    """Read encoded DCT coefficient from bit stream"""
    global InputRunLength
    
    if InputRunLength > 0:
        InputRunLength -= 1
        return 0
    
    bit_count = bit_reader.read_bits(2)
    if bit_count == 0:
        InputRunLength = bit_reader.read_bits(4)
        return 0
    
    if bit_count == 1:
        bit_count = bit_reader.read_bits(1) + 1
    else:
        bit_count = bit_reader.read_bits(2) + (bit_count << 2) - 5
    
    result = bit_reader.read_bits(bit_count)
    if result & (1 << (bit_count - 1)):
        return result
    return result - (1 << bit_count) + 1

def read_dct_data(bit_reader) -> np.ndarray:
    """Read and decode DCT data from compressed file"""
    input_data = np.zeros((N, N), dtype=int)
    
    for i in range(N * N):
        row, col = ZigZag[i]
        input_data[row][col] = input_code(bit_reader) * Quantum[row][col]
    
    return input_data

def output_code(bit_writer, code: int):
    """Output encoded DCT coefficient to bit stream"""
    global OutputRunLength
    
    if code == 0:
        OutputRunLength += 1
        return
    
    if OutputRunLength != 0:
        while OutputRunLength > 0:
            bit_writer.write_bits(0, 2)
            if OutputRunLength <= 16:
                bit_writer.write_bits(OutputRunLength - 1, 4)
                OutputRunLength = 0
            else:
                bit_writer.write_bits(15, 4)
                OutputRunLength -= 16
    
    abs_code = abs(code)
    top_of_range = 1
    bit_count = 1
    
    while abs_code > top_of_range:
        bit_count += 1
        top_of_range = ((top_of_range + 1) * 2) - 1
    
    if bit_count < 3:
        bit_writer.write_bits(bit_count + 1, 3)
    else:
        bit_writer.write_bits(bit_count + 5, 4)
    
    if code > 0:
        bit_writer.write_bits(code, bit_count)
    else:
        bit_writer.write_bits(code + top_of_range, bit_count)

def write_dct_data(bit_writer, output_data: np.ndarray):
    """Encode and write DCT data to compressed file"""
    for i in range(N * N):
        row, col = ZigZag[i]
        result = output_data[row][col] / Quantum[row][col]
        output_code(bit_writer, ROUND(result))

def write_pixel_strip(output_file, strip: np.ndarray):
    """Write a strip of pixel data to output file"""
    for row in range(N):
        for col in range(COLS):
            output_file.write(bytes([strip[row][col]]))

def forward_dct(input_pixels: np.ndarray) -> np.ndarray:
    """Perform forward DCT transformation"""
    temp = np.zeros((N, N))
    output = np.zeros((N, N), dtype=int)
    
    # First matrix multiplication: temp = input * Ct
    for i in range(N):
        for j in range(N):
            temp[i][j] = 0.0
            for k in range(N):
                temp[i][j] += (input_pixels[i][k] - 128) * Ct[k][j]
    
    # Second matrix multiplication: output = C * temp
    for i in range(N):
        for j in range(N):
            temp1 = 0.0
            for k in range(N):
                temp1 += C[i][k] * temp[k][j]
            output[i][j] = ROUND(temp1)
    
    return output

def inverse_dct(input_data: np.ndarray) -> np.ndarray:
    """Perform inverse DCT transformation"""
    temp = np.zeros((N, N))
    output = np.zeros((N, N), dtype=np.uint8)
    
    # First matrix multiplication: temp = input * C
    for i in range(N):
        for j in range(N):
            temp[i][j] = 0.0
            for k in range(N):
                temp[i][j] += input_data[i][k] * C[k][j]
    
    # Second matrix multiplication: output = Ct * temp
    for i in range(N):
        for j in range(N):
            temp1 = 0.0
            for k in range(N):
                temp1 += Ct[i][k] * temp[k][j]
            temp1 += 128.0
            
            if temp1 < 0:
                output[i][j] = 0
            elif temp1 > 255:
                output[i][j] = 255
            else:
                output[i][j] = ROUND(temp1)
    
    return output

def compress_file(self, input_stream: FileIO, output: 'Compressor.BitFile', argc: int, argv: list[str], quality: int = 3):
    """Main compression function"""
    if quality < 0 or quality > 50:
        raise ValueError(f"Illegal quality factor of {quality}")
    
    print(f"Using quality factor of {quality}")
    initialize(quality)
    
    with open(input_filename, 'rb') as input_file, \
         BitFileWriter(output_filename) as output_writer:
        
        output_writer.write_bits(quality, 8)
        
        for row in range(0, ROWS, N):
            pixel_strip = read_pixel_strip(input_file)
            
            for col in range(0, COLS, N):
                # Extract NxN block from the strip
                input_block = np.zeros((N, N), dtype=np.uint8)
                for i in range(N):
                    input_block[i] = pixel_strip[i, col:col+N]
                
                output_array = forward_dct(input_block)
                write_dct_data(output_writer, output_array)
        
        output_code(output_writer, 1)

def expand_file(self, input_stream: 'Compressor.BitFile', output_stream: FileIO, argc: int, argv: list[str]):
    """Main expansion function"""
    with BitFileReader(input_filename) as input_reader, \
         open(output_filename, 'wb') as output_file:
        
        quality = input_reader.read_bits(8)
        print(f"Using quality factor of {quality}")
        initialize(quality)
        
        for row in range(0, ROWS, N):
            pixel_strip = np.zeros((N, COLS), dtype=np.uint8)
            
            for col in range(0, COLS, N):
                input_array = read_dct_data(input_reader)
                output_block = inverse_dct(input_array)
                
                # Place the NxN block back into the strip
                for i in range(N):
                    pixel_strip[i, col:col+N] = output_block[i]
            
            write_pixel_strip(output_file, pixel_strip)