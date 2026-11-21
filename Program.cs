using Velopack;

namespace GaleriaDesktop;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Obsługa instalacji/aktualizacji Velopack; przy zdarzeniach instalatora kończy proces
        VelopackApp.Build().Run();
        XamlGeneratedProgram.XamlGeneratedMain();
    }
}
