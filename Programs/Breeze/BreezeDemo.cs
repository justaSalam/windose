public static class BreezeDemo
{
    public const string Source = @"
// This entire window is constructed by Breeze.
let main = window(""Breeze Application"", 160, 120, 620, 400);
let root = windowRoot(main);

let menus = menuBar();
dock(root, menus, ""top"");
let fileMenu = menu(menus, ""File"");
let closeItem = menuItem(fileMenu, ""Close"");

let tools = toolbar();
dock(root, tools, ""top"");
let helloTool = toolbarButton(tools, ""Say hello"", 100);

let status = statusBar();
dock(root, status, ""bottom"");
let statusText = statusPanel(status, ""Ready"", 420);

let body = stackPanel(""vertical"");
dock(root, body, ""fill"");
let heading = panel(""This app was loaded by the Breeze runtime."", 28);
let input = textField(""Windose"", 26);
let hello = button(""Update status"", 150, 28);
stack(body, heading);
stack(body, input);
stack(body, hello);

on hello.click {
    set statusText.text = ""Hello, "" + value(input, ""text"");
}

on helloTool.click {
    set statusText.text = ""Toolbar event executed"";
}

on closeItem.click {
    close(main);
}

show(main);
";

    public static void Run() => BreezeHost.RunSource(Source);
}
