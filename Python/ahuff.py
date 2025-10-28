import sys
import struct
from typing import BinaryIO, Optional

class Node:
    def __init__(self):
        self.weight = 0
        self.parent = -1
        self.child_is_leaf = False
        self.child = 0

class Tree:
    def __init__(self):
        self.leaf = [-1] * SYMBOL_COUNT
        self.next_free_node = 0
        self.nodes = [Node() for _ in range(NODE_TABLE_COUNT)]

# Constants
END_OF_STREAM = 256
ESCAPE = 257
SYMBOL_COUNT = 258
NODE_TABLE_COUNT = (SYMBOL_COUNT * 2) - 1
ROOT_NODE = 0
MAX_WEIGHT = 0x8000

tree = Tree()

def compress_file(input_file: BinaryIO, output_file: BinaryIO, args: list):
    bit_output = BitFile(output_file)
    
    initialize_tree(tree)
    
    while True:
        byte = input_file.read(1)
        if not byte:
            break
        c = byte[0]
        encode_symbol(tree, c, bit_output)
        update_model(tree, c)
    
    encode_symbol(tree, END_OF_STREAM, bit_output)
    bit_output.flush()
    
    for arg in args:
        if arg == "-d":
            print_tree(tree)
        else:
            print(f"Unused argument: {arg}")

def expand_file(input_file: BinaryIO, output_file: BinaryIO, args: list):
    bit_input = BitFile(input_file, for_reading=True)
    
    initialize_tree(tree)
    
    while True:
        c = decode_symbol(tree, bit_input)
        if c == END_OF_STREAM:
            break
        output_file.write(bytes([c]))
        update_model(tree, c)
    
    for arg in args:
        if arg == "-d":
            print_tree(tree)
        else:
            print(f"Unused argument: {arg}")

def initialize_tree(tree: Tree):
    tree.nodes[ROOT_NODE].child = ROOT_NODE + 1
    tree.nodes[ROOT_NODE].child_is_leaf = False
    tree.nodes[ROOT_NODE].weight = 2
    tree.nodes[ROOT_NODE].parent = -1

    tree.nodes[ROOT_NODE + 1].child = END_OF_STREAM
    tree.nodes[ROOT_NODE + 1].child_is_leaf = True
    tree.nodes[ROOT_NODE + 1].weight = 1
    tree.nodes[ROOT_NODE + 1].parent = ROOT_NODE
    tree.leaf[END_OF_STREAM] = ROOT_NODE + 1

    tree.nodes[ROOT_NODE + 2].child = ESCAPE
    tree.nodes[ROOT_NODE + 2].child_is_leaf = True
    tree.nodes[ROOT_NODE + 2].weight = 1
    tree.nodes[ROOT_NODE + 2].parent = ROOT_NODE
    tree.leaf[ESCAPE] = ROOT_NODE + 2

    tree.next_free_node = ROOT_NODE + 3

    for i in range(END_OF_STREAM):
        tree.leaf[i] = -1

def encode_symbol(tree: Tree, c: int, output: BitFile):
    code = 0
    current_bit = 1
    code_size = 0
    current_node = tree.leaf[c]
    
    if current_node == -1:
        current_node = tree.leaf[ESCAPE]
    
    while current_node != ROOT_NODE:
        if (current_node & 1) == 0:
            code |= current_bit
        current_bit <<= 1
        code_size += 1
        current_node = tree.nodes[current_node].parent
    
    output.output_bits(code, code_size)
    
    if tree.leaf[c] == -1:
        output.output_bits(c, 8)
        add_new_node(tree, c)

def decode_symbol(tree: Tree, input_bitfile: BitFile) -> int:
    current_node = ROOT_NODE
    
    while not tree.nodes[current_node].child_is_leaf:
        current_node = tree.nodes[current_node].child
        if input_bitfile.input_bit():
            current_node += 1
    
    c = tree.nodes[current_node].child
    if c == ESCAPE:
        c = input_bitfile.input_bits(8)
        add_new_node(tree, c)
    
    return c

def update_model(tree: Tree, c: int):
    if tree.nodes[ROOT_NODE].weight == MAX_WEIGHT:
        rebuild_tree(tree)
    
    current_node = tree.leaf[c]
    while current_node != -1:
        tree.nodes[current_node].weight += 1
        new_node = current_node
        while new_node > ROOT_NODE:
            if tree.nodes[new_node - 1].weight >= tree.nodes[current_node].weight:
                break
            new_node -= 1
        
        if current_node != new_node:
            swap_nodes(tree, current_node, new_node)
            current_node = new_node
        
        current_node = tree.nodes[current_node].parent

