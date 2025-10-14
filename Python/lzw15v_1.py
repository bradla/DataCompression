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
      
COMPRESSION_NAME = "LZW 15 Bit Variable Rate Encoder"
USAGE = "in-file out-file xx\n\n"
BITS  =                     15
MAX_CODE =                  ( ( 1 << BITS ) - 1 )
TABLE_SIZE =                35023
TABLE_BANKS =               ( ( TABLE_SIZE >> 8 ) + 1 )
END_OF_STREAM =              256
BUMP_CODE  =                257
FLUSH_CODE =                258
FIRST_CODE =                259
UNUSED  =                   -1

#dict_entries = [DictionaryEntry() for _ in range(TABLE_SIZE)]
decode_stack = [''] * TABLE_SIZE
    
# Create a list of lists to simulate the C array of pointers
dict_banks = [[None] * 256 for _ in range(TABLE_BANKS)]


	# Define a DICT(i) accessor equivalent
def DICT(i):
    	"""Mimic the macro DICT(i) -> dict[i >> 8][i & 0xff]"""
    	bank_index = i >> 8
    	entry_index = i & 0xFF
    	return dict_banks[bank_index][entry_index]
    
def __init__(self):
        self.dictionary: List[DictionaryEntry] = []
        self.decode_stack: bytearray = bytearray(TABLE_SIZE)
        self.next_code: int = 0
        self.current_code_bits: int = 0
        self.next_bump_code: int = 0
        
        #self._initialize_storage()

def initialize_storage(self):
        """Allocates the dictionary structure (Python list equivalent)."""
        self.dictionary = [DictionaryEntry() for _ in range(TABLE_SIZE)]

def initialize_dictionary(self):
        """
        Initializes the dictionary, called at startup or on a FLUSH_CODE.
        """
        for i in range(TABLE_SIZE):
            self.dictionary[i].code_value = UNUSED

        self.next_code = FIRST_CODE
        # print('F', end='', file=sys.stdout) # C puts 'F' to stdout
        self.current_code_bits = 9
        self.next_bump_code = 511 # 2**9 - 1

def find_child_node(self, parent_code: int, child_character: int) -> int:
        """
        Hashing routine to find the table location for a string/character combination.
        """
        index = (child_character << (BITS - 8)) ^ parent_code
        index %= TABLE_SIZE

        if index == 0:
            offset = 1
        else:
            offset = TABLE_SIZE - index

        while True:
            entry = self.dictionary[index]
            
            if entry.code_value == UNUSED:
                return index
            
            if entry.parent_code == parent_code and entry.character == child_character:
                return index
            
            # Collision resolution: move to the next slot
            if index >= offset:
                index -= offset
            else:
                index += TABLE_SIZE - offset

def decode_string(self, count: int, code: int) -> int:
        """
        Decodes a string from the dictionary and stores it in decode_stack.
        """
        current_count = count
        while code > 255:
            if code >= TABLE_SIZE or self.dictionary[code].code_value == UNUSED:
                raise ValueError(f"Invalid code in dictionary lookup: {code}")

            self.decode_stack[current_count] = self.dictionary[code].character # type: ignore
            current_count += 1
            code = self.dictionary[code].parent_code

        self.decode_stack[current_count] = code
        current_count += 1
        
        return current_count

@staticmethod
def find_child_node(parent_code: int, child_character: int) -> int:
        index: int = (child_character << (BITS - 8)) ^ parent_code
        offset: int = 1 #if index == 0 else TABLE_SIZE - index
        if index == 0:
            offset = 1
        else:
            offset = TABLE_SIZE - index;

        while True:
            if DICT[index].code_value == UNUSED:
                return index

            if (DICT[index].parent_code == parent_code and
                    DICT[index].character == chr(child_character)):
                return index

            index -= offset
            if index < 0:
                index += TABLE_SIZE - offset

@staticmethod
def decode_string2(count: int, code: int) -> int:
        while code > 255:
            decode_stack[count] = DICT[code].character
            count += 1
            code = DICT[code].parent_code

        decode_stack[count] = chr(code)
        count += 1
        return count

def compress_file(self, input_stream: FileIO, output: 'CompressorBitio.BitFile', argc: int, argv: list[str]):
        character: int
        string_code: int = -1
        index: int = 0

        # Initialize dictionary
        initialize_storage()
        initialize_dictionary()

        # Read the first character
        first_byte = input_stream.read(1)
        if not first_byte:  # EOF
            string_code = END_OF_STREAM

        while True:
            char_byte = input_stream.read(1)
            if not char_byte:  # EOF
                break
            character: int = ord(char_byte)

            index = find_child_node(string_code, character)

            if DICT[index].code_value != -1:
                string_code = DICT[index].code_value
            else:
                DICT[index].code_value = next_code
                DICT[index].parent_code = string_code
                DICT[index].character = chr(character)
                outut.output_bits( string_code, current_code_bits )
                string_code = character
                if next_code > MAX_CODE:
                    output.output_bits( FLUSH_CODE, current_code_bits )
                    current_code_bits = current_code_bits + 1
                    next_bump_code <<= 1
                    next_bump_code |= 1
                    print( 'B', stdout )

        # Write the last string and end-of-stream marker
        output.output_bits(string_code, current_code_bits)
        output.output_bits(END_OF_STREAM, current_code_bits)

        while argc > 0:
            argc -= 1
            print(f"Unknown argument: {argv[len(argv) - argc - 1]}")

