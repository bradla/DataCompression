import sys
from collections import namedtuple

def FilePrintBinary(file, code, bits):
    """Placeholder for printing the binary code."""
    print(f"<{code:0{bits}b}>", end="")
    pass

# Using namedtuple for lightweight structures similar to C structs

NODE = namedtuple('NODE', ['count', 'saved_count', 'child_0', 'child_1'])
# In C, arrays are fixed size and allocated. In Python, we'll use lists of these.
# Since the C code accesses and modifies elements by index (e.g., nodes[i].count = ...),
# we'll use a class with mutable attributes for the array elements instead of namedtuple,
# as namedtuples are immutable.

class Node:
    """Represents a tree_node with mutable attributes."""
    def __init__(self, count=0, saved_count=0, child_0=-1, child_1=-1):
        self.count = count
        self.saved_count = saved_count
        self.child_0 = child_0
        self.child_1 = child_1

class Code:
    """Represents a code structure with mutable attributes."""
    def __init__(self, code=0, code_bits=0):
        self.code = code
        # int in C
        self.code_bits = code_bits

END_OF_STREAM = 256

CompressionName = "static order 0 model with Huffman coding"
Usage = "infile outfile [-d]\n\nSpecifying -d will dump the modeling data\n"

def CompressFile(input_file, output_bit_file, argv):
    """
    void CompressFile( input, output, argc, argv )
    FILE *input; BIT_FILE *output; int argc; char *argv[];
    """
    # Allocation: Python lists/arrays are dynamically sized/allocated
    # counts = calloc(256, sizeof(unsigned long)) -> list of 256 zeros
    counts = [0] * 256
    # nodes = calloc(514, sizeof(NODE)) -> list of 514 Node objects
    nodes = [Node() for _ in range(514)]
    # codes = calloc(257, sizeof(CODE)) -> list of 257 Code objects
    codes = [Code() for _ in range(257)]

    # 1. Count bytes
    count_bytes(input_file, counts)
    # 2. Scale counts and prepare nodes
    scale_counts(counts, nodes)
    # 3. Output counts to compressed file
    output_counts(output_bit_file, nodes)
    # 4. Build Huffman tree
    root_node = build_tree(nodes)
    # 5. Convert tree to codes
    convert_tree_to_code(nodes, codes, 0, 0, root_node)

    # 6. Optional model dump
    if len(argv) > 0 and argv[0] == "-d":
        print_model(nodes, codes)

    # 7. Compress data
    compress_data(input_file, output_bit_file, codes)

def ExpandFile(input_bit_file, output_file, argv):
    """
    void ExpandFile( input, output, argc, argv )
    BIT_FILE *input; FILE *output; int argc; char *argv[];
    """
    # nodes = calloc(514, sizeof(NODE)) -> list of 514 Node objects
    nodes = [Node() for _ in range(514)]


    input_counts(input_bit_file, nodes)
    # 2. Build Huffman tree
    root_node = build_tree(nodes)

    # 3. Optional model dump
    if len(argv) > 0 and argv[0] == "-d":
        print_model(nodes, None) # Pass None instead of 0 for codes

    # 4. Expand data
    expand_data(input_bit_file, output_file, nodes, root_node)


