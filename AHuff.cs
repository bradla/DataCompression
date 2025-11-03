using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

partial class Compressor
{
    public string CompressionName = "Adaptive Huffman coding, with escape codes";
    public static string Usage = "infile outfile [ -d ]";

    public const int END_OF_STREAM = 256;
    public const int ESCAPE = 257;
    public const int SYMBOL_COUNT = 258;
    public const int NODE_TABLE_COUNT = ((SYMBOL_COUNT * 2) - 1);
    public const int ROOT_NODE = 0;
    public const int MAX_WEIGHT = 0x8000;
    public const bool TRUE = true;
    public const bool FALSE = false;

    public struct Node
    {
        public uint weight;
        public int parent;
        public bool child_is_leaf;
        public int child;
    }

    public class TreeAHuff
    {
        public int[] leaf = new int[SYMBOL_COUNT];
        public int next_free_node;
        public Node[] nodes = new Node[NODE_TABLE_COUNT];
    }

    public static TreeAHuff Tree = new TreeAHuff();

    public void CompressFile(Stream input, BitFile output, int argc, string[] argv)
    {
        int c;

        InitializeTree(Tree);
        while ((c = input.ReadByte()) != -1)
        {
            EncodeSymbol(Tree, (uint)c, output);
            UpdateModel(Tree, c);
        }
        EncodeSymbol(Tree, END_OF_STREAM, output);
        
        foreach (string arg in argv)
        {
            if (arg == "-d")
                PrintTree(Tree);
            else
                Console.WriteLine("Unused argument: {0}", arg);
        }
        while (argc-- > 0)
            Console.WriteLine($"Unknown argument: {argv[argv.Length - argc - 1]}");
    }

    public void ExpandFile(BitFile input, Stream output, int argc, string[] argv)
    {
        int c;

        InitializeTree(Tree);
        while ((c = DecodeSymbol(Tree, input)) != END_OF_STREAM)
        {
            output.WriteByte((byte)c);
            UpdateModel(Tree, c);
        }
        
        foreach (string arg in argv)
        {
            if (arg == "-d")
                PrintTree(Tree);
            else
                Console.WriteLine("Unused argument: {0}", arg);
        }
        while (argc-- > 0)
            Console.WriteLine($"Unknown argument: {argv[argv.Length - argc - 1]}");
    }

    public static void InitializeTree(TreeAHuff tree)
    {
        int i;

        tree.nodes[ROOT_NODE].child = ROOT_NODE + 1;
        tree.nodes[ROOT_NODE].child_is_leaf = false;
        tree.nodes[ROOT_NODE].weight = 2;
        tree.nodes[ROOT_NODE].parent = -1;

        tree.nodes[ROOT_NODE + 1].child = END_OF_STREAM;
        tree.nodes[ROOT_NODE + 1].child_is_leaf = true;
        tree.nodes[ROOT_NODE + 1].weight = 1;
        tree.nodes[ROOT_NODE + 1].parent = ROOT_NODE;
        tree.leaf[END_OF_STREAM] = ROOT_NODE + 1;

        tree.nodes[ROOT_NODE + 2].child = ESCAPE;
        tree.nodes[ROOT_NODE + 2].child_is_leaf = true;
        tree.nodes[ROOT_NODE + 2].weight = 1;
        tree.nodes[ROOT_NODE + 2].parent = ROOT_NODE;
        tree.leaf[ESCAPE] = ROOT_NODE + 2;

        tree.next_free_node = ROOT_NODE + 3;

        for (i = 0; i < END_OF_STREAM; i++)
            tree.leaf[i] = -1;
    }

    public static void EncodeSymbol(TreeAHuff tree, uint c, BitFile output)
    {
        uint code = 0;
        uint current_bit = 1;
        int code_size = 0;
        int current_node = tree.leaf[(int)c];
        
        if (current_node == -1)
            current_node = tree.leaf[ESCAPE];
            
        while (current_node != ROOT_NODE)
        {
            if ((current_node & 1) == 0)
                code |= current_bit;
            current_bit <<= 1;
            code_size++;
            current_node = tree.nodes[current_node].parent;
        }
        
        output.OutputBits(code, code_size);
        
        if (tree.leaf[(int)c] == -1)
        {
            output.OutputBits(c, 8);
            add_new_node(tree, (int)c);
        }
    }

    public static int DecodeSymbol(TreeAHuff tree, BitFile input)
    {
        int current_node = ROOT_NODE;
        int c;

        while (!tree.nodes[current_node].child_is_leaf)
        {
            current_node = tree.nodes[current_node].child;
            current_node += input.InputBit(); // ? 1 : 0;
        }
        
        c = tree.nodes[current_node].child;
        if (c == ESCAPE)
        {
            c = (int)input.InputBits(8);
            add_new_node(tree, c);
        }
        
        return c;
    }

