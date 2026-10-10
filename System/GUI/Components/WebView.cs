using System.Drawing;

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



    public WebView(int x, int y, int width, int height) : base(x, y, width, height)
    {

       

    }


    public override void DrawLocal()
    {
        try
        {


        }
        catch (Exception ex)
        {
            DrawString(ex.Message, Color.Black, 0, 0);

        }
    }

    public override string GetComponentName() => "ImageView";
}
