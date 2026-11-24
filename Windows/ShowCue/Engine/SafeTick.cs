namespace ShowCue.Engine;

/// <summary>Kjører ett steg i tidsløkka uten at et unntak slipper ut. Tidsløkka går hver 16. ms; et unntak som slapp ut
/// ville gitt samme feilmelding igjen og igjen midt i et show.</summary>
public static class SafeTick
{
    /// <summary>Kjører <paramref name="work"/>. Returnerer true hvis det gikk bra; ellers false, og <paramref name="error"/> har meldingen.</summary>
    public static bool Run(Action work, out string error)
    {
        try
        {
            work();
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            return false;
        }
    }
}
