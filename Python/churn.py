import os
import sys
import subprocess
import time

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
        
        # Ensure trailing separator
        if not root_dir.endswith(os.sep):
            root_dir = root_dir + os.sep
        
        try:
            self.log_file = open("CHURN.LOG", "w", encoding="utf-8")
            self.write_log_header()
            
            start_time = time.time()
            self.churn_files(root_dir)
            stop_time = time.time()
            
            self.write_log_summary(start_time, stop_time)
            self.log_file.close()
            
        except Exception as e:
            print(f"Error: {e}", file=sys.stderr)
            sys.exit(1)
    
    def churn_files(self, path):
        """Recursively process all files in directory"""
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
                            
        except PermissionError as e:
            print(f"Access denied to {path}: {e}", file=sys.stderr)
        except Exception as e:
            print(f"Error processing {path}: {e}", file=sys.stderr)
    
    def file_is_already_compressed(self, filename):
        """Check if file already has a compressed extension"""
        compressed_extensions = {'.zip', '.ice', '.lzh', '.arc', '.gif', '.pak', '.arj', 
                                 '.gz', '.bz2', '.xz', '.7z', '.rar', '.tar', '.tgz'}
        extension = os.path.splitext(filename)[1].lower()
        return extension in compressed_extensions
    
    def compress(self, filename):
        """Compress and expand a file, then compare results"""
        print(filename)
        
        # Prepare temporary files with absolute paths
        current_dir = os.getcwd()
        compressed_file = os.path.join(current_dir, "TEST.CMP")
        output_file = os.path.join(current_dir, "TEST.OUT")
        
        # Clean up any existing temp files
        for temp_file in [compressed_file, output_file]:
            if os.path.exists(temp_file):
                try:
                    os.remove(temp_file)
                except:
                    pass
        
        try:
            # Log file entry
            self.log_file.write(f"{os.path.basename(filename):<40} ")
            
            # Handle different command formats
            # For compress command, replace %s with the filename
            compress_cmd = self.compress_command
            
            # Check if command uses TEST.CMP as output file
            if "TEST.CMP" in compress_cmd:
                # Replace TEST.CMP with actual path
                compress_cmd = compress_cmd.replace("TEST.CMP", compressed_file)
                # Replace %s with filename
                compress_cmd = compress_cmd.replace("%s", f'"{filename}"')
                
                # Execute compress command
                result = subprocess.run(
                    compress_cmd,
                    shell=True,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE,
                    timeout=30
                )
            else:
                # Assume command writes to stdout, redirect to TEST.CMP
                compress_cmd = compress_cmd.replace("%s", f'"{filename}"')
                with open(compressed_file, "wb") as outfile:
                    result = subprocess.run(
                        compress_cmd,
                        shell=True,
                        stdout=outfile,
                        stderr=subprocess.PIPE,
                        timeout=30
                    )
            
            if result.returncode != 0:
                error_msg = result.stderr.decode('utf-8', errors='ignore') if result.stderr else ""
                print(f"Compress command failed: {error_msg}", file=sys.stderr)
                self.log_file.write("Failed: Compress command failed\n")
                self.total_failed += 1
                return False
            
            # Execute expand command
            expand_cmd = self.expand_command
            
            # Check if command uses TEST.CMP as input and TEST.OUT as output
            if "TEST.CMP" in expand_cmd and "TEST.OUT" in expand_cmd:
                # Replace TEST.CMP and TEST.OUT with actual paths
                expand_cmd = expand_cmd.replace("TEST.CMP", compressed_file)
                expand_cmd = expand_cmd.replace("TEST.OUT", output_file)
                
                result = subprocess.run(
                    expand_cmd,
                    shell=True,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE,
                    timeout=30
                )
            elif "TEST.CMP" in expand_cmd:
                # Command reads from TEST.CMP and writes to stdout
                expand_cmd = expand_cmd.replace("TEST.CMP", compressed_file)
                with open(output_file, "wb") as outfile:
                    result = subprocess.run(
                        expand_cmd,
                        shell=True,
                        stdout=outfile,
                        stderr=subprocess.PIPE,
                        timeout=30
                    )
            else:
                # Unknown format
                self.log_file.write("Failed: Invalid expand command format\n")
                self.total_failed += 1
                return False
            
            if result.returncode != 0:
                error_msg = result.stderr.decode('utf-8', errors='ignore') if result.stderr else ""
                print(f"Expand command failed: {error_msg}", file=sys.stderr)
                self.log_file.write("Failed: Expand command failed\n")
                self.total_failed += 1
                return False
            
            # Check if output files were created
            if not os.path.exists(compressed_file):
                self.log_file.write("Failed: Compressed file not created\n")
                self.total_failed += 1
                return False
            
            if not os.path.exists(output_file):
                self.log_file.write("Failed: Output file not created\n")
                self.total_failed += 1
                return False
            
            # Get file sizes
            old_size = os.path.getsize(filename)
            new_size = os.path.getsize(compressed_file)
            
            self.total_files += 1
            
            # Log sizes and ratio
            self.log_file.write(f" {old_size:8} {new_size:8} ")
            if old_size == 0:
                old_size = 1
            
            ratio = 100 - (new_size * 100 // old_size)
            self.log_file.write(f"{ratio:4}%  ")
            
            # Compare files
            if not self.files_are_equal(filename, output_file):
                self.log_file.write("Failed\n")
                self.total_failed += 1
                return False
            
            self.log_file.write("Passed\n")
            self.total_passed += 1
            return True
            
        except subprocess.TimeoutExpired:
            self.log_file.write("Failed: Command timed out\n")
            self.total_failed += 1
            return False
        except Exception as e:
            self.total_failed += 1
            self.log_file.write(f"Failed: {e}\n")
            return False
            
        finally:
            # Clean up temporary files
            for temp_file in [compressed_file, output_file]:
                if os.path.exists(temp_file):
                    try:
                        os.remove(temp_file)
                    except:
                        pass
    
    def files_are_equal(self, file1, file2):
        if os.path.getsize(file1) != os.path.getsize(file2):
            return False
        
        buffer_size = 4096
        with open(file1, 'rb') as f1, open(file2, 'rb') as f2:
            while True:
                chunk1 = f1.read(buffer_size)
                chunk2 = f2.read(buffer_size)
                
                if not chunk1 and not chunk2:
                    break
                    
                if chunk1 != chunk2:
                    return False
        
        return True
    
    def write_log_header(self):
        """Write header to log file"""
        self.log_file.write("                                          Original   Packed\n")
        self.log_file.write("            File Name                     Size      Size   Ratio  Result\n")
        self.log_file.write("-------------------------------------     --------  --------  ----  ------\n")
    
    def write_log_summary(self, start_time, stop_time):
        """Write summary to log file"""
        elapsed = stop_time - start_time
        self.log_file.write(f"\nTotal elapsed time: {elapsed:.2f} seconds\n")
        self.log_file.write(f"Total files:   {self.total_files}\n")
        self.log_file.write(f"Total passed:  {self.total_passed}\n")
        self.log_file.write(f"Total failed:  {self.total_failed}\n")
    
    def usage_exit(self):
        """Display usage information and exit"""
        usage = """
CHURN 1.0. Usage: CHURN root-dir "compress command" "expand command"

CHURN tests compression programs by compressing and expanding all files in a directory.

Examples:
  Windows:
   python churn.py C:\\Dir "Lzss-c.exe %s TEST.CMP" "Lzss-e.exe -d TEST.CMP TEST.OUT"
  
  Linux:
    python churn.py /home/user "gzip -c %s TEST.CMP" "gzip -d TEST.CMP TEST.OUT"
    
    python churn.py /home/user "gzip -c %s" "gzip -d -c TEST.CMP"
    
    python churn.py /any/RootDir "./Lzss-c %s TEST.CMP" "./Lzss-e TEST.CMP TEST.OUT"

Note: Use %s as placeholder for input filename in compress command.
      TEST.CMP and TEST.OUT will be automatically created in the current directory.
"""
        print(usage)
        sys.exit(1)


if __name__ == "__main__":
    churn = ChurnProgram()
    churn.main(sys.argv[1:])
