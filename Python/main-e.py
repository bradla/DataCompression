# Bradford Arrington 2025
from lzw12 import Compressor as Compressor
from bitio import Compressor as CompressorBitio
import time
import tracemalloc
import psutil
import sys
import os
from typing import List

_printed_header = False

bitio = CompressorBitio()
compdecomp = Compressor()

if __name__ == '__main__':
    @staticmethod           
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
        
    if len(sys.argv) < 1:
        prog_name = arguments[0]
        short_name = prog_name
        last_slash = max(prog_name.rfind('\\'), prog_name.rfind('/'), prog_name.rfind(':'))
            
        if last_slash != -1:
            short_name = prog_name[last_slash + 1:]
            
        extension = short_name.rfind('.')
        if extension != -1:
            short_name = short_name[:extension]
            
        print(f"\nUsage:  {short_name} {Compressor.Usage}")
        sys.exit(0)

    remaining_args = arguments[3:]
    try:
        input_file = bitio.BitFile.open_input_bit_file(arguments[1])
        output_file = open(arguments[2], 'wb')
            
        print(f"\nDecompressing {arguments[1]} to {arguments[2]}")
        print(f"Using {compdecomp.COMPRESSION_NAME}\n")
            
        compdecomp.expand_file(input_file, output_file, len(remaining_args), remaining_args)
    except FileNotFoundError:
        print(f"Error: Input file '{arguments[1]}' not found.")
        sys.exit(1)
    except Exception as e:
        print(f"An error occurred: {e}")
        sys.exit(1)
