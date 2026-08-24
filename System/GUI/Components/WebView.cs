using System.Drawing;
using Microsoft.VisualBasic;

public class WebView : Component
{

    private const string html = """
<html>
    <body>
        <h1>Hello Windose From HTML</h1>
        <button id="test">Click me</button>
    </body>
</html>
""";




    public WebView(int x, int y, int width, int height) : base(x, y, width, height)
    {


       

    }


    public override void DrawLocal()
    {
        try
        {
            //DrawString($"{document.Body?.TextContent}", Color.Black, 0, 0);

        }
        catch (Exception ex)
        {
            DrawString(ex.Message, Color.Black, 0, 0);

        }
    }

    public override string GetName() => "ImageView";
}
