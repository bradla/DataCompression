using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

class ChurnProgram
{
    private static int totalFiles = 0;
    private static int totalPassed = 0;
    private static int totalFailed = 0;
    private static string compressCommand;
    private static string expandCommand;
    private static StreamWriter logFile;

    static void Main(string[] args)
    {
        if (args.Length != 3)
        {
            UsageExit();
            return;
        }

        string rootDir = args[0];
        compressCommand = args[1];
        expandCommand = args[2];

        if (!rootDir.EndsWith(Path.DirectorySeparatorChar))
        {
            rootDir += Path.DirectorySeparatorChar;
        }

        logFile = new StreamWriter("CHURN.LOG");
        WriteLogHeader();

        DateTime startTime = DateTime.Now;
        ChurnFiles(rootDir);
        DateTime stopTime = DateTime.Now;

        WriteLogSummary(startTime, stopTime);
        logFile.Close();
    }

    private static void ChurnFiles(string path)
    {
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(path))
            {
                ChurnFiles(directory);
            }

            foreach (string file in Directory.EnumerateFiles(path))
            {
                if (!FileIsAlreadyCompressed(file))
                {
                    Console.Error.WriteLine($"Testing {file}");
                    if (!Compress(file))
                    {
                        Console.Error.WriteLine("Comparison failed!");
                    }
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"Access denied to {path}: {ex.Message}");
        }
    }

    private static bool FileIsAlreadyCompressed(string name)
    {
        string[] compressedExtensions = { ".zip", ".ice", ".lzh", ".arc", ".gif", ".pak", ".arj" };
        string extension = Path.GetExtension(name)?.TrimStart('.').ToLower();
        return compressedExtensions.Contains(extension);
    }

    private static bool Compress(string fileName)
    {
        string command;
        long newSize;
        long oldSize;

        Console.WriteLine(fileName);
        logFile.Write($"{fileName,-40} ");

        // Prepare compressed filename with absolute path
        string compressedFile = Path.Combine(Directory.GetCurrentDirectory(), "TEST.CMP");
        string outputFile = Path.Combine(Directory.GetCurrentDirectory(), "TEST.OUT");

        // Ensure we start clean
        if (File.Exists(compressedFile)) File.Delete(compressedFile);
        if (File.Exists(outputFile)) File.Delete(outputFile);

        try
        {
            // Execute compress command with the correct placeholder
            command = compressCommand.Replace("%s", $"\"{fileName}\"");
            ExecuteCommand(command);

            // Execute expand command
            command = expandCommand;
            ExecuteCommand(command);

            if (!File.Exists(compressedFile) || !File.Exists(outputFile))
            {
                logFile.WriteLine("Failed: Output files not created");
                totalFailed++;
                return false;
            }

            using (FileStream input = File.OpenRead(fileName))
            using (FileStream output = File.OpenRead(outputFile))
            using (FileStream compressed = File.OpenRead(compressedFile))
            {
                totalFiles++;

                oldSize = input.Length;
                newSize = compressed.Length;

                logFile.Write($" {oldSize,8} {newSize,8} ");
                if (oldSize == 0) oldSize = 1;

                logFile.Write($"{100 - (newSize * 100 / oldSize),4}%  ");

                if (!FilesAreEqual(input, output))
                {
                    logFile.WriteLine("Failed");
                    totalFailed++;
                    return false;
                }
            }

            logFile.WriteLine("Passed");
            totalPassed++;
            return true;
        }
        catch (Exception ex)
        {
            totalFailed++;
            logFile.WriteLine($"Failed: {ex.Message}");
            return false;
        }
        finally
        {
            // Clean up temporary files
            try
            {
                if (File.Exists(compressedFile)) File.Delete(compressedFile);
                if (File.Exists(outputFile)) File.Delete(outputFile);
            }
            catch { }
        }
    }

    private static bool FilesAreEqual(FileStream file1, FileStream file2)
    {
        if (file1.Length != file2.Length) return false;

        file1.Position = 0;
        file2.Position = 0;

        const int bufferSize = 4096;
        byte[] buffer1 = new byte[bufferSize];
        byte[] buffer2 = new byte[bufferSize];

        int bytesRead1, bytesRead2;
        do
        {
            bytesRead1 = file1.Read(buffer1, 0, bufferSize);
            bytesRead2 = file2.Read(buffer2, 0, bufferSize);

            if (bytesRead1 != bytesRead2) return false;
            if (!buffer1.Take(bytesRead1).SequenceEqual(buffer2.Take(bytesRead2))) return false;
        } while (bytesRead1 > 0);

        return true;
    }

    private static void ExecuteCommand(string command)
    {
        ProcessStartInfo startInfo;
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
        else
        {
            startInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };
        }

        var process = new Process { StartInfo = startInfo };

        process.Start();

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine($"Command failed with exit code {process.ExitCode}: {command}");
            if (!string.IsNullOrWhiteSpace(error))
            {
                Console.Error.WriteLine($"Error: {error}");
            }
        }
        
        if (!string.IsNullOrWhiteSpace(output))
        {
            Console.WriteLine(output);
        }
    }

    private static void WriteLogHeader()
    {
        logFile.WriteLine("                                          Original   Packed");
        logFile.WriteLine("            File Name                     Size      Size   Ratio  Result");
        logFile.WriteLine("-------------------------------------     --------  --------  ----  ------");
    }

    private static void WriteLogSummary(DateTime startTime, DateTime stopTime)
    {
        logFile.WriteLine($"\nTotal elapsed time: {(stopTime - startTime).TotalSeconds:F2} seconds");
        logFile.WriteLine($"Total files:   {totalFiles}");
        logFile.WriteLine($"Total passed:  {totalPassed}");
        logFile.WriteLine($"Total failed:  {totalFailed}");
    }

    private static void UsageExit()
    {
        string usage = @"
CHURN 1.0. Usage: CHURN root-dir ""compress command"" ""expand command""

CHURN tests compression programs by compressing and expanding all files in a directory.

Examples:
  Windows:
   ./Churn C:\Dir ""Lzss-c.exe %s TEST.CMP"" ""Lzss-e.exe -d TEST.CMP TEST.OUT""
  
  Linux:
    ./Churn /home/user ""gzip -c %s > TEST.CMP"" ""gzip -d -c TEST.CMP > TEST.OUT""
    ./Churn /any/RootDir ""./Lzss-c %s TEST.CMP"" ""./Lzss-e TEST.CMP  TEST.OUT""

Note: Use %s as placeholder for input filename in compress command.
      Expand command should take TEST.CMP as input and produce TEST.OUT as output.
";

        Console.WriteLine(usage);
        Environment.Exit(1);
    }
}
