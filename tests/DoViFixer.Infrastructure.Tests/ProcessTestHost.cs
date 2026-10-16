using System.Diagnostics;
using System.Text;
using DoViFixer.Infrastructure.MediaTools.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace DoViFixer.Infrastructure.Tests;

// Runs only when explicitly launched as a fixture. It never touches real media or application settings.
internal static class ProcessTestHost
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0)
        {
            return 2;
        }

        string mode = args[0];
        if (mode == "lock")
        {
            using var held = new FileStream(args[1], FileMode.Open, FileAccess.Read, FileShare.Read);
            await File.WriteAllTextAsync(args[2], Environment.ProcessId.ToString());
            await Task.Delay(Timeout.Infinite);
        }
        if (mode == "echo")
        {
            Console.WriteLine(args[1]);
            Console.Error.WriteLine("diagnostic");
            return int.Parse(args[2]);
        }

        if (mode is "host-single" or "host-pipeline")
        {
            var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
            Task operation = mode == "host-single"
                ? runner.RunAsync(Request("tree", args[1], "single"), default)
                : runner.PipeAsync(Request("tree", args[1], "producer"), Request("tree", args[1], "consumer"), default);
            Release(args[1]);
            await operation;
            return 0;
        }

        if (mode is "tree" or "orphan" or "leaf")
        {
            string directory = args[1];
            string name = args[2];
            string pidPath = Path.Combine(directory, name + ".pid");
            await File.WriteAllTextAsync(pidPath + ".tmp", Environment.ProcessId.ToString());
            File.Move(pidPath + ".tmp", pidPath);
            if (mode != "leaf")
            {
                while (!File.Exists(Path.Combine(directory, "release")))
                {
                    await Task.Delay(10);
                }

                using var child = Process.Start(StartInfo("leaf", directory, name + "-child"))!;
                while (!File.Exists(Path.Combine(directory, name + "-child.pid")))
                {
                    await Task.Delay(10);
                }

                if (mode == "orphan")
                {
                    return 0;
                }
            }

            await Task.Delay(Timeout.Infinite);
        }

        return 2;
    }

    internal static ProcessRequest Request(params string[] args)
    {
        var start = StartInfo(args);
        return new(start.FileName, start.ArgumentList.ToArray());
    }

    internal static ProcessStartInfo StartInfo(params string[] args)
    {
        string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        var start = new ProcessStartInfo(dotnet)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(typeof(ProcessTestHost).Assembly.Location);
        foreach (string argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    internal static void Release(string directory) => File.WriteAllText(Path.Combine(directory, "release"), "ready");
}
