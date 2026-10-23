namespace Sacred.Network.Gameplay;

/// <summary>Gold unsigned-key AVL insertion (0x4BF460/0x4C4680), including duplicates and native height initialization.</summary>
internal sealed class GoldTriggerPatchMap
{
    private sealed class Node(uint key, uint value)
    {
        public uint Key { get; } = key;
        public uint Value { get; } = value;
        public Node? Left;
        public Node? Right;
        public int Height;
    }
    private Node? _root;
    public void Clear() => _root = null;
    public void Insert(uint key, uint value) => _root = Insert(_root, key, value);
    public bool TryGetValue(uint key, out uint value)
    {
        var node = _root;
        while (node is not null)
        {
            if (key == node.Key) { value = node.Value; return true; }
            node = key < node.Key ? node.Left : node.Right;
        }
        value = 0; return false;
    }
    public IEnumerable<(uint Key, uint TriggerReference)> Entries => Walk(_root);
    private static IEnumerable<(uint Key, uint TriggerReference)> Walk(Node? node)
    {
        if (node is null) yield break;
        foreach (var row in Walk(node.Left)) yield return row;
        yield return (node.Key, node.Value);
        foreach (var row in Walk(node.Right)) yield return row;
    }
    private static int Height(Node? node) => node?.Height ?? 0;
    private static int Balance(Node node) => Height(node.Right)-Height(node.Left);
    private static void Update(Node? node)
    {
        if (node is not null) node.Height = 1+Math.Max(Height(node.Left), Height(node.Right));
    }
    private static Node Insert(Node? node, uint key, uint value)
    {
        // Native freshly allocated leaves start at height zero, as do null pointers.
        if (node is null) return new(key, value);
        if (key < node.Key) node.Left = Insert(node.Left, key, value);
        else node.Right = Insert(node.Right, key, value);
        Update(node);
        if (Balance(node) > 1 && node.Right is { } right)
        {
            if (Balance(right) > 0) node = RotateLeft(node);
            else if (Balance(right) < 0) { node.Right = RotateRight(right); node = RotateLeft(node); }
        }
        if (Balance(node) < -1 && node.Left is { } left)
        {
            if (Balance(left) < 0) node = RotateRight(node);
            else if (Balance(left) > 0) { node.Left = RotateLeft(left); node = RotateRight(node); }
        }
        return node;
    }
    private static Node RotateLeft(Node node)
    {
        var root = node.Right!; node.Right = root.Left; root.Left = node;
        Update(root.Left); Update(root.Right); Update(root); return root;
    }
    private static Node RotateRight(Node node)
    {
        var root = node.Left!; node.Left = root.Right; root.Right = node;
        Update(root.Left); Update(root.Right); Update(root); return root;
    }
}
