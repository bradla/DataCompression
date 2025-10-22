import math
import struct
import sys

COMPRESSION_NAME = "Sound sample companding"
USAGE = "infile outfile [n]\n\n n optionally sets the bits per sample\n\n"

def get_file_length(file):
    """Get the length of the file"""
    current_pos = file.tell()
    file.seek(0, 2)  # SEEK_END
    length = file.tell()
    file.seek(current_pos, 0)  # SEEK_SET
    return length - current_pos

def compress_file(self, input_stream: FileIO, output: 'Compressor.BitFile', argc: int, argv: list[str]):
    compress = [0] * 256
           
    print(f"Compressing using {bits} bits per sample...")
    steps = 1 << (bits - 1)
    
    bit_output = BitFile(output_file, 'w')
    bit_output.output_bits(bits, 8)
    bit_output.output_bits(get_file_length(input_file), 32)
    
    for i in range(steps, 0, -1):
        value = int(128.0 * (math.pow(2.0, i / steps) - 1.0) + 0.5)
        for j in range(value, 0, -1):
            compress[j + 127] = i + steps - 1
            compress[128 - j] = steps - i
    
    while True:
        byte_data = input_file.read(1)
        if not byte_data:
            break
        c = byte_data[0]
        bit_output.output_bits(compress[c], bits)
    
    bit_output.flush()

def expand_file(self, input_stream: 'Compressor.BitFile', output_stream: FileIO, argc: int, argv: list[str]):
    expand = [0] * 256

    bit_input = BitFile(input_file, 'r')
    bits = bit_input.input_bits(8)
    print(f"Expanding using {bits} bits per sample...")
    
    steps = 1 << (bits - 1)
    last_value = 0
    
    for i in range(1, steps + 1):
        value = int(128.0 * (math.pow(2.0, i / steps) - 1.0) + 0.5)
        expand[steps + i - 1] = 128 + (value + last_value) // 2
        expand[steps - i] = 127 - (value + last_value) // 2
        last_value = value
    
    count = bit_input.input_bits(32)
    for _ in range(count):
        c = bit_input.input_bits(bits)
        output_file.write(bytes([expand[c]]))
    
    while argc > 0:
        print(f"Unused argument: {argv[0]}")
        argc -= 1
        argv = argv[1:]