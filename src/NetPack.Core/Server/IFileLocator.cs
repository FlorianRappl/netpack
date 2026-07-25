namespace NetPack.Server;

interface IFileLocator
{
    bool HasFile(string fullPath);

    bool HasDirectory(string fullPath);
}
