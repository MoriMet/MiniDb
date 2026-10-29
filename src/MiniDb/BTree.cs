namespace MiniDb;
// Minimum degree 16: at most 31 distinct keys per node. Duplicate keys share a row-id bucket.
public class BTree
{
    const int Degree = 16;
    class Node(bool leaf)
    {
        public bool Leaf = leaf;
        public List<object> Keys = new();
        public List<List<long>> Buckets = new();
        public List<Node> Children = new();
    }
    Node root = new(true);
    public int Height
    {
        get
        {
            int n = 1;
            var node = root;
            while (!node.Leaf)
            {
                n++;
                node = node.Children[0];
            }
            return n;
        }
    }
    static int Lower(Node n, object key)
    {
        int lo = 0, hi = n.Keys.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (ValueComparer.Instance.Compare(n.Keys[mid], key) < 0)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }
    public IReadOnlyList<long> Find(object key)
    {
        var n = root;
        while (true)
        {
            int i = Lower(n, key);
            if (i < n.Keys.Count && ValueComparer.Instance.Compare(n.Keys[i], key) == 0)
                return n.Buckets[i];
            if (n.Leaf)
                return Array.Empty<long>();
            n = n.Children[i];
        }
    }
    public void Add(object key, long rowId)
    {
        if (root.Keys.Count == 2 * Degree - 1)
        {
            var parent = new Node(false);
            parent.Children.Add(root);
            Split(parent, 0);
            root = parent;
        }
        Insert(root, key, rowId);
    }
    static void Split(Node parent, int position)
    {
        var left = parent.Children[position];
        var right = new Node(left.Leaf);
        parent.Keys.Insert(position, left.Keys[Degree - 1]);
        parent.Buckets.Insert(position, left.Buckets[Degree - 1]);
        parent.Children.Insert(position + 1, right);
        right.Keys.AddRange(left.Keys.Skip(Degree));
        right.Buckets.AddRange(left.Buckets.Skip(Degree));
        left.Keys.RemoveRange(Degree - 1, Degree);
        left.Buckets.RemoveRange(Degree - 1, Degree);
        if (!left.Leaf)
        {
            right.Children.AddRange(left.Children.Skip(Degree));
            left.Children.RemoveRange(Degree, Degree);
        }
    }
    static void Insert(Node n, object key, long rowId)
    {
        int i = Lower(n, key);
        if (i < n.Keys.Count && ValueComparer.Instance.Compare(n.Keys[i], key) == 0)
        {
            n.Buckets[i].Add(rowId);
            return;
        }
        if (n.Leaf)
        {
            n.Keys.Insert(i, key);
            n.Buckets.Insert(i, new() { rowId });
            return;
        }
        if (n.Children[i].Keys.Count == 2 * Degree - 1)
        {
            Split(n, i);
            int cmp = ValueComparer.Instance.Compare(key, n.Keys[i]);
            if (cmp == 0)
            {
                n.Buckets[i].Add(rowId);
                return;
            }
            if (cmp > 0)
                i++;
        }
        Insert(n.Children[i], key, rowId);
    }
    // Storage keeps node topology as well as keys/buckets; no data scan is needed on reopen.
    public byte[] Serialize()
    {
        var w = new RecordWriter();
        Write(root, w);
        return w.Bytes();
    }
    static void Write(Node n, RecordWriter w)
    {
        w.Int(n.Leaf ? 1 : 0);
        w.Int(n.Keys.Count);
        for (int i = 0; i < n.Keys.Count; i++)
        {
            w.Value(n.Keys[i]);
            w.Int(n.Buckets[i].Count);
            foreach (long id in n.Buckets[i])
                w.Int(id);
        }
        foreach (var child in n.Children)
            Write(child, w);
    }
    public static BTree Deserialize(byte[] bytes)
    {
        var r = new RecordReader(bytes);
        return new()
        {
            root = Read(r)
        };
    }
    static Node Read(RecordReader r)
    {
        var n = new Node(r.Int() == 1);
        long count = r.Int();
        if (count < 0 || count > 2 * Degree - 1)
            throw new SqlException("Повреждён узел индекса");
        for (int i = 0; i < count; i++)
        {
            n.Keys.Add(r.Value());
            long size = r.Int();
            if (size < 1 || size > int.MaxValue)
                throw new SqlException("Повреждён индекс");
            var bucket = new List<long>();
            for (int j = 0; j < size; j++)
                bucket.Add(r.Int());
            n.Buckets.Add(bucket);
        }
        if (!n.Leaf)
            for (int i = 0; i <= count; i++)
                n.Children.Add(Read(r));
        return n;
    }
}
public class DbIndex(string name, int column)
{
    public string Name { get; } = name;
    public int Column { get; } = column;
    public BTree Tree { get; set; } = new();
    public void Rebuild(Table table)
    {
        Tree = new();
        foreach (var r in table.Rows)
            Tree.Add(r.Value[Column], r.Key);
    }
}
