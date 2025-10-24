import os
import sys
import subprocess
import time
from datetime import datetime
from pathlib import Path

class ChurnProgram:
    def __init__(self):
        self.total_files = 0
        self.total_passed = 0
        self.total_failed = 0
        self.compress_command = ""
        self.expand_command = ""
        self.log_file = None

    def main(self, args):
        if len(args) != 3:
            self.usage_exit()
            return

        root_dir = args[0]
        self.compress_command = args[1]
        self.expand_command = args[2]

        # Ensure path ends with separator
        root_dir = os.path.normpath(root_dir) + os.sep

        try:
            self.log_file = open("CHURN.LOG", "w", encoding="utf-8")
            self.write_log_header()

            start_time = datetime.now()
            self.churn_files(root_dir)
            stop_time = datetime.now()

            self.write_log_summary(start_time, stop_time)
            self.log_file.close()
            
        except Exception as e:
            print(f"Error: {e}", file=sys.stderr)
            if self.log_file:
                self.log_file.close()

    def churn_files(self, path):
        try:
            # Process all files in current directory
            for entry in os.scandir(path):
                if entry.is_dir(follow_symlinks=False):
                    self.churn_files(entry.path)
                elif entry.is_file(follow_symlinks=False):
                    if not self.file_is_already_compressed(entry.path):
                        print(f"Testing {entry.path}", file=sys.stderr)
                        if not self.compress(entry.path):
                            print("Comparison failed!", file=sys.stderr)
        except PermissionError as ex:
            print(f"Access denied to {path}: {ex}", file=sys.stderr)
        except Exception as ex:
            print(f"Error processing {path}: {ex}", file=sys.stderr)

    def file_is_already_compressed(self, name):
        compressed_extensions = {".zip", ".ice", ".lzh", ".arc", ".gif", ".pak", ".arj"}
        extension = Path(name).suffix.lower()
        return extension in compressed_extensions

    def compress(self, file_name):
        try:
            print(file_name)
            self.log_file.write(f"{file_name:<40} ")

            # Execute compress command
            compress_cmd = self.compress_command.replace("%s", f'"{file_name}"')
            self.execute_command(compress_cmd)

            # Execute expand command  
            expand_cmd = self.expand_command.replace("%s", f'"{file_name}"')
            self.execute_command(expand_cmd)

            # Compare files
            self.total_files += 1

            old_size = os.path.getsize(file_name)
            
            if os.path.exists("TEST.CMP"):
                new_size = os.path.getsize("TEST.CMP")
            else:
                new_size = 0

            self.log_file.write(f" {old_size:8} {new_size:8} ")
            
            if old_size == 0:
                old_size = 1

            ratio = 100 - (new_size * 100 // old_size)
            self.log_file.write(f"{ratio:4}%  ")

            if not self.files_are_equal(file_name, "TEST.OUT"):
                self.log_file.write("Failed\n")
                self.total_failed += 1
                return False

            self.log_file.write("Passed\n")
            self.total_passed += 1
            return True

        except Exception as ex:
            self.total_failed += 1
            self.log_file.write(f"Failed: {ex}\n")
            return False

    def files_are_equal(self, file1, file2):
        """Compare two files byte by byte"""
        if not os.path.exists(file1) or not os.path.exists(file2):
            return False

        if os.path.getsize(file1) != os.path.getsize(file2):
            return False

        try:
            with open(file1, "rb") as f1, open(file2, "rb") as f2:
                while True:
                    byte1 = f1.read(4096)
                    byte2 = f2.read(4096)
                    
                    if byte1 != byte2:
                        return False
                    
                    if not byte1:  # End of both files
                        break
                        
            return True
        except Exception:
            return False

    def execute_command(self, command):
        """Execute a shell command"""
        try:
            # On Windows, use shell=True to handle commands properly
            result = subprocess.run(
                command,
                shell=True,
                capture_output=True,
                text=True,
                check=False  # Don't raise exception on non-zero exit
            )
            
            # You can log stdout/stderr if needed
            if result.returncode != 0:
                print(f"Command failed with return code {result.returncode}: {command}", 
                      file=sys.stderr)
                
        except Exception as ex:
            print(f"Error executing command '{command}': {ex}", file=sys.stderr)

    def write_log_header(self):
        self.log_file.write("                                          Original   Packed\n")
        self.log_file.write("            File Name                     Size      Size   Ratio  Result\n")
        self.log_file.write("-------------------------------------     --------  --------  ----  ------\n")

    def write_log_summary(self, start_time, stop_time):
        elapsed_time = (stop_time - start_time).total_seconds()
        self.log_file.write(f"\nTotal elapsed time: {elapsed_time:.2f} seconds\n")
        self.log_file.write(f"Total files:   {self.total_files}\n")
        self.log_file.write(f"Total passed:  {self.total_passed}\n")
        self.log_file.write(f"Total failed:  {self.total_failed}\n")

    def usage_exit(self):
        usage = """
CHURN 1.0. Usage: CHURN root-dir "compress command" "expand command"

CHURN tests compression programs by compressing and expanding all files in a directory.

Example:
  CHURN C:\\ "LZSS-C %s TEST.CMP" "LZSS-C TEST.CMP TEST.OUT"
"""
        print(usage)
        sys.exit(1)


if __name__ == "__main__":
    churn = ChurnProgram()
    churn.main(sys.argv[1:])