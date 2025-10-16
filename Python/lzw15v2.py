# Bradford Arrington 2025
import sys
from io import FileIO, SEEK_SET, SEEK_CUR
from dataclasses import dataclass
from collections import defaultdict
from bitio import CompressorBitio # as CompressorBitio
import time
import tracemalloc
import psutil
import os

@dataclass
class Dictionary:
    def __init__(self):
        self.code_value: int = 0
        self.parent_code: int = 0
        self.character: str = '\0'
      
class Compressor_lzw15v:
    COMPRESSION_NAME = "LZW 15 Bit Variable Rate Encoder"
    USAGE = "in-file out-file\n\n"
    BITS  =                     15
    MAX_CODE =                  ( ( 1 << BITS ) - 1 )
    TABLE_SIZE =                35023
    TABLE_BANKS =               ( ( TABLE_SIZE >> 8 ) + 1 )
    END_OF_STREAM =              256
    BUMP_CODE  =                257
    FLUSH_CODE =                258
    FIRST_CODE =                259
    UNUSED  =                   -1

    next_code: int = 0
    current_code_bits: int = 0
    next_bump_code: int = 0
    decode_stack = bytearray(TABLE_SIZE) #[''] * TABLE_SIZE
    #dict_banks = [None] * TABLE_BANKS
    dist_list = [[None ] * 256 for _ in range(TABLE_BANKS)]

    def fatal_error(message):
        raise MemoryError(message)

    def initialize_storage(self):
        for i in range(self.TABLE_BANKS):
        # Allocate 256 Dictionary objects for each bank
            self.dist_list[i] = [Dictionary() for _ in range(256)]
            if self.dist_list[i] is None:
                self.fatal_error("Error allocating dictionary space")

	# Define a DICT(i) accessor equivalent
    def DICT(self,i):
        """Mimic the macro DICT(i) -> dict[i >> 8][i & 0xff]"""
        return self.dist_list[i >> 8][i & 0xFF]

    def initialize_dictionary(self):
        """Initializes the dictionary, called at startup or on a FLUSH_CODE."""
        for i in range(self.TABLE_SIZE):
            self.DICT(i).code_value = self.UNUSED

        self.next_code = self.FIRST_CODE
        print('F', end='', file=sys.stdout) # C puts 'F' to stdout
        self.current_code_bits = 9
        self.next_bump_code = 511 # 2**9 - 1

    @staticmethod
    def decode_string(self, count: int, code: int) -> int:
        """ Decodes a string from the dictionary and stores it in decode_stack.  """
        while code > 255:
            self.decode_stack[count] = self.DICT(code).character # type: ignore
            count += 1
            code = self.DICT(code).parent_code

        self.decode_stack[count] = code
        count += 1       
        return count

    @staticmethod
    def find_child_node(self, parent_code: int, child_character: int) -> int:
        """Hashing routine to find the table location for a string/character combination."""

        index = (child_character << (self.BITS - 8)) ^ parent_code
        offset = 1

        if index == 0:
            offset = 1
        else:
            offset = self.TABLE_SIZE - index

        while True:
            if  self.DICT( index ).code_value == self.UNUSED:
                return index
            
            if (self.DICT( index ).parent_code == parent_code and self.DICT( index ).character ==  chr(child_character)):
                return index

            if index >= offset:
                index -= offset
            else:
                index += self.TABLE_SIZE - offset

    def compress_file(self, input_stream: FileIO, output: 'CompressorBitio.BitFile', argc: int, argv: list):
        character: int = 0
        string_code: int = 0
        index: int = 0

        # Initialize dictionary
        self.initialize_storage()
        self.initialize_dictionary()

        first_byte = input_stream.read(1)
        if not first_byte:  # EOF
            string_code = self.END_OF_STREAM

        while True:
            char_byte = input_stream.read(1)
            if not char_byte:  # EOF
                break
            character = ord(char_byte)

            index = self.find_child_node(self, string_code, character)

            if self.DICT(index).code_value != -1:
                string_code = self.DICT(index).code_value
            else:
                self.DICT(index).code_value = self.next_code
                self.next_code = self.next_code + 1
                self.DICT(index).parent_code = string_code
                self.DICT(index).character = chr(character)
                output.output_bits( string_code, self.current_code_bits )
                string_code = character
                if self.next_code > self.MAX_CODE:
                    output.output_bits( self.FLUSH_CODE, self.current_code_bits )
                    self.initialize_dictionary()
                elif self.next_code > self.next_bump_code:
                    output.output_bits( self.BUMP_CODE, self.current_code_bits )
                    self.current_code_bits = self.current_code_bits + 1
                    self.next_bump_code <<= 1
                    self.next_bump_code |= 1
                    print( 'B' )

        # Write the last string and end-of-stream marker
        output.output_bits(string_code, self.current_code_bits)
        output.output_bits(self.END_OF_STREAM, self.current_code_bits)

        while argc > 0:
            argc -= 1
            print(f"Unknown argument: {argv[len(argv) - argc - 1]}")

    def expand_file(self, input_bit_file: 'CompressorBitio.BitFile', output_stream: FileIO, argc: int, argv: list[str]):
        new_code: int = 0
        old_code: int = 0
        character: int = 0
        count: int = 0
        
        self.initialize_storage()
        while True:
            self.initialize_dictionary()
            old_code = input_bit_file.input_bits(BITS)
            if old_code == END_OF_STREAM:
	            break
	
            character = old_code
            output_stream.write(bytes([old_code]))
            while True:
                new_code = input_bit_file.input_bits(BITS)
                if new_code == END_OF_STREAM:
                    break
                if new_code == FLUSH_CODE:
	                break
                if new_code == BUMP_CODE:
                    current_code_bits = + 1
                    print('B')
                    continue
                if new_code >= next_code:
                    decode_stack[0] = chr(character)
                    count = self.decode_string(1, old_code)
                else:
                    count = self.decode_string(0, new_code)
	
                character = ord(decode_stack[count - 1])
                while count > 0:
                    count -= 1
                    output_stream.write(bytes([ord(decode_stack[count])]))
                self.DICT(next_code).parent_code = old_code
                self.DICT(next_code).character = chr(character)
                next_code += 1
                old_code = new_code

        while argc > 0:
            argc -= 1
            print(f"Unknown argument: {argv[len(argv) - argc - 1]}")          
