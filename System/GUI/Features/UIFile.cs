using System.Text;

public static class UiFile
{
    public static string Save(Component root)
    {
        var sb = new StringBuilder();
        WriteNode(root, sb, 0);
        return sb.ToString();
    }

    private static void WriteNode(Component c, StringBuilder sb, int depth)
    {
        var d = UiRegistry.For(c);
        if (d == null) return;

        sb.Append(' ', depth * 2).Append(d.TypeName);
        foreach (var p in d.Properties)
        {
            string v = (p.Get(c) ?? "").Replace('"', '\'').Replace('\n', ' ').Replace('\r', ' ');
            sb.Append(' ').Append(p.Name).Append("=\"").Append(v).Append('"');
        }
        sb.Append('\n');

        if (!d.IsContainer) return;

        List<Component> kids;
        lock (c.children) { kids = new List<Component>(c.children); }
        foreach (var k in kids) WriteNode(k, sb, depth + 1);
    }

    public static Component? Load(string text) => Load(text.Split('\n'));

    public static Component? Load(string[] lines)
    {
        var stack = new List<Component>();
        Component? root = null;

        foreach (string raw in lines)
        {
            string line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0) continue;

            int indent = 0;
            while (indent < line.Length && line[indent] == ' ') indent++;
            int depth = Math.Min(indent / 2, stack.Count);
            int pos = indent;

            string type = ReadUntil(line, ref pos, ' ');
            if (!UiRegistry.Types.TryGetValue(type, out var d)) continue;
            Component c = d.Create();

            while (pos < line.Length)
            {
                while (pos < line.Length && line[pos] == ' ') pos++;
                if (pos >= line.Length) break;
                string key = ReadUntil(line, ref pos, '=');
                pos++;                                         // opening quote
                string value = ReadUntil(line, ref pos, '"');
                d.Find(key)?.Set(c, value);
            }

            while (stack.Count > depth) stack.RemoveAt(stack.Count - 1);

            if (depth == 0 || stack.Count == 0)
            {
                if (root == null) root = c;
            }
            else
            {
                Component parent = stack[stack.Count - 1];
                if (parent is DockPanel dp) dp.AddDockChild(c, c.dock);
                else parent.AddChild(c);
            }

            stack.Add(c);
        }

        return root;
    }

    private static string ReadUntil(string s, ref int pos, char stop)
    {
        int start = pos;
        while (pos < s.Length && s[pos] != stop) pos++;
        string r = s.Substring(start, pos - start);
        pos++;
        return r;
    }
}