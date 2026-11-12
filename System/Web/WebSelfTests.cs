using System;
using System.Collections.Generic;
using Windose.System.System_Calls;

public static class WebSelfTests
{
    private static int passed = 0;
    private static int failed = 0;

    public static bool Run()
    {
        passed = 0;
        failed = 0;
        TestParseSimple();
        TestInlineBlockLayout();
        TestTableColumnAlignment();
        TestFloatAndClear();
        TestCssCaching();

        SystemLogger.WriteLine("WebSelfTests", "Completed: " + passed + " passed, " + failed + " failed.", 
            failed == 0 ? ConsoleMessageType.Log : ConsoleMessageType.Error);
        return failed == 0;
    }

    private static void TestParseSimple()
    {
        DomNode doc = HtmlDom.Parse("<div><p>Hello</p></div>");
        Assert(doc != null, "doc != null");
        Assert(doc.FirstElementChild() != null, "doc has root element");
        DomNode div = doc.FirstElementChild();
        Assert(div != null && div.Tag == "div", "root element is div");
        DomNode p = div.FirstElementChild();
        Assert(p != null && p.Tag == "p", "div child is p");
    }

    private static void TestInlineBlockLayout()
    {
        string html = "<html><body><div style='display:inline-block; width:120px; height:40px; background:#ff0000;'>Box</div></body></html>";
        DomNode doc = HtmlDom.Parse(html);
        RenderPlan plan = HtmlLayout.Build(doc, "", 800, 600, 16, (s, sz) => s.Length * 8, null, "");

        bool foundBox = false;
        for (int i = 0; i < plan.Boxes.Count; i++)
        {
            if (plan.Boxes[i].W == 120 && plan.Boxes[i].H == 40)
            {
                foundBox = true;
                break;
            }
        }
        Assert(foundBox, "inline-block must produce a 120x40 RenderBox");
    }

    private static void TestTableColumnAlignment()
    {
        string html = @"<html><body><table>
            <tr><td style='width:100px;'>Short</td><td>Auto Col</td></tr>
            <tr><td>Longer First Cell</td><td>Auto Col 2</td></tr>
        </table></body></html>";
        DomNode doc = HtmlDom.Parse(html);
        RenderPlan plan = HtmlLayout.Build(doc, "", 800, 600, 16, (s, sz) => s.Length * 8, null, "");

        Assert(plan != null && plan.Lines.Count > 0, "Table rendered lines");
    }

    private static void TestFloatAndClear()
    {
        string html = @"<html><body>
            <div style='float:left; width:100px; height:50px;'>Float</div>
            <p>Text beside float</p>
            <div style='clear:both;'>Cleared</div>
        </body></html>";
        DomNode doc = HtmlDom.Parse(html);
        RenderPlan plan = HtmlLayout.Build(doc, "", 800, 600, 16, (s, sz) => s.Length * 8, null, "");

        Assert(plan != null, "Plan built with floats");
    }

    private static void TestCssCaching()
    {
        string css = "body { margin: 10px; } .card { width: 100px; }";
        CssSheet sheet = CssSheet.Parse(css);
        DomNode doc = HtmlDom.Parse("<div class='card'>Card</div>");
        RenderPlan plan1 = HtmlLayout.Build(doc, css, 800, 600, 16, (s, sz) => s.Length * 8, null, "");
        RenderPlan plan2 = HtmlLayout.Build(doc, sheet, 800, 600, 16, (s, sz) => s.Length * 8, null, "");
        Assert(plan1 != null, "Plan1 built with css");
        Assert(plan2 != null, "Plan2 built with cached sheet");
    }

    public static void Assert(bool condition, string message)
    {
        if (condition)
        {
            passed++;
        }
        else
        {
            failed++;
            SystemLogger.WriteLine("WebSelfTests", "FAILED: " + message, ConsoleMessageType.Error);
        }
    }
}
