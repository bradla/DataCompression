# Bradford Arrington 2025

from bitio import CompressorBitio # as CompressorBitio

INDEX_BIT_COUNT = 12
LENGTH_BIT_COUNT = 4
WINDOW_SIZE = (1 << INDEX_BIT_COUNT)
RAW_LOOK_AHEAD_SIZE = (1 << LENGTH_BIT_COUNT)
BREAK_EVEN = ((1 + INDEX_BIT_COUNT + LENGTH_BIT_COUNT) // 9)
LOOK_AHEAD_SIZE = (RAW_LOOK_AHEAD_SIZE + BREAK_EVEN)
TREE_ROOT = WINDOW_SIZE
END_OF_STREAM = 0
UNUSED = 0

def MOD_WINDOW(a):
    """Perform arithmetic on tree indices"""
    return a & (WINDOW_SIZE - 1)

CompressionName = "LZSS Encoder"
Usage = "in-file out-file\n\n"
window = [0] * WINDOW_SIZE

class TreeNode:
    """Represents a node in the binary tree"""
    __slots__ = ['parent', 'smaller_child', 'larger_child']
    
    def __init__(self):
        self.parent = UNUSED
        self.smaller_child = UNUSED
        self.larger_child = UNUSED

# Initialize tree with WINDOW_SIZE + 1 nodes
tree = [TreeNode() for _ in range(WINDOW_SIZE + 1)]


def InitTree(r):
    tree[TREE_ROOT].larger_child = r
    tree[r].parent = TREE_ROOT
    tree[r].larger_child = UNUSED
    tree[r].smaller_child = UNUSED


def ContractNode(old_node, new_node):

    tree[new_node].parent = tree[old_node].parent
    parent = tree[old_node].parent
    if tree[parent].larger_child == old_node:
        tree[parent].larger_child = new_node
    else:
        tree[parent].smaller_child = new_node
    tree[old_node].parent = UNUSED


def ReplaceNode(old_node, new_node):

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


def FindNextNode(node):

    next_node = tree[node].smaller_child
    while tree[next_node].larger_child != UNUSED:
        next_node = tree[next_node].larger_child
    return next_node


def DeleteString(p):

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


def AddString(new_node, match_position):

    if new_node == END_OF_STREAM:
        return 0
    
    test_node = tree[TREE_ROOT].larger_child
    match_length = 0
    
    while True:
        # Compare strings in the window
        i = 0
        while i < LOOK_AHEAD_SIZE:
            delta = (window[MOD_WINDOW(new_node + i)] - 
                    window[MOD_WINDOW(test_node + i)])
            if delta != 0:
                break
            i += 1
        
        if i >= match_length:
            match_length = i
            match_position[0] = test_node  # Update the reference
            if match_length >= LOOK_AHEAD_SIZE:
                ReplaceNode(test_node, new_node)
                return match_length
        
        # Determine which child to follow
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

def compress_file(self, input_bit_file: FileIO, output: 'Compressor.BitFile', argc: int, argv: list[str]):
    i: int
    c: int 
    look_ahead_bytes: int
    current_position: int
    replace_count: int
    match_length: int
    match_position: int

    current_position = 1;
    for i in range(LOOK_AHEAD_SIZE):
        c = input_bit_file.input_bit()
        if c == self.END_OF_STREAM:
	        break
        #if ( ( c = getc( input ) ) == EOF )
        #    break
        window[ current_position + i ] = c

    look_ahead_bytes = i
    InitTree( current_position )
    match_length = 0;
    match_position = 0;
    while look_ahead_bytes > 0:
        if match_length > look_ahead_bytes:
            match_length = look_ahead_bytes
        if match_length <= BREAK_EVEN:
            replace_count = 1
            output.output_bits( output, 1 )
            output.output_bits( output, window[ current_position ], 8 )
        else:
            output.output_bit( output, 0 )
            output.output_bits( output,match_position, INDEX_BIT_COUNT )
            output.output_bits( output, match_length - ( BREAK_EVEN + 1 ), LENGTH_BIT_COUNT )
            replace_count = match_length

        for i in range(replace_count):
            DeleteString( MOD_WINDOW( current_position + LOOK_AHEAD_SIZE ) )
            c = input_bit_file.input_bit()
            if c == self.END_OF_STREAM:
                #if ( ( c = getc( input ) ) == EOF )
                look_ahead_bytes = look_ahead_bytes - 1
            else:
                window[ MOD_WINDOW( current_position + LOOK_AHEAD_SIZE ) ] = c

            current_position = MOD_WINDOW( current_position + 1 )
            if look_ahead_bytes:
                match_length = AddString( current_position, match_position )

    output.output_bit( output, 0 )
    output.output_bits( output, END_OF_STREAM, INDEX_BIT_COUNT )

    while argc > 0:
            argc -= 1
            print(f"Unknown argument: {argv[len(argv) - argc - 1]}")


def expand_file(self, input_bit_file: 'Compressor.BitFile', output_stream: FileIO, argc: int, argv: list[str]):
    i: int
    current_position: int
    c: int
    match_length: int
    match_position: int

    current_position = 1;
    while True:
        #if ( InputBit( input ) ):
            c = InputBits( input, 8 )
            #putc( c, output )
            window[ current_position ] = c
            current_position = MOD_WINDOW( current_position + 1 );
        else {
            match_position = (int) InputBits( input, INDEX_BIT_COUNT );
            if ( match_position == END_OF_STREAM )
                break;
            match_length = (int) InputBits( input, LENGTH_BIT_COUNT );
            match_length += BREAK_EVEN;
            for ( i = 0 ; i <= match_length ; i++ ) {
                c = window[ MOD_WINDOW( match_position + i ) ];
                putc( c, output );
                window[ current_position ] = c
                current_position = MOD_WINDOW( current_position + 1 )

    while argc > 0:
        argc -= 1
        print(f"Unknown argument: {argv[len(argv) - argc - 1]}")