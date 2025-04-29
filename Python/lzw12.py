# Bradford Arrington 2025
import sys
from io import FileIO, SEEK_SET, SEEK_CUR
from dataclasses import dataclass


@dataclass
class DictionaryEntry:
    def __init__(self):
        self.code_value: int = 0
        self.parent_code: int = 0
        self.character: str = ''

class Compressor:
    COMPRESSION_NAME = "LZW 12 Bit Encoder"
    USAGE = "in-file out-file\n\n"
    BITS = 12
    MAX_CODE = (1 << BITS) - 1
    TABLE_SIZE = 5021
    END_OF_STREAM = 256
    FIRST_CODE = 257
    UNUSED = -1

    dict_entries = [DictionaryEntry() for _ in range(TABLE_SIZE)]

    decode_stack = [''] * TABLE_SIZE

    @staticmethod
    def find_child_node(parent_code: int, child_character: int) -> int:
        index: int = (child_character << (Compressor.BITS - 8)) ^ parent_code
        offset: int = 1 if index == 0 else Compressor.TABLE_SIZE - index

        while True:
            if Compressor.dict_entries[index].code_value == Compressor.UNUSED:
                return index

            if (Compressor.dict_entries[index].parent_code == parent_code and
                    Compressor.dict_entries[index].character == chr(child_character)):
                return index

            index -= offset
            if index < 0:
                index += Compressor.TABLE_SIZE

    @staticmethod
    def decode_string(count: int, code: int) -> int:
        while code > 255:
            Compressor.decode_stack[count] = Compressor.dict_entries[code].character
            count += 1
            code = Compressor.dict_entries[code].parent_code

        Compressor.decode_stack[count] = chr(code)
        count += 1
        return count

    def compress_file(self, input_stream: FileIO, output: 'Compressor.BitFile', argc: int, argv: list[str]):
        next_code: int = Compressor.FIRST_CODE
        string_code: int = -1
        index: int = 0

        # Initialize dictionary
        for i in range(Compressor.TABLE_SIZE):
            Compressor.dict_entries[i].code_value = Compressor.UNUSED

        # Read the first character
        first_byte = input_stream.read(1)
        if not first_byte:  # EOF
            string_code = Compressor.END_OF_STREAM
        else:
            string_code = ord(first_byte)

        while True:
            char_byte = input_stream.read(1)
            if not char_byte:  # EOF
                break
            character: int = ord(char_byte)

            index = Compressor.find_child_node(string_code, character)

            if Compressor.dict_entries[index].code_value != Compressor.UNUSED:
                string_code = Compressor.dict_entries[index].code_value
            else:
                if next_code <= Compressor.MAX_CODE:
                    Compressor.dict_entries[index].code_value = next_code
                    Compressor.dict_entries[index].parent_code = string_code
                    Compressor.dict_entries[index].character = chr(character)
                    next_code += 1

                output.output_bits(string_code, Compressor.BITS)
                string_code = character

        # Write the last string and end-of-stream marker
        output.output_bits(string_code, Compressor.BITS)
        output.output_bits(Compressor.END_OF_STREAM, Compressor.BITS)

        while argc > 0:
            argc -= 1
            print(f"Unknown argument: {argv[len(argv) - argc - 1]}")

    def expand_file(self, input_bit_file: 'Compressor.BitFile', output_stream: FileIO, argc: int, argv: list[str]):
        next_code: int = Compressor.FIRST_CODE
        new_code: int = 0
        old_code: int = 0
        character: int = 0
        count: int = 0

        # Read the first code
        try:
            old_code = input_bit_file.input_bits(Compressor.BITS)
        except EOFError:
            return

        if old_code == Compressor.END_OF_STREAM:
            return

        character = old_code
        output_stream.write(bytes([old_code]))

        # Process input
        while True:
            try:
                new_code = input_bit_file.input_bits(Compressor.BITS)
            except EOFError:
                break

            if new_code == Compressor.END_OF_STREAM:
                break

            if new_code >= next_code:
                Compressor.decode_stack[0] = chr(character)
                count = Compressor.decode_string(1, old_code)
            else:
                count = Compressor.decode_string(0, new_code)

            character = ord(Compressor.decode_stack[count - 1])

            # Write decoded string to output
            while count > 0:
                count -= 1
                output_stream.write(bytes([ord(Compressor.decode_stack[count])]))

            if next_code <= Compressor.MAX_CODE:
                Compressor.dict_entries[next_code].parent_code = old_code
                Compressor.dict_entries[next_code].character = chr(character)
                next_code += 1

            old_code = new_code

        while argc > 0:
            argc -= 1
            print(f"Unknown argument: {argv[len(argv) - argc - 1]}")

#if __name__ == '__main__':
#    if len(sys.argv) != 3:
#        print(f"Usage: python {sys.argv[0]} <in-file> <out-file>")
#        sys.exit(1)

#    in_file_name = sys.argv[1]
#    out_file_name = sys.argv[2]

#    try:
#        with open(in_file_name, 'rb') as infile, Compressor.BitFile.open_output_bit_file(out_file_name) as outfile:
#            compressor = Compressor()
#            compressor.compress_file(infile, outfile, 0, [])  # argc and argv not really used here for direct conversion
#        print(f"Compression successful. Output written to {out_file_name}")

#        compressed_file_name = out_file_name + ".compressed"
#        with open(out_file_name, 'rb') as compressed_infile, open(compressed_file_name, 'wb') as decompressed_outfile:
#            compressed_bitfile = Compressor.BitFile(out_file_name, True)
#            decompressor = Compressor()
#            decompressor.expand_file(compressed_bitfile, decompressed_outfile, 0, [])
#            compressed_bitfile.close_bit_file()
#        print(f"Decompression successful. Output written to {compressed_file_name}")

#    except FileNotFoundError:
#        print(f"Error: Input file '{in_file_name}' not found.")
#        sys.exit(1)
#    except Exception as e:
#        print(f"An error occurred: {e}")
#        sys.exit(1)
