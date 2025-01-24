using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

//dotnet run "C:\RootDir" "compress_command %s TEST.CMP" "expand_command TEST.CMP TEST.OUT"

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

        command = string.Format(compressCommand, fileName);
        ExecuteCommand(command);

        command = string.Format(expandCommand, fileName);
        ExecuteCommand(command);

        try
        {
            using (FileStream input = File.OpenRead(fileName))
            using (FileStream output = File.OpenRead("TEST.OUT"))
            using (FileStream compressed = File.OpenRead("TEST.CMP"))
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
    }

    private static bool FilesAreEqual(FileStream file1, FileStream file2)
    {
        if (file1.Length != file2.Length) return false;

        int byte1, byte2;
        do
        {
            byte1 = file1.ReadByte();
            byte2 = file2.ReadByte();

            if (byte1 != byte2) return false;
        } while (byte1 != -1);

        return true;
    }

    private static void ExecuteCommand(string command)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo("cmd.exe", $"/c {command}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        process.WaitForExit();
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

Example:
  CHURN C:\ ""LZSS-C %s TEST.CMP"" ""LZSS-C TEST.CMP TEST.OUT""
";

        Console.WriteLine(usage);
        Environment.Exit(1);
    }
}

