using System;
using System.Collections.Generic;
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

    public static string ExportCSharp(Component root, string className = "GeneratedWindow")
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Drawing;");
        sb.AppendLine("using Windose;");
        sb.AppendLine();
        sb.AppendLine($"public class {className} : Window");
        sb.AppendLine("{");
        sb.AppendLine($"    public {className}(int x, int y) : base(x, y, {Math.Max(300, root.Width + 16)}, {Math.Max(200, root.Height + 36)}, \"{className}\", true)");
        sb.AppendLine("    {");
        sb.AppendLine("        InitializeComponent();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private void InitializeComponent()");
        sb.AppendLine("    {");

        int counter = 1;
        EmitCSharpChildren(root, sb, "this", ref counter);

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitCSharpChildren(Component parent, StringBuilder sb, string parentVar, ref int counter)
    {
        List<Component> kids;
        lock (parent.children) { kids = new List<Component>(parent.children); }
        foreach (var c in kids)
        {
            var d = UiRegistry.For(c);
            if (d == null) continue;
            string varName = !string.IsNullOrEmpty(c.name) ? c.name : $"ctrl{counter++}";
            string typeName = d.TypeName;
            sb.AppendLine($"        var {varName} = ({typeName})UiRegistry.Types[\"{typeName}\"].Create();");
            sb.AppendLine($"        {varName}.name = \"{varName}\";");
            sb.AppendLine($"        {varName}.SetBounds({c.X}, {c.Y}, {c.Width}, {c.Height});");
            if (!string.IsNullOrEmpty(c.text))
                sb.AppendLine($"        {varName}.text = \"{c.text.Replace("\"", "\\\"")}\";");
            if (c.dock != Dock.None)
                sb.AppendLine($"        {varName}.dock = Dock.{c.dock};");

            if (parentVar == "this")
                sb.AppendLine($"        AddChild({varName});");
            else
                sb.AppendLine($"        {parentVar}.AddChild({varName});");

            if (d.IsContainer)
                EmitCSharpChildren(c, sb, varName, ref counter);

            sb.AppendLine();
        }
    }

    public static string ExportElfC(Component root, string appName = "app")
    {
        var sb = new StringBuilder();
        sb.AppendLine("/**");
        sb.AppendLine($" * Native Windose ELF C Application: {appName}");
        sb.AppendLine(" */");
        sb.AppendLine("#include <stdio.h>");
        sb.AppendLine("#include <stdlib.h>");
        sb.AppendLine("#include <string.h>");
        sb.AppendLine("#include \"kernel.h\"");
        sb.AppendLine();
        sb.AppendLine("static handle_t main_win = 0;");

        List<Component> allControls = new List<Component>();
        CollectControls(root, allControls);
        foreach (var c in allControls)
        {
            if (c.designType != "DockPanel" && c.designType != "Panel")
                sb.AppendLine($"static handle_t h_{c.name} = 0;");
        }
        sb.AppendLine();
        sb.AppendLine("int main(int argc, char **argv) {");
        sb.AppendLine($"    main_win = k_create_window({root.Width}, {root.Height}, \"{appName}\");");
        sb.AppendLine("    if (!main_win) return 1;");
        sb.AppendLine();

        foreach (var c in allControls)
        {
            string dt = c.designType;
            if (dt == "Button")
                sb.AppendLine($"    h_{c.name} = k_create_button(main_win, {c.X}, {c.Y}, {c.Width}, {c.Height}, \"{c.text}\");");
            else if (dt == "Label" || dt == "LinkLabel")
                sb.AppendLine($"    h_{c.name} = k_create_label(main_win, {c.X}, {c.Y}, \"{c.text}\");");
            else if (dt == "TextField")
            {
                sb.AppendLine($"    h_{c.name} = k_create_textfield(main_win, {c.X}, {c.Y}, {c.Width});");
                sb.AppendLine($"    k_textfield_setup(h_{c.name}, 256, 0, 0);");
            }
            else if (dt == "Checkbox")
                sb.AppendLine($"    h_{c.name} = k_create_checkbox(main_win, {c.X}, {c.Y}, \"{c.text}\");");
            else if (dt == "RadioButton")
                sb.AppendLine($"    h_{c.name} = k_create_radio(main_win, {c.X}, {c.Y}, \"{c.text}\", \"group1\");");
            else if (dt == "Slider")
                sb.AppendLine($"    h_{c.name} = k_create_slider(main_win, {c.X}, {c.Y}, {c.Width}, 1, 0, 100, 50);");
            else if (dt == "ProgressBar")
            {
                sb.AppendLine($"    h_{c.name} = k_create_progress(main_win, {c.X}, {c.Y}, {c.Width}, {c.Height});");
                sb.AppendLine($"    k_progress_setup(h_{c.name}, 0, 100);");
                sb.AppendLine($"    k_progress_set(h_{c.name}, 50);");
            }
            else if (dt == "ComboBox")
                sb.AppendLine($"    h_{c.name} = k_create_combo(main_win, {c.X}, {c.Y}, {c.Width});");
            else if (dt == "ListView")
                sb.AppendLine($"    h_{c.name} = k_create_listbox(main_win, {c.X}, {c.Y}, {c.Width}, {c.Height});");
            else if (dt == "NumericUpDown")
                sb.AppendLine($"    h_{c.name} = k_numeric_create(main_win, {c.X}, {c.Y}, {c.Width}, 0, 100, 1, 0);");
            else if (dt == "Panel")
                sb.AppendLine($"    k_fill_rect(main_win, {c.X}, {c.Y}, {c.Width}, {c.Height}, 0xFFE0E0E0);");
        }

        sb.AppendLine();
        sb.AppendLine("    for (;;) {");
        sb.AppendLine("        Event e;");
        sb.AppendLine("        while (k_poll_event(&e)) {");
        sb.AppendLine("            if (e.type == EV_CLOSE && e.handle == main_win) {");
        sb.AppendLine("                k_close(main_win);");
        sb.AppendLine("                return 0;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("        k_sleep(10);");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void CollectControls(Component c, List<Component> list)
    {
        List<Component> kids;
        lock (c.children) { kids = new List<Component>(c.children); }
        foreach (var k in kids)
        {
            list.Add(k);
            if (UiRegistry.IsContainer(k))
                CollectControls(k, list);
        }
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
