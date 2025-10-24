using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

partial class Compressor
{
    public string CompressionName = "LZSS Encoder";
    public static string Usage = "in-file out-file\n\n";

    public const int INDEX_BIT_COUNT = 12;
    public const int LENGTH_BIT_COUNT = 4;
    public const int RAW_LOOK_AHEAD_SIZE = (1 << LENGTH_BIT_COUNT);
    public const int BREAK_EVEN = ((1 + INDEX_BIT_COUNT + LENGTH_BIT_COUNT) / 9);
    public const int LOOK_AHEAD_SIZE = (RAW_LOOK_AHEAD_SIZE + BREAK_EVEN);
    public const int END_OF_STREAM = 0;

    public const int WINDOW_SIZE = (1 << INDEX_BIT_COUNT);
    public const int TREE_ROOT = WINDOW_SIZE;
    public const int UNUSED = 0;

    public class TreeNode
    {
        public int parent;
        public int smaller_child;
        public int larger_child;

        public TreeNode()
        {
            parent = 0;
            smaller_child = 0;
            larger_child = 0;
        }
    }

    private byte[] window;
    private TreeNode[] tree;

    private int MOD_WINDOW(int a)
    {
        return a & (WINDOW_SIZE - 1);
    }

    private void InitTree(int r)
    {
        tree[TREE_ROOT].larger_child = r;
        tree[r].parent = TREE_ROOT;
        tree[r].larger_child = UNUSED;
        tree[r].smaller_child = UNUSED;
    }

    private void ContractNode(int old_node, int new_node)
    {
        tree[new_node].parent = tree[old_node].parent;
        int parent = tree[old_node].parent;
        if (tree[parent].larger_child == old_node)
        {
            tree[parent].larger_child = new_node;
        }
        else
        {
            tree[parent].smaller_child = new_node;
        }
        tree[old_node].parent = UNUSED;
    }

    private void ReplaceNode(int old_node, int new_node)
    {
        int parent = tree[old_node].parent;
        if (tree[parent].smaller_child == old_node)
        {
            tree[parent].smaller_child = new_node;
        }
        else
        {
            tree[parent].larger_child = new_node;
        }

        tree[new_node].parent = tree[old_node].parent;
        tree[new_node].smaller_child = tree[old_node].smaller_child;
        tree[new_node].larger_child = tree[old_node].larger_child;

        if (tree[new_node].smaller_child != UNUSED)
        {
            tree[tree[new_node].smaller_child].parent = new_node;
        }
        if (tree[new_node].larger_child != UNUSED)
        {
            tree[tree[new_node].larger_child].parent = new_node;
        }

        tree[old_node].parent = UNUSED;
    }

    private int FindNextNode(int node)
    {
        int next_node = tree[node].smaller_child;
        while (tree[next_node].larger_child != UNUSED)
        {
            next_node = tree[next_node].larger_child;
        }
        return next_node;
    }

    private void DeleteString(int p)
    {
        if (tree[p].parent == UNUSED)
            return;

        if (tree[p].larger_child == UNUSED)
        {
            ContractNode(p, tree[p].smaller_child);
        }
        else if (tree[p].smaller_child == UNUSED)
        {
            ContractNode(p, tree[p].larger_child);
        }
        else
        {
            int replacement = FindNextNode(p);
            DeleteString(replacement);
            ReplaceNode(p, replacement);
        }
    }

    private int AddString(int new_node, ref int match_position)
    {
        if (new_node == END_OF_STREAM)
            return 0;

        int test_node = tree[TREE_ROOT].larger_child;
        int match_length = 0;
        int delta = 0;

        while (true)
        {
            int i = 0;
            while (i < LOOK_AHEAD_SIZE)
            {
                byte new_node_val = window[MOD_WINDOW(new_node + i)];
                byte test_node_val = window[MOD_WINDOW(test_node + i)];
                delta = new_node_val - test_node_val;
                if (delta != 0)
                    break;
                i++;
            }

            if (i >= match_length)
            {
                match_length = i;
                match_position = test_node;
                if (match_length >= LOOK_AHEAD_SIZE)
                {
                    ReplaceNode(test_node, new_node);
                    return match_length;
                }
            }

            int child;
            if (delta >= 0)
            {
                child = tree[test_node].larger_child;
            }
            else
            {
                child = tree[test_node].smaller_child;
            }

            if (child == UNUSED)
            {
                if (delta >= 0)
                {
                    tree[test_node].larger_child = new_node;
                }
                else
                {
                    tree[test_node].smaller_child = new_node;
                }

                tree[new_node].parent = test_node;
                tree[new_node].larger_child = UNUSED;
                tree[new_node].smaller_child = UNUSED;
                return match_length;
            }

            test_node = child;
        }
    }

