namespace MikuSB.Data;

public abstract class ExcelResource
{
    public abstract uint GetId();

    public virtual void Loaded()
    {
    }

    public virtual void Finalized()
    {
    }

    public virtual void AfterAllDone()
    {
    }
}