using QuestPDF.Drawing;
using System.Reflection;

namespace GameHubz.Logic.Fonts
{
    public static class FontRegistration
    {
        public static void RegisterEmbeddedFonts()
        {
            var assembly = Assembly.GetExecutingAssembly();

            RegisterFont(assembly, "GameHubz.Logic.Fonts.Inter-Regular.ttf");
            RegisterFont(assembly, "GameHubz.Logic.Fonts.Inter-Bold.ttf");
        }

        private static void RegisterFont(Assembly assembly, string resourceName)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);

            if (stream == null)
                throw new InvalidOperationException($"Embedded font resource '{resourceName}' not found.");

            FontManager.RegisterFontWithCustomName(
                resourceName.Contains("Bold") ? "Inter-Bold" : "Inter",
                stream);
        }
    }
}
