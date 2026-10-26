public static class Dbg
{
    public static void Dump(MES.Web.Services.LedgerRow row)
    {
        foreach (var G in row.Groups.Values)
        {
            System.Console.WriteLine($"  {G.Code}: ChuaLam={G.ChuaLam} ConGiao={G.ConGiao} LoiGiu={G.LoiGiu} SuaGiu={G.SuaGiu} Ton={G.Ton}");
            foreach (var s in G.Steps) System.Console.WriteLine($"     {s.Pg} {s.Code} excl={s.IsExcluded} prep={s.IsPrep} In={s.In} Ok={s.Ok} Ng={s.Ng} W={s.Waiting} HelpOut={s.HelpOut}");
        }
    }
}
