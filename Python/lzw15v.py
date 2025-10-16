# Bradford Arrington 2025
import sys
from io import FileIO, SEEK_SET, SEEK_CUR
from collections import defaultdict
from bitio import CompressorBitio # as CompressorBitio
import time
import tracemalloc
import psutil
import os
import struct
from typing import BinaryIO, List, Tuple, Optional
    
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

    def __init__(self):
        self.dict = [None] * self.TABLE_BANKS
        self.decode_stack = [0] * self.TABLE_SIZE
        self.next_code = self.FIRST_CODE
        self.current_code_bits = 9
        self.next_bump_code = 511
        self.initialize_storage()

    def initialize_storage(self):
        """Initialize the dictionary storage"""
        for i in range(self.TABLE_BANKS):
            self.dict[i] = [{'code_value': self.UNUSED, 'parent_code': 0, 'character': 0} 
                           for _ in range(256)]

    def initialize_dictionary(self):
        """Initialize/reset the dictionary"""
        for i in range(self.TABLE_SIZE):
            self.dict_lookup(i)['code_value'] = self.UNUSED
        self.next_code = self.FIRST_CODE
        print('F', end='', flush=True)
        self.current_code_bits = 9
        self.next_bump_code = 511

    def dict_lookup(self, index: int) -> dict:
        """Dictionary lookup macro equivalent"""
        bank = index >> 8
        offset = index & 0xFF
        if self.dict[bank] is None:
            raise RuntimeError(f"Dictionary bank {bank} not initialized (index={index})")
        return self.dict[bank][offset]

    def decode_string(self, count: int, code: int) -> int:
        """
        Decode a string from the dictionary and store it in decode_stack
        Returns the count of characters placed in the stack
        """
        while code > 255:
            dict_entry = self.dict_lookup(code)
            self.decode_stack[count] = dict_entry['character']
            count += 1
            code = dict_entry['parent_code']
        self.decode_stack[count] = code
        return count + 1

    def find_child_node(self, parent_code: int, child_character: int) -> int:
        """
        Find the table location for a string/character combination
        Uses XOR hashing with collision handling
        """
        index = (child_character << (self.BITS - 8)) ^ parent_code
        if index == 0:
            offset = 1
        else:
            offset = self.TABLE_SIZE - index
            
        while True:
            dict_entry = self.dict_lookup(index)
            if dict_entry['code_value'] == self.UNUSED:
                return index
            if (dict_entry['parent_code'] == parent_code and 
                dict_entry['character'] == child_character):
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
        #self.initialize_storage() # handled in __init__
        self.initialize_dictionary()

        char_byte = input_stream.read(1)
        if not char_byte:  # EOF
            string_code = self.END_OF_STREAM
        else:
            string_code = char_byte[0]

        while True:
            char_byte = input_stream.read(1)
            if not char_byte:  # EOF
                break

            character = char_byte[0]

            index = self.find_child_node( string_code, character)
            dict_entry = self.dict_lookup(index)
            
            if dict_entry['code_value'] != self.UNUSED:
                string_code = dict_entry['code_value']
            else:
                dict_entry['code_value'] = self.next_code
                dict_entry['parent_code'] = string_code
                dict_entry['character'] = character

                output.output_bits( string_code, self.current_code_bits )
                string_code = character
                self.next_code += 1

                if self.next_code > self.MAX_CODE:
                    output.output_bits( self.FLUSH_CODE, self.current_code_bits )
                    self.initialize_dictionary()
                elif self.next_code > self.next_bump_code:
                    output.output_bits( self.BUMP_CODE, self.current_code_bits )
                    self.current_code_bits += 1
                    self.next_bump_code <<= 1
                    self.next_bump_code |= 1
                    print( 'B' )

        # Write the last string and end-of-stream marker
        output.output_bits(string_code, self.current_code_bits)
        output.output_bits(self.END_OF_STREAM, self.current_code_bits)
        # flush ??

        while argc > 0:
            argc -= 1
            print(f"Unknown argument: {argv[len(argv) - argc - 1]}")
   
    def expand_file(self, input_bit_file: 'CompressorBitio.BitFile', output_stream: FileIO, argc: int, argv: list[str]):
        """Expand compressed file using LZW algorithm"""
        new_code: int = 0
        old_code: int = 0
        character: int = 0
        count: int = 0
        
        #self.initialize_storage()
        while True:
            self.initialize_dictionary()
            old_code = input_bit_file.input_bits(self.current_code_bits)
            if old_code == self.END_OF_STREAM:
	            break
                
            character = old_code
            output_stream.write(bytes([character]))
            
            while True:
                new_code = input_bit_file.input_bits(self.current_code_bits)
                if new_code == self.END_OF_STREAM:
                    return
                if new_code == self.FLUSH_CODE:
                    break
                if new_code == self.BUMP_CODE:
                    self.current_code_bits += 1
                    print('B', end='', flush=True)
                    continue
                
                if new_code >= self.next_code:
                    self.decode_stack[0] = character
                    count = self.decode_string(1, old_code)
                else:
                    count = self.decode_string(0, new_code)
                
                character = self.decode_stack[count - 1]
                
                # Output the decoded string
                for i in range(count - 1, -1, -1):
                    output_stream.write(bytes([self.decode_stack[i]]))
                
                # Add new entry to dictionary
                if self.next_code < self.TABLE_SIZE:
                    dict_entry = self.dict_lookup(self.next_code)
                    dict_entry['parent_code'] = old_code
                    dict_entry['character'] = character
                    self.next_code += 1
                
                old_code = new_code
