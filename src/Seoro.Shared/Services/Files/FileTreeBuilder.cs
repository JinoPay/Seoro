namespace Seoro.Shared.Services.Files;

/// <summary>
///     익스플로러 파일 트리 구축/증분 갱신 헬퍼.
///     SidebarExplorer 에서 분리해 단위 테스트 가능하게 만든 순수 로직.
///     경로는 git 이 돌려주는 '/' 구분 상대 경로를 그대로 사용한다.
/// </summary>
public static class FileTreeBuilder
{
    /// <summary>평면 파일 목록을 정렬된 중첩 트리로 구축한다.</summary>
    public static List<FileNode> Build(IReadOnlyList<string> files)
    {
        var root = new List<FileNode>();
        var dirMap = new Dictionary<string, FileNode>();

        foreach (var file in files)
        {
            var parts = file.Split('/');
            var currentList = root;
            var currentPath = "";

            for (var i = 0; i < parts.Length; i++)
            {
                currentPath = i == 0 ? parts[i] : $"{currentPath}/{parts[i]}";
                var isLast = i == parts.Length - 1;

                if (isLast)
                {
                    currentList.Add(new FileNode { Name = parts[i], FullPath = file, IsDirectory = false });
                }
                else
                {
                    if (!dirMap.TryGetValue(currentPath, out var dirNode))
                    {
                        dirNode = new FileNode { Name = parts[i], FullPath = currentPath, IsDirectory = true };
                        dirMap[currentPath] = dirNode;
                        currentList.Add(dirNode);
                    }

                    currentList = dirNode.Children;
                }
            }
        }

        SortNodes(root);
        return root;
    }

    /// <summary>
    ///     파일 하나를 트리에 추가한다 — 중간 디렉터리는 자동 생성, 정렬 위치에 삽입.
    ///     이미 존재하는 경로면 아무것도 하지 않는다 (멱등).
    /// </summary>
    public static void AddPath(List<FileNode> root, string path)
    {
        var parts = path.Split('/');
        var currentList = root;
        var currentPath = "";

        for (var i = 0; i < parts.Length; i++)
        {
            currentPath = i == 0 ? parts[i] : $"{currentPath}/{parts[i]}";
            var isLast = i == parts.Length - 1;

            if (isLast)
            {
                if (currentList.Any(n => !n.IsDirectory && n.FullPath == path))
                    return;
                InsertSorted(currentList, new FileNode { Name = parts[i], FullPath = path, IsDirectory = false });
            }
            else
            {
                var dirNode = currentList.FirstOrDefault(n => n.IsDirectory && n.FullPath == currentPath);
                if (dirNode == null)
                {
                    dirNode = new FileNode { Name = parts[i], FullPath = currentPath, IsDirectory = true };
                    InsertSorted(currentList, dirNode);
                }

                currentList = dirNode.Children;
            }
        }
    }

    /// <summary>
    ///     파일 하나를 트리에서 제거한다 — 비게 된 중간 디렉터리도 가지치기.
    ///     존재하지 않는 경로면 아무것도 하지 않는다.
    /// </summary>
    public static void RemovePath(List<FileNode> root, string path)
    {
        RemovePathRecursive(root, path.Split('/'), 0, "");
    }

    /// <summary>이전/현재 파일 목록의 차이를 계산한다.</summary>
    public static (List<string> Added, List<string> Removed) ComputeDiff(
        HashSet<string> oldFiles, IReadOnlyList<string> newFiles)
    {
        var added = newFiles.Where(f => !oldFiles.Contains(f)).ToList();
        var newSet = newFiles as HashSet<string> ?? newFiles.ToHashSet();
        var removed = oldFiles.Where(f => !newSet.Contains(f)).ToList();
        return (added, removed);
    }

    private static bool RemovePathRecursive(List<FileNode> nodes, string[] parts, int depth, string currentPath)
    {
        var segment = parts[depth];
        currentPath = depth == 0 ? segment : $"{currentPath}/{segment}";
        var isLast = depth == parts.Length - 1;

        if (isLast)
        {
            var index = nodes.FindIndex(n => !n.IsDirectory && n.FullPath == currentPath);
            if (index < 0) return false;
            nodes.RemoveAt(index);
            return true;
        }

        var dirIndex = nodes.FindIndex(n => n.IsDirectory && n.FullPath == currentPath);
        if (dirIndex < 0) return false;

        var dir = nodes[dirIndex];
        var removed = RemovePathRecursive(dir.Children, parts, depth + 1, currentPath);
        if (removed && dir.Children.Count == 0)
            nodes.RemoveAt(dirIndex);
        return removed;
    }

    private static void InsertSorted(List<FileNode> nodes, FileNode node)
    {
        var index = 0;
        while (index < nodes.Count && Compare(nodes[index], node) < 0)
            index++;
        nodes.Insert(index, node);
    }

    private static int Compare(FileNode a, FileNode b)
    {
        if (a.IsDirectory != b.IsDirectory)
            return a.IsDirectory ? -1 : 1;
        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static void SortNodes(List<FileNode> nodes)
    {
        nodes.Sort(Compare);

        foreach (var node in nodes.Where(node => node is { IsDirectory: true, Children.Count: > 0 }))
            SortNodes(node.Children);
    }
}