    public static void UpdateModel(TreeAHuff tree, int c)
    {
        int current_node;
        int new_node;

        if (tree.nodes[ROOT_NODE].weight == MAX_WEIGHT)
            RebuildTree(tree);
            
        current_node = tree.leaf[c];
        while (current_node != -1)
        {
            tree.nodes[current_node].weight++;
            for (new_node = current_node; new_node > ROOT_NODE; new_node--)
            {
                if (tree.nodes[new_node - 1].weight >= tree.nodes[current_node].weight)
                    break;
            }
            
            if (current_node != new_node)
            {
                swap_nodes(tree, current_node, new_node);
                current_node = new_node;
            }
            current_node = tree.nodes[current_node].parent;
        }
    }

    public static void RebuildTree(TreeAHuff tree)
    {
        int i, j, k;
        uint weight;

        Console.Write("R");
        j = tree.next_free_node - 1;
        for (i = j; i >= ROOT_NODE; i--)
        {
            if (tree.nodes[i].child_is_leaf)
            {
                tree.nodes[j] = tree.nodes[i];
                tree.nodes[j].weight = (tree.nodes[j].weight + 1) / 2;
                j--;
            }
        }

        for (i = tree.next_free_node - 2; j >= ROOT_NODE; i -= 2, j--)
        {
            k = i + 1;
            tree.nodes[j].weight = tree.nodes[i].weight + tree.nodes[k].weight;
            weight = tree.nodes[j].weight;
            tree.nodes[j].child_is_leaf = false;
            
            for (k = j + 1; weight < tree.nodes[k].weight; k++) ;
            k--;
            
            Array.Copy(tree.nodes, j + 1, tree.nodes, j, k - j);
            tree.nodes[k].weight = weight;
            tree.nodes[k].child = i;
            tree.nodes[k].child_is_leaf = false;
        }

        for (i = tree.next_free_node - 1; i >= ROOT_NODE; i--)
        {
            if (tree.nodes[i].child_is_leaf)
            {
                k = tree.nodes[i].child;
                tree.leaf[k] = i;
            }
            else
            {
                k = tree.nodes[i].child;
                tree.nodes[k].parent = i;
                tree.nodes[k + 1].parent = i;
            }
        }
    }

    public static void swap_nodes(TreeAHuff tree, int i, int j)
    {
        Node temp = tree.nodes[i];

        if (tree.nodes[i].child_is_leaf)
            tree.leaf[tree.nodes[i].child] = j;
        else
        {
            tree.nodes[tree.nodes[i].child].parent = j;
            tree.nodes[tree.nodes[i].child + 1].parent = j;
        }
        
        if (tree.nodes[j].child_is_leaf)
            tree.leaf[tree.nodes[j].child] = i;
        else
        {
            tree.nodes[tree.nodes[j].child].parent = i;
            tree.nodes[tree.nodes[j].child + 1].parent = i;
        }
        
        tree.nodes[i] = tree.nodes[j];
        tree.nodes[i].parent = temp.parent;
        temp.parent = tree.nodes[j].parent;
        tree.nodes[j] = temp;
    }

    public static void add_new_node(TreeAHuff tree, int c)
    {
        int lightest_node = tree.next_free_node - 1;
        int new_node = tree.next_free_node;
        int zero_weight_node = tree.next_free_node + 1;
        tree.next_free_node += 2;

        tree.nodes[new_node] = tree.nodes[lightest_node];
        tree.nodes[new_node].parent = lightest_node;
        tree.leaf[tree.nodes[new_node].child] = new_node;

        tree.nodes[lightest_node].child = new_node;
        tree.nodes[lightest_node].child_is_leaf = false;

        tree.nodes[zero_weight_node].child = c;
        tree.nodes[zero_weight_node].child_is_leaf = true;
        tree.nodes[zero_weight_node].weight = 0;
        tree.nodes[zero_weight_node].parent = lightest_node;
        tree.leaf[c] = zero_weight_node;
    }


    public static void PrintTree(TreeAHuff tree)
    {
        Console.WriteLine("\nHuffman Tree:");
        print_codes(tree);
        // Additional tree printing logic would go here
    }

    public static void print_codes(TreeAHuff tree)
    {
        Console.WriteLine();
        for (int i = 0; i < SYMBOL_COUNT; i++)
        {
            if (tree.leaf[i] != -1)
            {
                if (char.IsLetterOrDigit((char)i) || char.IsPunctuation((char)i) || char.IsSymbol((char)i))
                    Console.Write("'{0}': ", (char)i);
                else
                    Console.Write("<{0,3}>: ", i);
                    
                Console.Write("{0,5} ", tree.nodes[tree.leaf[i]].weight);
                print_code(tree, i);
                Console.WriteLine();
            }
        }
    }

    public static void print_code(TreeAHuff tree, int c)
    {
        uint code = 0;
        uint current_bit = 1;
        int code_size = 0;
        int current_node = tree.leaf[c];

        while (current_node != ROOT_NODE)
        {
            if ((current_node & 1) != 0)
                code |= current_bit;
            current_bit <<= 1;
            code_size++;
            current_node = tree.nodes[current_node].parent;
        }

        for (int i = 0; i < code_size; i++)
        {
            current_bit >>= 1;
            Console.Write((code & current_bit) != 0 ? '1' : '0');
        }
    }
}