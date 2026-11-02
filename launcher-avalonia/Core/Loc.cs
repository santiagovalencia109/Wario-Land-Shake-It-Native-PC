namespace WarioLauncher.Core
{
    // Textos del lanzador en español o inglés.
    public static class Loc
    {
        public static bool English;
        public static string T(string es, string en) => English ? en : es;
    }
}
