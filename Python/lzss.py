# Bradford Arrington 2025

from bitio import CompressorBitio # as CompressorBitio
from io import FileIO, SEEK_SET, SEEK_CUR
from typing import BinaryIO, List
import sys

class LZSS:
    COMPRESSION_NAME = "LZSS Encoder"
    USAGE = "in-file out-file\n\n"
      
    INDEX_BIT_COUNT = 12
    LENGTH_BIT_COUNT = 4
    RAW_LOOK_AHEAD_SIZE = (1 << LENGTH_BIT_COUNT)
    BREAK_EVEN = ((1 + INDEX_BIT_COUNT + LENGTH_BIT_COUNT) // 9)
    LOOK_AHEAD_SIZE = (RAW_LOOK_AHEAD_SIZE + BREAK_EVEN)
    END_OF_STREAM = 0

    WINDOW_SIZE = (1 << INDEX_BIT_COUNT)
    TREE_ROOT = WINDOW_SIZE
    UNUSED = 0

    class TreeNode:
            """Represents a node in the binary tree"""
            __slots__ = ['parent', 'smaller_child', 'larger_child']
            def __init__(self):
                self.parent = 0  # Use 0 instead of UNUSED during initialization
                self.smaller_child = 0
                self.larger_child = 0

    def __init__(self):
        #window = [0] * WINDOW_SIZE
        # Initialize tree with WINDOW_SIZE + 1 nodes
        #self.tree = [TreeNode() for _ in range(WINDOW_SIZE + 1)]
                # Data structures
        self.window = [0] * self.WINDOW_SIZE
        self.tree = [self.TreeNode() for _ in range(self.WINDOW_SIZE + 1)]

    def debug_window_content(self, position, length=10):
        """Debug method to check window content types"""
        print(f"Window content around position {position}:")
        for i in range(max(0, position-5), min(len(self.window), position+5)):
            val = self.window[i]
            print(f"  window[{i}] = {val} (type: {type(val)})")

    def MOD_WINDOW(self,a):
        """Perform arithmetic on tree indices"""
        return a & (self.WINDOW_SIZE - 1)

    def InitTree(self,r):
        self.tree[self.TREE_ROOT].larger_child = r
        self.tree[r].parent = self.TREE_ROOT
        self.tree[r].larger_child = self.UNUSED
        self.tree[r].smaller_child = self.UNUSED


    def ContractNode(self, old_node, new_node):

        self.tree[new_node].parent = self.tree[old_node].parent
        parent = self.tree[old_node].parent
        if self.tree[parent].larger_child == old_node:
            self.tree[parent].larger_child = new_node
        else:
            self.tree[parent].smaller_child = new_node
        self.tree[old_node].parent = self.UNUSED


    def ReplaceNode(self, old_node, new_node):

        parent = self.tree[old_node].parent
        if self.tree[parent].smaller_child == old_node:
            self.tree[parent].smaller_child = new_node
        else:
            self.tree[parent].larger_child = new_node
    
        # Copy all attributes from old_node to new_node
        self.tree[new_node].parent = self.tree[old_node].parent
        self.tree[new_node].smaller_child = self.tree[old_node].smaller_child
        self.tree[new_node].larger_child = self.tree[old_node].larger_child
    
        # Update parent references of children
        if self.tree[new_node].smaller_child != self.UNUSED:
            self.tree[self.tree[new_node].smaller_child].parent = new_node
        if self.tree[new_node].larger_child != self.UNUSED:
            self.tree[self.tree[new_node].larger_child].parent = new_node
    
        self.tree[old_node].parent = self.UNUSED


    def FindNextNode(self, node):

        next_node = self.tree[node].smaller_child
        while self.tree[next_node].larger_child != self.UNUSED:
            next_node = self.tree[next_node].larger_child
        return next_node


    def DeleteString(self, p):

        if self.tree[p].parent == self.UNUSED:
            return
    
        if self.tree[p].larger_child == self.UNUSED:
            self.ContractNode(p, self.tree[p].smaller_child)
        elif self.tree[p].smaller_child == self.UNUSED:
            self.ContractNode(p, self.tree[p].larger_child)
        else:
            replacement = self.FindNextNode(p)
            self.DeleteString(replacement)
            self.ReplaceNode(p, replacement)


    def AddString(self, new_node, match_position):

        if new_node == self.END_OF_STREAM:
            return 0
    
        test_node = self.tree[self.TREE_ROOT].larger_child
        match_length = 0
    
        while True:
            # Compare strings in the window
            i = 0
            while i < self.LOOK_AHEAD_SIZE:
                #delta = (self.window[self.MOD_WINDOW(new_node + i)] - self.window[self.MOD_WINDOW(test_node + i)])
                new_node_val = self.window[self.MOD_WINDOW(new_node + i)]
                test_node_val = self.window[self.MOD_WINDOW(test_node + i)]
                delta = new_node_val - test_node_val
                if delta != 0:
                    break
                i += 1
        
            if i >= match_length:
                match_length = i
                match_position[0] = test_node  # Update the reference
                if match_length >= self.LOOK_AHEAD_SIZE:
                    self.ReplaceNode(test_node, new_node)
                    return match_length
        
            # Determine which child to follow
            if delta >= 0:
                child = self.tree[test_node].larger_child
            else:
                child = self.tree[test_node].smaller_child
        
            if child == self.UNUSED:
                # Add new node to the tree
                if delta >= 0:
                    self.tree[test_node].larger_child = new_node
                else:
                    self.tree[test_node].smaller_child = new_node
            
                self.tree[new_node].parent = test_node
                self.tree[new_node].larger_child = self.UNUSED
                self.tree[new_node].smaller_child = self.UNUSED
                return match_length
        
            test_node = child

    def compress_file(self, input_stream: FileIO, output: 'Compressor.BitFile', argc: int, argv: list[str]):
        i: int
        c: int 
        look_ahead_bytes: int = 0
        current_position: int = 1
        replace_count: int
        match_length: int
        match_position: int

        current_position = 1
        for i in range(self.LOOK_AHEAD_SIZE):
            c = input_stream.read(1)
            if not c: # EOF c == self.END_OF_STREAM:
	            break

            # Convert byte to integer and store in window
            byte_value = c[0] if isinstance(c, bytes) else ord(c)
            self.window[ current_position + i ] = byte_value
            look_ahead_bytes = i + 1

        #look_ahead_bytes = i
        self.InitTree( current_position )
        match_length = 0
        match_position = [0] # Use list for pass-by-reference
        #print(f"Starting compression with {look_ahead_bytes} bytes in look-ahead buffer")
        #print(f"Window size: {self.WINDOW_SIZE}, Look ahead size: {self.LOOK_AHEAD_SIZE}")

        while look_ahead_bytes > 0:
            if match_length > look_ahead_bytes:
                match_length = look_ahead_bytes

            if match_length <= self.BREAK_EVEN:
                replace_count = 1
                output.output_bit( 1 )  # Flag for literal
                literal_value = self.window[current_position]
                output.output_bits( literal_value, 8 )
                # print(f"Literal: {literal_value:02x} at pos {current_position}")
            else:
                output.output_bit( 0 )
                output.output_bits( match_position[0], self.INDEX_BIT_COUNT )
                length_value = match_length - (self.BREAK_EVEN + 1) # need ??
                output.output_bits( length_value, self.LENGTH_BIT_COUNT )
                replace_count = match_length
                # print(f"Reference: pos={match_position[0]}, len={match_length}")

            for i in range(replace_count):
                #self.DeleteString( self.MOD_WINDOW( current_position + self.LOOK_AHEAD_SIZE ) )
                # Delete the string that will be overwritten
                delete_pos = self.MOD_WINDOW(current_position + self.LOOK_AHEAD_SIZE)
                if delete_pos < len(self.tree):  # Safety check
                    self.DeleteString(delete_pos)

                c = input_stream.read(1)
                if not c: # EOF #if ( ( c = getc( input ) ) == EOF )
                    look_ahead_bytes -= 1
                else:
                    # Add new character to window as integer
                    #self.window[ self.MOD_WINDOW( current_position + self.LOOK_AHEAD_SIZE ) ] = c
                    new_pos = self.MOD_WINDOW(current_position + self.LOOK_AHEAD_SIZE)
                    byte_value = c[0] if isinstance(c, bytes) else ord(c)
                    self.window[new_pos] = byte_value

                current_position = self.MOD_WINDOW( current_position + 1 )
                if look_ahead_bytes > 0:
                    match_length = self.AddString( current_position, match_position )

        output.output_bit( 0 )
        output.output_bits( self.END_OF_STREAM, self.INDEX_BIT_COUNT )

        while argc > 0:
                argc -= 1
                print(f"Unknown argument: {argv[len(argv) - argc - 1]}")


    def expand_file(self, input_stream: 'Compressor.BitFile', output_stream: FileIO, argc: int, argv: list[str]):
        i: int = 0
        current_position: int
        c: int
        match_length: int
        match_position: int
        current_position = 1

        # Initialize window with zeros
        #self.window = [0] * self.WINDOW_SIZE
        while True:
            if input_stream.input_bit():

                c = input_stream.input_bits(  8 )
                output_stream.write(bytes([c]))
                self.window[ current_position ] = ord(chr(c))

                current_position = self.MOD_WINDOW( current_position + 1 )
            else:
                match_position = input_stream.input_bits( self.INDEX_BIT_COUNT )
                if match_position == self.END_OF_STREAM:
                    break
                match_length = input_stream.input_bits( self.LENGTH_BIT_COUNT )
                match_length += self.BREAK_EVEN
                i = 0
                for i in range(match_length+1):
                    c = self.window[ self.MOD_WINDOW( match_position + i ) ]
                    output_stream.write(bytes([c]))
                    self.window[ current_position ] = ord(chr(c))
                    current_position = self.MOD_WINDOW( current_position + 1 )

        while argc > 0:
            argc -= 1
            print(f"Unknown argument: {argv[len(argv) - argc - 1]}")