def output_counts(output_bit_file, nodes):
    """
    void output_counts( output, nodes )
    BIT_FILE *output; NODE *nodes;
    """
    first = 0
    # Find the first non-zero count node
    while first < 256 and nodes[first].count == 0:
        first += 1

    current_node = first
    while current_node < 256:
        last = current_node + 1
        # Find the end of the current block
        while True:
            # Skip over zero-count nodes
            while last < 256 and nodes[last].count != 0:
                last += 1
            last -= 1 # last is now the last non-zero count node
            
            # Find the next non-zero count node (next block start)
            next_start = last + 1
            while next_start < 256 and nodes[next_start].count == 0:
                next_start += 1
            
            if next_start > 255:
                # Reached the end of the 0-255 range
                break
            
            # C code logic for grouping blocks: if gap size (next_start - last) > 3, start new block
            if (next_start - last) > 3:
                break
            
            # Extend the current block to the next non-zero count node and repeat
            last = next_start
        
        next_block_start = next_start # save for the outer loop update

        # Output range (first, last)
        try:
            output_bit_file.putc(current_node)
            output_bit_file.putc(last)
        except Exception:
            fatal_error("Error writing byte counts (range)")

        # Output counts for the range
        for i in range(current_node, last + 1):
            try:
                # Assuming scaled count fits in one byte (max count is <= 255 after scaling).
                if output_bit_file.putc(nodes[i].count) != nodes[i].count:
                    fatal_error("Error writing byte counts (data)")
            except Exception:
                fatal_error("Error writing byte counts (data)")
        
        current_node = next_block_start # move to the start of the next block
    
    # Write the termination marker (first == 0)
    try:
        output_bit_file.putc(0)
    except Exception:
        fatal_error("Error writing byte counts (terminator)")


def input_counts(input_bit_file, nodes):
    """
    void input_counts( input, nodes )
    BIT_FILE *input; NODE *nodes;
    """
    for i in range(256):
        nodes[i].count = 0

    first = input_bit_file.getc()
    if first == -1: fatal_error("Error reading byte counts (first)")

    last = input_bit_file.getc()
    if last == -1: fatal_error("Error reading byte counts (last)")

    while True:
        # Read counts for the range [first, last]
        for i in range(first, last + 1):
            c = input_bit_file.getc()
            if c == -1: fatal_error("Error reading byte counts (data)")
            nodes[i].count = c # c is the count (0-255)

        # Read the next 'first' marker
        first = input_bit_file.getc()
        if first == -1: fatal_error("Error reading byte counts (next first)")

        if first == 0:
            break # Termination marker found

        # Read the next 'last' marker
        last = input_bit_file.getc()
        if last == -1: fatal_error("Error reading byte counts (next last)")

    # Set EOF count
    nodes[END_OF_STREAM].count = 1


def count_bytes(input_file, counts):
    """
    void count_bytes( input, counts )
    FILE *input; unsigned long *counts;
    """
    # Save current file position
    input_marker = input_file.tell()

    # Read bytes and count them
    input_file.seek(0) # Ensure we read from the start for counting
    while True:
        c = input_file.read(1)
        if not c:
            break # EOF
        c_val = c[0]
        counts[c_val] += 1

    # Restore file position
    input_file.seek(input_marker)


