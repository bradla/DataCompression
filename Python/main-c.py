# Bradford Arrington 2025
import sys
import os
from bitio import CompressorBitio # as CompressorBitio
import time
import tracemalloc
import psutil
import os

bitio = CompressorBitio()
_printed_header = False

#class Program:
if __name__ == '__main__':
    @staticmethod
    def file_size(file_name: str) -> int:
        try:
            file_info = os.stat(file_name)
            return file_info.st_size
        except FileNotFoundError:
            return 0

    @staticmethod
    def print_ratios(input_file_path: str, output_file_path: str):
        input_size = file_size(input_file_path)
        if input_size == 0:
            input_size = 1

        output_size = file_size(output_file_path)
        ratio = 100 - int((output_size * 100) / input_size)

        print(f"\nInput bytes:             {input_size}")
        print(f"Output bytes:            {output_size}")
        print(f"Compression ratio:       {ratio}%")

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
    if len(sys.argv) < 3:
         prog_name = arguments[0]
         short_name = prog_name
         last_slash = prog_name.rfind('\\')
         if last_slash == -1:
              last_slash = prog_name.rfind('/')
         if last_slash == -1:
              last_slash = prog_name.rfind(':')
         if last_slash != -1:
              short_name = prog_name[last_slash + 1:]
         extension = short_name.rfind('.')
         if extension != -1:
              short_name = short_name[:extension]
         print(f"\nUsage:  {short_name} {USAGE}")
         sys.exit(0)

    remaining_args = arguments[3:]
    try:
          output = track_performance("OpenBitFile", bitio.BitFile.open_output_bit_file,arguments[2])
          with open(arguments[1], 'rb') as input_file:
            track_performance("CompressFile", compress_file, input_file, output, len(remaining_args), remaining_args)
          track_performance("CloseBitFile", output.close_bit_file)
          print(f"\nCompressing {arguments[1]} to {arguments[2]}")
          print(f"Using {COMPRESSION_NAME}\n")
          print_ratios(arguments[1], arguments[2])
    except FileNotFoundError:
          print(f"Error: Input file '{arguments[1]}' not found.")
          sys.exit(1)
    except Exception as e:
          print(f"An error occurred: {e}")
          sys.exit(1)
