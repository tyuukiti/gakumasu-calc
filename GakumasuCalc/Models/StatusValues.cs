namespace GakumasuCalc.Models;

public class StatusValues
{
    public int Vo { get; set; }
    public int Da { get; set; }
    public int Vi { get; set; }

    public int Total => Vo + Da + Vi;

    public StatusValues() { }

    public StatusValues(int vo, int da, int vi)
    {
        Vo = vo;
        Da = da;
        Vi = vi;
    }

    public static StatusValues Zero => new(0, 0, 0);

    public StatusValues Add(StatusValues other)
    {
        return new StatusValues(Vo + other.Vo, Da + other.Da, Vi + other.Vi);
    }

    public StatusValues MultiplyAndFloor(double factor)
    {
        return new StatusValues(
            (int)Math.Floor(Vo * factor),
            (int)Math.Floor(Da * factor),
            (int)Math.Floor(Vi * factor));
    }

    public StatusValues Clone() => new(Vo, Da, Vi);

    public override string ToString() => $"Vo:{Vo} Da:{Da} Vi:{Vi} (Total:{Total})";
}
