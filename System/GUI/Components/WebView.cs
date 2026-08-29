using System.Drawing;
using HtmlAgilityPack;
using Microsoft.VisualBasic;

public class WebView : Component
{

    private const string test = """
<html>
    <body>
        <h1>Hello Windose From HTML</h1>
        <button id="test">Click me</button>
    </body>
</html>
""";



    private HtmlDocument document;
 
    public WebView(int x, int y, int width, int height) : base(x, y, width, height)
    {
        document = new HtmlDocument();
        document.LoadHtml(test);

       

    }


    public override void DrawLocal()
    {
        try
        {
            var button = document.GetElementbyId("test");
            DrawString($"{button?.Name}", Color.Black, 0, 20);
            DrawString($"{button?.InnerText}", Color.Black, 0, 0);

        }
        catch (Exception ex)
        {
            DrawString(ex.Message, Color.Black, 0, 0);

        }
    }

    public override string GetComponentName() => "ImageView";
}
