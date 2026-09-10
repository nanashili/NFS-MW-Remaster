using System;
using System.Collections.Generic;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    /// <summary>Folder labels only. Database IDs, variants and vehicle identity authoring remain separate.</summary>
    internal static class MostWantedCarFolders
    {
        private static readonly Dictionary<string, string> Players = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["911GT2"] = "Porsche/911 GT2", ["911TURBO"] = "Porsche/911 Turbo", ["997S"] = "Porsche/911 Carrera S",
            ["A3"] = "Audi/A3", ["A4"] = "Audi/A4", ["TT"] = "Audi/TT",
            ["BMWM3GTR"] = "BMW/M3 GTR", ["BMWM3GTRE46"] = "BMW/M3 E42",
            ["CAMARO"] = "Chevrolet/Camaro", ["COBALTSS"] = "Chevrolet/Cobalt SS",
            ["CORVETTE"] = "Chevrolet/Corvette", ["CORVETTEC6R"] = "Chevrolet/Corvette C6.R",
            ["CARRERAGT"] = "Porsche/Carrera GT", ["CAYMANS"] = "Porsche/Cayman S", ["CLIO"] = "Renault/Clio",
            ["CLK500"] = "Mercedes-Benz/CLK 500", ["SL500"] = "Mercedes-Benz/SL 500",
            ["SL65"] = "Mercedes-Benz/SL 65", ["SLR"] = "Mercedes-Benz/SLR McLaren",
            ["CTS"] = "Cadillac/CTS", ["DB9"] = "Aston Martin/DB9", ["ECLIPSEGT"] = "Mitsubishi/Eclipse GT",
            ["ELISE"] = "Lotus/Elise", ["FORDGT"] = "Ford/GT", ["MUSTANGGT"] = "Ford/Mustang GT",
            ["GALLARDO"] = "Lamborghini/Gallardo", ["MURCIELAGO"] = "Lamborghini/Murcielago",
            ["GTI"] = "Volkswagen/Golf GTI", ["GTO"] = "Pontiac/GTO", ["IMPREZAWRX"] = "Subaru/Impreza WRX",
            ["IS300"] = "Lexus/IS 300", ["LANCEREVO8"] = "Mitsubishi/Lancer Evolution VIII", ["MONARO"] = "Vauxhall/Monaro",
            ["PUNTO"] = "Fiat/Punto", ["RX7"] = "Mazda/RX-7", ["RX8"] = "Mazda/RX-8", ["RX8SPEEDT"] = "Mazda/RX-8 SpeedT",
            ["SUPRA"] = "Toyota/Supra", ["VIPER"] = "Dodge/Viper"
        };

        public static string ForModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model) || model.IndexOfAny(new[] { '/', '\\', ':', '.' }) >= 0)
                throw new ArgumentException("Invalid source model directory.");
            return Players.TryGetValue(model, out string path) ? path : (model.StartsWith("COP", StringComparison.OrdinalIgnoreCase) ? "Police/" : "Traffic/") + model.ToUpperInvariant();
        }
    }
}
