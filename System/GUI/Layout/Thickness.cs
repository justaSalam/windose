public struct Thickness
{
    public int top, bottom, right, left = 5;

    public Thickness(int value)
    {
        top = bottom = right = left = value;
    }

    public Thickness(int top, int bottom, int right, int left)
    {
        this.top = top;
        this.bottom = bottom;
        this.right = right;
        this.left = left;
    }
}