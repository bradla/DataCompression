# Bradford Arrington 2025

from bitio import CompressorBitio # as CompressorBitio
from io import FileIO, SEEK_SET, SEEK_CUR
from typing import BinaryIO, List
import sys


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

#def __init__(self):
window = [0] * WINDOW_SIZE
tree = [TreeNode() for _ in range(WINDOW_SIZE + 1)]

def debug_window_content(self, position, length=10):
	"""Debug method to check window content types"""
	print(f"Window content around position {position}:")
	for i in range(max(0, position-5), min(len(self.window), position+5)):
		val = self.window[i]
		print(f"  window[{i}] = {val} (type: {type(val)})")

def MOD_WINDOW(a):
	"""Perform arithmetic on tree indices"""
	return a & (WINDOW_SIZE - 1)

def InitTree(r):
	tree[TREE_ROOT].larger_child = r
	tree[r].parent = TREE_ROOT
	tree[r].larger_child = UNUSED
	tree[r].smaller_child = UNUSED


def ContractNode( old_node, new_node):

	tree[new_node].parent = tree[old_node].parent
	parent = tree[old_node].parent
	if tree[parent].larger_child == old_node:
		tree[parent].larger_child = new_node
	else:
		tree[parent].smaller_child = new_node
	tree[old_node].parent = UNUSED


def ReplaceNode( old_node, new_node):

	parent = tree[old_node].parent
	if tree[parent].smaller_child == old_node:
		tree[parent].smaller_child = new_node
	else:
		tree[parent].larger_child = new_node
	
		# Copy all attributes from old_node to new_node
	tree[new_node].parent = tree[old_node].parent
	tree[new_node].smaller_child = tree[old_node].smaller_child
	tree[new_node].larger_child = tree[old_node].larger_child
	
		# Update parent references of children
	if tree[new_node].smaller_child != UNUSED:
		tree[tree[new_node].smaller_child].parent = new_node
	if tree[new_node].larger_child != UNUSED:
		tree[tree[new_node].larger_child].parent = new_node
	
	tree[old_node].parent = UNUSED


def FindNextNode( node):
	next_node = tree[node].smaller_child
	while tree[next_node].larger_child != UNUSED:
		next_node = tree[next_node].larger_child
	return next_node


def DeleteString( p):
	if tree[p].parent == UNUSED:
		return
	
	if tree[p].larger_child == UNUSED:
		ContractNode(p, tree[p].smaller_child)
	elif tree[p].smaller_child == UNUSED:
		ContractNode(p, tree[p].larger_child)
	else:
		replacement = FindNextNode(p)
		DeleteString(replacement)
		ReplaceNode(p, replacement)


def AddString( new_node, match_position):

	if new_node == END_OF_STREAM:
		return 0
	
	test_node = tree[TREE_ROOT].larger_child
	match_length = 0
	
	while True:
			# Compare strings in the window
		i = 0
		while i < LOOK_AHEAD_SIZE:
				#delta = (self.window[self.MOD_WINDOW(new_node + i)] - self.window[self.MOD_WINDOW(test_node + i)])
			new_node_val = window[MOD_WINDOW(new_node + i)]
			test_node_val = window[MOD_WINDOW(test_node + i)]
			delta = new_node_val - test_node_val
			if delta != 0:
				break
			i += 1
		
		if i >= match_length:
			match_length = i
			match_position[0] = test_node  # Update the reference
			if match_length >= LOOK_AHEAD_SIZE:
				ReplaceNode(test_node, new_node)
				return match_length

		if delta >= 0:
			child = tree[test_node].larger_child
		else:
			child = tree[test_node].smaller_child
		
		if child == UNUSED:
				# Add new node to the tree
			if delta >= 0:
				tree[test_node].larger_child = new_node
			else:
				tree[test_node].smaller_child = new_node
			
			tree[new_node].parent = test_node
			tree[new_node].larger_child = UNUSED
			tree[new_node].smaller_child = UNUSED
			return match_length
		
		test_node = child

def compress_file(input_stream: FileIO, output: 'Compressor.BitFile', argc: int, argv: list[str]):
	i: int
	c: int 
	look_ahead_bytes: int = 0
	current_position: int = 1
	replace_count: int
	match_length: int
	match_position: int

	current_position = 1
	for i in range(LOOK_AHEAD_SIZE):
		c = input_stream.read(1)
		if not c:
			break

		byte_value = c[0] if isinstance(c, bytes) else ord(c)
		window[ current_position + i ] = byte_value
		look_ahead_bytes = i + 1

		#look_ahead_bytes = i
	InitTree( current_position )
	match_length = 0
	match_position = [0] # Use list for pass-by-reference

	while look_ahead_bytes > 0:
		if match_length > look_ahead_bytes:
			match_length = look_ahead_bytes

		if match_length <= BREAK_EVEN:
			replace_count = 1
			output.output_bit( 1 )  # Flag for literal
			literal_value = window[current_position]
			output.output_bits( literal_value, 8 )
		else:
			output.output_bit( 0 )
			output.output_bits( match_position[0], INDEX_BIT_COUNT )
			length_value = match_length - (BREAK_EVEN + 1) # need ??
			output.output_bits( length_value, LENGTH_BIT_COUNT )
			replace_count = match_length

		for i in range(replace_count):
				# Delete the string that will be overwritten
			delete_pos = MOD_WINDOW(current_position + LOOK_AHEAD_SIZE)
			if delete_pos < len(tree):  # Safety check
				DeleteString(delete_pos)

			c = input_stream.read(1)
			if not c:
				look_ahead_bytes -= 1
			else:
				new_pos = MOD_WINDOW(current_position + LOOK_AHEAD_SIZE)
				byte_value = c[0] if isinstance(c, bytes) else ord(c)
				window[new_pos] = byte_value

			current_position = MOD_WINDOW( current_position + 1 )
			if look_ahead_bytes > 0:
				match_length = AddString( current_position, match_position )

	output.output_bit( 0 )
	output.output_bits( END_OF_STREAM, INDEX_BIT_COUNT )

	while argc > 0:
			argc -= 1
			print(f"Unknown argument: {argv[len(argv) - argc - 1]}")


def expand_file( input_stream: 'Compressor.BitFile', output_stream: FileIO, argc: int, argv: list[str]):
	i: int = 0
	current_position: int
	c: int
	match_length: int
	match_position: int
	current_position = 1

	while True:
		if input_stream.input_bit():

			c = input_stream.input_bits(  8 )
			output_stream.write(bytes([c]))
			window[ current_position ] = ord(chr(c))

			current_position = MOD_WINDOW( current_position + 1 )
		else:
			match_position = input_stream.input_bits( INDEX_BIT_COUNT )
			if match_position == END_OF_STREAM:
				break
			match_length = input_stream.input_bits( LENGTH_BIT_COUNT )
			match_length += BREAK_EVEN
			i = 0
			for i in range(match_length+1):
				c = window[ MOD_WINDOW( match_position + i ) ]
				output_stream.write(bytes([c]))
				window[ current_position ] = ord(chr(c))
				current_position = MOD_WINDOW( current_position + 1 )

	while argc > 0:
		argc -= 1
		print(f"Unknown argument: {argv[len(argv) - argc - 1]}")