def scale_counts(counts, nodes):
    """
    void scale_counts( counts, nodes )
    unsigned long *counts; NODE *nodes;
    """
    max_count = 0
    for count in counts:
        if count > max_count:
            max_count = count

    if max_count == 0:
        counts[0] = 1
        max_count = 1

    # C code: max_count = max_count / 255; max_count = max_count + 1;
    # This ensures the scaled counts fit within a byte (0-255)
    max_count = (max_count // 255) + 1

    for i in range(256):
        scaled_count = counts[i] // max_count
        # Ensure any non-zero count is at least 1 after scaling
        if scaled_count == 0 and counts[i] != 0:
            scaled_count = 1
        nodes[i].count = scaled_count

    # Set EOF count
    nodes[END_OF_STREAM].count = 1


def build_tree(nodes):
    """
    int build_tree( nodes )
    NODE *nodes;
    """
    # 513 is an index outside the 0-513 range for initialization
    nodes[513].count = 0xFFFF # High value (65535)

    next_free = END_OF_STREAM + 1 # Start non-leaf nodes after 257 (0-256 for chars + EOF)

    while True:
        min_1 = 513
        min_2 = 513

        # Find two nodes with the minimum non-zero counts
        for i in range(next_free):
            if nodes[i].count != 0:
                if nodes[i].count < nodes[min_1].count:
                    min_2 = min_1
                    min_1 = i
                elif nodes[i].count < nodes[min_2].count:
                    min_2 = i

        if min_2 == 513:
            break # Tree is complete (only one node left, min_1)

        # Create new parent node
        nodes[next_free].count = nodes[min_1].count + nodes[min_2].count
        
        # Save counts and zero them out to prevent re-selection
        nodes[min_1].saved_count = nodes[min_1].count
        nodes[min_1].count = 0
        nodes[min_2].saved_count = nodes[min_2].count
        nodes[min_2].count = 0

        # Set children
        nodes[next_free].child_0 = min_1
        nodes[next_free].child_1 = min_2
        
        next_free += 1

    # next_free points one past the root node, so decrement to get the root's index
    next_free -= 1
    nodes[next_free].saved_count = nodes[next_free].count # Save root count
    return next_free # Return root node index


def convert_tree_to_code(nodes, codes, code_so_far, bits, node):
    """
    void convert_tree_to_code( nodes, codes, code_so_far, bits, node )
    Recursive function to generate Huffman codes.
    """
    if node <= END_OF_STREAM: # Leaf node (character or EOF)
        codes[node].code = code_so_far
        codes[node].code_bits = bits
        return

    # Go left (0 bit)
    # C code: code_so_far <<= 1; bits++;
    convert_tree_to_code(nodes, codes, code_so_far << 1, bits + 1,
                         nodes[node].child_0)

    # Go right (1 bit)
    # C code: code_so_far | 1
    convert_tree_to_code(nodes, codes, (code_so_far << 1) | 1, bits + 1,
                         nodes[node].child_1)


def print_char(c):
    """
    void print_char( c )
    int c;
    """
    if 0x20 <= c < 127:
        print(f"'{chr(c)}'", end="")
    else:
        print(f"{c:3d}", end="")

def print_model(nodes, codes):
    """
    void print_model( nodes, codes )
    NODE *nodes; CODE *codes;
    """
    for i in range(513):
        if nodes[i].saved_count != 0:
            print(f"node=", end="")
            print_char(i)
            print(f"  count={nodes[i].saved_count:3d}", end="")
            
            # Print children indices (or char/EOF if a leaf)
            print("  child_0=", end="")
            print_char(nodes[i].child_0)
            print("  child_1=", end="")
            print_char(nodes[i].child_1)

            # Print Huffman code if applicable
            if codes is not None and i <= END_OF_STREAM:
                print("  Huffman code=", end="")
                # FilePrintBinary is a placeholder function
                FilePrintBinary(sys.stdout, codes[i].code, codes[i].code_bits)
            
            print() # Newline


def compress_data(input_file, output_bit_file, codes):
    """
    void compress_data( input, output, codes )
    FILE *input; BIT_FILE *output; CODE *codes;
    """
    # Go to the start of the data segment
    input_file.seek(0)
    
    while True:
        c = input_file.read(1)
        if not c:
            break # EOF
        c_val = c[0]
        
        # Output Huffman code for the byte
        output_bit_file.OutputBits(codes[c_val].code, codes[c_val].code_bits)

    # Output EOF code
    output_bit_file.OutputBits(codes[END_OF_STREAM].code, codes[END_OF_STREAM].code_bits)


def expand_data(input_bit_file, output_file, nodes, root_node):
    """
    void expand_data( input, output, nodes, root_node )
    BIT_FILE *input; FILE *output; NODE *nodes; int root_node;
    """
    while True:
        node = root_node
        
        # Traverse the tree bit by bit
        while node > END_OF_STREAM:
            # InputBit is a placeholder function
            if input_bit_file.InputBit():
                node = nodes[node].child_1 # 1 bit (right)
            else:
                node = nodes[node].child_0 # 0 bit (left)

        # Leaf node found
        if node == END_OF_STREAM:
            break # Done

        # Write the decoded byte to output
        try:
            # C code uses putc, Python uses write
            output_file.write(bytes([node]))
        except Exception:
            fatal_error("Error trying to write expanded byte to output")

