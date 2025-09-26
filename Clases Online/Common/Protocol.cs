namespace Common;

public class Protocol
{
    public static int MessegeTypeLength = 3;
    public static int CommandStingLenght = 2;
    public static string request = "REQ";
    public static string Response = "RES";
    public const int MaxFileChunkSize = 32768; // 32k
    public const int FileNameLenghtSize = 4;
    public const int FileSizeLenghtSize = 8;

    public static long CalculateFileParts(long fileSize)
    {
        long fileParts = fileSize / MaxFileChunkSize;
        return fileParts * MaxFileChunkSize == fileSize ? fileParts : fileParts + 1;
    }

}

public enum Command
{
    Register,
    Login,
    CreateClass,
    Exit,
}