    public void CompressFile(Stream inputStream, BitFile output, int argc, string[] argv)
    {
        int i = 0;
        int c = 0;
        int look_ahead_bytes = 0;
        int current_position = 1;
        int replace_count;
        int match_length = 0;
        int match_position = 0;

        window = new byte[WINDOW_SIZE];
        tree = new TreeNode[WINDOW_SIZE + 1];
        for (int z = 0; z < tree.Length; z++)
        {
            tree[z] = new TreeNode();
        }

        for (i = 0; i < LOOK_AHEAD_SIZE; i++)
        {
            c = inputStream.ReadByte();
            if (c == -1)
                break;

            window[current_position + i] = (byte)c;
            look_ahead_bytes = i + 1;
        }

        InitTree(current_position);

        while (look_ahead_bytes > 0)
        {
            if (match_length > look_ahead_bytes)
                match_length = look_ahead_bytes;

            if (match_length <= BREAK_EVEN)
            {
                replace_count = 1;
                output.OutputBit(1);
                byte literal_value = window[current_position];
                output.OutputBits(window[current_position], 8);
            }
            else
            {
                output.OutputBit(0);
                output.OutputBits((uint)match_position, INDEX_BIT_COUNT);
                //int length_value = match_length - (BREAK_EVEN + 1);
                output.OutputBits((uint)match_length - (BREAK_EVEN + 1), LENGTH_BIT_COUNT);
                replace_count = match_length;
            }

            for (i = 0; i < replace_count; i++)
            {
                /*int delete_pos = MOD_WINDOW(current_position + LOOK_AHEAD_SIZE);
                if (delete_pos < tree.Length)
                {
                    DeleteString(delete_pos);
                }*/
                DeleteString(MOD_WINDOW(current_position + LOOK_AHEAD_SIZE));
                c = inputStream.ReadByte();
                if (c == -1)
                    look_ahead_bytes--;
                else
                    window[MOD_WINDOW(current_position + LOOK_AHEAD_SIZE)] = (byte)c;
                //int new_pos = MOD_WINDOW(current_position + LOOK_AHEAD_SIZE);

                current_position = MOD_WINDOW(current_position + 1);
                if (look_ahead_bytes > 0)
                {
                    match_length = AddString(current_position, ref match_position);
                }
            }
        }

        output.OutputBit(0);
        output.OutputBits((uint)END_OF_STREAM, INDEX_BIT_COUNT);

        while (argc-- > 0)
            Console.WriteLine($"Unknown argument: {argv[argv.Length - argc - 1]}");
    }

    public void ExpandFile(BitFile input, Stream output, int argc, string[] argv)
    {
        int i;
        int c;
        int match_length;
        int match_position;
        int current_position = 1;

        window = new byte[WINDOW_SIZE];
        tree = new TreeNode[WINDOW_SIZE + 1];
        for (int z = 0; z < tree.Length; z++)
        {
            tree[z] = new TreeNode();
        }

        while (true)
        {
            if (input.InputBit() == 1)
            {
                c = (int)input.InputBits(8);
                output.WriteByte((byte)c);
				//Console.Write("1: " + c + " ");
                window[current_position] = (byte)c;
                current_position = MOD_WINDOW(current_position + 1);
            }
            else
            {
                match_position = (int)input.InputBits(INDEX_BIT_COUNT);
                if (match_position == END_OF_STREAM)
                    break;

                match_length = (int)input.InputBits(LENGTH_BIT_COUNT);
                match_length += BREAK_EVEN;

                for (i = 0; i <= match_length; i++)
                {
                    c = window[MOD_WINDOW(match_position + i)];
                    output.WriteByte((byte)c);
					//Console.Write("2:" + c + " m:" + match_length + " ");
                    window[current_position] = (byte)c;
                    current_position = MOD_WINDOW(current_position + 1);
                }
            }
        }
		output.Close();

        while (argc-- > 0)
            Console.WriteLine($"Unknown argument: {argv[argv.Length - argc - 1]}");
    }
}
