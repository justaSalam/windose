public struct Thickness
{
    public int top, bottom, right, left;

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

    public static Thickness All(int value) => new Thickness(value);

    public static Thickness FromLTRB(int left, int top, int right, int bottom)
        => new Thickness(top, bottom, right, left);
}