namespace MikuSB.Common.Util;

public class Position
{
    public Position(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    //public Position(Proto.Position position)
    //{
    //    X = position.X;
    //    Y = position.Y;
    //    Z = position.Z;
    //}

    public Position()
    {
        X = 0;
        Y = 0;
        Z = 0;
    }

    public Position(Position position)
    {
        X = position.X;
        Y = position.Y;
        Z = position.Z;
    }

    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public void Set(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public void Set(Position position)
    {
        X = position.X;
        Y = position.Y;
        Z = position.Z;
    }

    //public void Set(Vector vector)
    //{
    //    X = vector.X;
    //    Y = vector.Y;
    //    Z = vector.Z;
    //}

    public void Add(float x, float y, float z)
    {
        X += x;
        Y += y;
        Z += z;
    }

    public void Add(Position position)
    {
        X += position.X;
        Y += position.Y;
        Z += position.Z;
    }

    public void Sub(float x, float y, float z)
    {
        X -= x;
        Y -= y;
        Z -= z;
    }

    public void Sub(Position position)
    {
        X -= position.X;
        Y -= position.Y;
        Z -= position.Z;
    }

    public void Mul(float x, float y, float z)
    {
        X *= x;
        Y *= y;
        Z *= z;
    }

    public void Mul(Position position)
    {
        X *= position.X;
        Y *= position.Y;
        Z *= position.Z;
    }

    public void Div(float x, float y, float z)
    {
        X /= x;
        Y /= y;
        Z /= z;
    }

    public void Div(Position position)
    {
        X /= position.X;
        Y /= position.Y;
        Z /= position.Z;
    }

    public double Distance(Position position)
    {
        return Math.Sqrt((X - position.X) * (X - position.X) + (Y - position.Y) * (Y - position.Y) +
                         (Z - position.Z) * (Z - position.Z));
    }

    //public Proto.Position ToProto()
    //{
    //    return new Proto.Position
    //    {
    //        X = (int)X,
    //        Y = (int)Y,
    //        Z = (int)Z
    //    };
    //}
}