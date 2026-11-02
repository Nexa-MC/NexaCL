namespace Nexa.Platform.Updates;

/// <summary>Retains an update namespace. System and unprivileged portable adapters have distinct admission policies. Names are single leaves.</summary>
public interface IUpdateDirectory : IDisposable
{
    string Path { get; }
    IUpdateDirectory OpenDirectory(string name);
    IUpdateDirectory CreateDirectory(string name, bool publicRead);
    FileStream OpenRead(string name);
    FileStream OpenState(string name, bool publicRead = false, bool exclusive = true);
    FileStream CreateFile(string name, bool publicRead = false, bool executable = false);
    void Flush();
}
