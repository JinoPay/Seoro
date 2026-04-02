namespace Cominomi.Shared.Models;

public class FileNode
{
    public bool IsDirectory { get; set; }
    public List<FileNode> Children { get; set; } = new();
    public string FullPath { get; set; } = "";
    public string Name { get; set; } = "";
}