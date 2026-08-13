namespace Civ6WorkshopUploader;

public static class Log
{
    private static readonly FileStream FileStream;
    private static readonly StreamWriter StreamWriter;

    static Log()
    {
        FileStream = new FileStream("civ6-uploader.log", FileMode.Create);
        StreamWriter = new StreamWriter(FileStream);
    }

    public static void Info(string log)
    {
        lock (StreamWriter)
        {
            Console.WriteLine(log);
            StreamWriter.WriteLine(log);
        }
    }

    public static void Warn(string log)
    {
        lock (StreamWriter)
        {
            Console.WriteLine($"\x1b[33m{log}\x1b[0m");
            StreamWriter.WriteLine(log);
        }
    }

    public static void Error(string log)
    {
        lock (StreamWriter)
        {
            Console.WriteLine($"\x1b[31m{log}\x1b[0m");
            StreamWriter.WriteLine(log);
        }
    }

    public static void Close()
    {
        lock (StreamWriter)
        {
            StreamWriter.Close();
            FileStream.Close();
        }
    }
}