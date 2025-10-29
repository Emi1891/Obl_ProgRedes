// Protocol.cs
namespace Common
{
    public static class Protocol
    {
        public const int MessageTypeLength = 3;   // "REQ" / "RES"
        public const int CommandStringLength = 2;   // "00", "01", etc.
        public const string request = "REQ";
        public const string Response = "RES";

        public const int MaxFileChunkSize = 32768;
        public const int FileNameLenghtSize = 4;
        public const int FileSizeLenghtSize = 8;

        public static long CalculateFileParts(long fileSize)
        {
            long fileParts = fileSize / MaxFileChunkSize;
            return (fileParts * MaxFileChunkSize == fileSize) ? fileParts : fileParts + 1;
        }
    }

    public enum Command
    {
        Register = 0,
        Login = 1,
        Exit = 2
    }

    public enum CommandLogged
    {
        CreateClass = 0,
        JoinClass = 1,
        ListClasses = 2,
        LeaveClass = 3,
        EditClass = 4,
        History = 5,
        Exit = 6
    }
}