def expand_file(self, input_bit_file: 'CompressorBitio.BitFile', output_stream: FileIO, argc: int, argv: list[str]):
        new_code: int = 0
        old_code: int = 0
        character: int = 0
        count: int = 0
        
        initialize_storage()
        while True:
            initizlize_dictionary()
            try:
                old_code = input_bit_file.input_bits(BITS)
            except EOFError:
                return
	
            if old_code == END_OF_STREAM:
	            return
	
            character = old_code
            output_stream.write(bytes([old_code]))
            while True:
                try:
                    new_code = input_bit_file.input_bits(BITS)
                except EOFError:
                    break
                if new_code == END_OF_STREAM:
                    break
                if new_code == FLUSH_CODE:
	                break
                if new_code == BUMP_CODE:
                    current_code_bits = + 1
                    print('B')
                	#putc( 'B', stdout );
                    continue
                if new_code >= next_code:
                    decode_stack[0] = chr(character)
                    count = decode_string(1, old_code)
                else:
                    count = decode_string(0, new_code)
	
                character = ord(decode_stack[count - 1])
                while count > 0:
                    count -= 1
                    output_stream.write(bytes([ord(decode_stack[count])]))
                DICT[next_code].parent_code = old_code
                DICT[next_code].character = chr(character)
                next_code += 1
                old_code = new_code

        while argc > 0:
            argc -= 1
            print(f"Unknown argument: {argv[len(argv) - argc - 1]}")
            

# Bradford Arrington 2025
import sys
import os
from bitio import CompressorBitio # as CompressorBitio
import time
import tracemalloc
import psutil
import os

bitio = CompressorBitio()
_printed_header = False

#class Program:
if __name__ == '__main__':
    @staticmethod
    def file_size(file_name: str) -> int:
        try:
            file_info = os.stat(file_name)
            return file_info.st_size
        except FileNotFoundError:
            return 0

    @staticmethod
    def print_ratios(input_file_path: str, output_file_path: str):
        input_size = file_size(input_file_path)
        if input_size == 0:
            input_size = 1

        output_size = file_size(output_file_path)
        ratio = 100 - int((output_size * 100) / input_size)

        print(f"\nInput bytes:             {input_size}")
        print(f"Output bytes:            {output_size}")
        print(f"Compression ratio:       {ratio}%")

    def track_performance(name, func, *args, **kwargs):
        global _printed_header

        process = psutil.Process(os.getpid())
        start_time = time.time()
        start_cpu = process.cpu_times().user
        tracemalloc.start()
        start_mem = tracemalloc.get_traced_memory()[0]

        result = func(*args, **kwargs)

        end_mem = tracemalloc.get_traced_memory()[1]
        tracemalloc.stop()
        end_cpu = process.cpu_times().user
        end_time = time.time()

        wall_time_ms = (end_time - start_time) * 1000
        cpu_time_ms = (end_cpu - start_cpu) * 1000
        mem_used_kb = (end_mem - start_mem) / 1024

        if not _printed_header:
          print(f"{'Function':<20} {'Wall Time (ms)':>15} {'CPU Time (ms)':>15} {'Memory Used (KB)':>20}")
          _printed_header = True

        print(f"{name:<20} {wall_time_ms:15.2f} {cpu_time_ms:15.2f} {mem_used_kb:20.2f}")

        return result

    arguments = sys.argv
    if len(sys.argv) < 3:
         prog_name = arguments[0]
         print(arguments[0])
         short_name = prog_name
         last_slash = prog_name.rfind('\\')
         if last_slash == -1:
              last_slash = prog_name.rfind('/')
         if last_slash == -1:
              last_slash = prog_name.rfind(':')
         if last_slash != -1:
              short_name = prog_name[last_slash + 1:]
         extension = short_name.rfind('.')
         if extension != -1:
              short_name = short_name[:extension]
         print(f"\nUsage:  {short_name} {USAGE}")
         sys.exit(0)

    remaining_args = arguments[3:]
    try:
          output = track_performance("OpenBitFile", bitio.BitFile.open_output_bit_file,arguments[2])
          with open(arguments[1], 'rb') as input_file:
            track_performance("CompressFile", compress_file, input_file, output, len(remaining_args), remaining_args)
          track_performance("CloseBitFile", output.close_bit_file)
          print(f"\nCompressing {arguments[1]} to {arguments[2]}")
          print(f"Using {COMPRESSION_NAME}\n")
          print_ratios(arguments[1], arguments[2])
    except FileNotFoundError:
          print(f"Error: Input file '{arguments[1]}' not found.")
          sys.exit(1)
    except Exception as e:
          print(f"An error occurred: {e} {arguments}")
          sys.exit(1)