def rebuild_tree(tree: Tree):
    print("R", end="", flush=True)
    j = tree.next_free_node - 1
    
    # Collect leaves and scale weights
    for i in range(j, ROOT_NODE - 1, -1):
        if tree.nodes[i].child_is_leaf:
            tree.nodes[j] = tree.nodes[i]
            tree.nodes[j].weight = (tree.nodes[j].weight + 1) // 2
            j -= 1
    
    # Rebuild internal nodes
    i = tree.next_free_node - 2
    while j >= ROOT_NODE:
        k = i + 1
        tree.nodes[j].weight = tree.nodes[i].weight + tree.nodes[k].weight
        weight = tree.nodes[j].weight
        tree.nodes[j].child_is_leaf = False
        
        k = j + 1
        while k < len(tree.nodes) and weight < tree.nodes[k].weight:
            k += 1
        k -= 1
        
        # Shift nodes
        for m in range(j, k):
            tree.nodes[m] = tree.nodes[m + 1]
        
        tree.nodes[k].weight = weight
        tree.nodes[k].child = i
        tree.nodes[k].child_is_leaf = False
        
        i -= 2
        j -= 1
    
    # Rebuild parent and leaf pointers
    for i in range(tree.next_free_node - 1, ROOT_NODE - 1, -1):
        if tree.nodes[i].child_is_leaf:
            k = tree.nodes[i].child
            tree.leaf[k] = i
        else:
            k = tree.nodes[i].child
            tree.nodes[k].parent = i
            tree.nodes[k + 1].parent = i

def swap_nodes(tree: Tree, i: int, j: int):
    # Update leaf pointers
    if tree.nodes[i].child_is_leaf:
        tree.leaf[tree.nodes[i].child] = j
    else:
        tree.nodes[tree.nodes[i].child].parent = j
        tree.nodes[tree.nodes[i].child + 1].parent = j
    
    if tree.nodes[j].child_is_leaf:
        tree.leaf[tree.nodes[j].child] = i
    else:
        tree.nodes[tree.nodes[j].child].parent = i
        tree.nodes[tree.nodes[j].child + 1].parent = i
    
    # Swap nodes
    temp = Node()
    temp.weight = tree.nodes[i].weight
    temp.parent = tree.nodes[i].parent
    temp.child_is_leaf = tree.nodes[i].child_is_leaf
    temp.child = tree.nodes[i].child
    
    tree.nodes[i].weight = tree.nodes[j].weight
    tree.nodes[i].parent = tree.nodes[j].parent
    tree.nodes[i].child_is_leaf = tree.nodes[j].child_is_leaf
    tree.nodes[i].child = tree.nodes[j].child
    
    tree.nodes[j].weight = temp.weight
    tree.nodes[j].parent = temp.parent
    tree.nodes[j].child_is_leaf = temp.child_is_leaf
    tree.nodes[j].child = temp.child

def add_new_node(tree: Tree, c: int):
    lightest_node = tree.next_free_node - 1
    new_node = tree.next_free_node
    zero_weight_node = tree.next_free_node + 1
    tree.next_free_node += 2

    # Copy the lightest node to new position
    tree.nodes[new_node].weight = tree.nodes[lightest_node].weight
    tree.nodes[new_node].parent = lightest_node
    tree.nodes[new_node].child_is_leaf = tree.nodes[lightest_node].child_is_leaf
    tree.nodes[new_node].child = tree.nodes[lightest_node].child
    
    # Update leaf pointer for the moved node
    tree.leaf[tree.nodes[new_node].child] = new_node

    # Convert lightest node to internal node
    tree.nodes[lightest_node].child = new_node
    tree.nodes[lightest_node].child_is_leaf = False

    # Add new symbol node
    tree.nodes[zero_weight_node].child = c
    tree.nodes[zero_weight_node].child_is_leaf = True
    tree.nodes[zero_weight_node].weight = 0
    tree.nodes[zero_weight_node].parent = lightest_node
    tree.leaf[c] = zero_weight_node

def print_tree(tree: Tree):
    print("\nHuffman Tree:")
    print_codes(tree)

def print_codes(tree: Tree):
    print()
    for i in range(SYMBOL_COUNT):
        if tree.leaf[i] != -1:
            if 32 <= i <= 126:  # Printable ASCII
                print(f"'{chr(i)}': ", end="")
            else:
                print(f"<{i:3}>: ", end="")
            
            print(f"{tree.nodes[tree.leaf[i]].weight:5} ", end="")
            print_code(tree, i)
            print()

def print_code(tree: Tree, c: int):
    code = 0
    current_bit = 1
    code_size = 0
    current_node = tree.leaf[c]

    while current_node != ROOT_NODE:
        if current_node & 1:
            code |= current_bit
        current_bit <<= 1
        code_size += 1
        current_node = tree.nodes[current_node].parent

    for i in range(code_size):
        current_bit >>= 1
        print('1' if (code & current_bit) else '0', end='')
